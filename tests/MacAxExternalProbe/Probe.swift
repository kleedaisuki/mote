// External, process-independent macOS Accessibility contract probe for mote.
// Compile with: swiftc -O Probe.swift -o <repository .temp path>/mote-ax-probe
import ApplicationServices
import Foundation

/// A single independently observed assertion, including the AX error when relevant.
struct Check: Codable {
    let name: String
    let passed: Bool
    let detail: String
}

/// Machine-readable result; the runner retains it even on permission denial.
struct Report: Codable {
    let status: String
    let editorPID: Int32
    let clientPID: Int32
    let checks: [Check]
    let note: String
}

/// A bounded external AX client. It does not link to or invoke mote internals.
final class Probe {
    private let pid: pid_t
    private let expected: NSString
    private let app: AXUIElement
    private var checks: [Check] = []

    init(pid: pid_t, fixture: String) throws {
        self.pid = pid
        expected = (try String(contentsOfFile: fixture, encoding: .utf8)) as NSString
        app = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(app, 2.0)
    }

    private func record(_ name: String, _ condition: Bool, _ detail: String) {
        checks.append(Check(name: name, passed: condition, detail: detail))
    }

    private func attribute(_ element: AXUIElement, _ name: String) -> (AXError, AnyObject?) {
        var value: CFTypeRef?
        let error = AXUIElementCopyAttributeValue(element, name as CFString, &value)
        return (error, value)
    }

    private func parameter(_ element: AXUIElement, _ name: String, _ input: AnyObject) -> (AXError, AnyObject?) {
        var value: CFTypeRef?
        let error = AXUIElementCopyParameterizedAttributeValue(element, name as CFString, input, &value)
        return (error, value)
    }

    private func rangeValue(_ object: AnyObject?) -> CFRange? {
        guard let value = object as? AXValue, AXValueGetType(value) == .cfRange else { return nil }
        var range = CFRange(location: 0, length: 0)
        return AXValueGetValue(value, .cfRange, &range) ? range : nil
    }

    private func axRange(_ location: Int, _ length: Int) -> AXValue {
        var range = CFRange(location: location, length: length)
        return AXValueCreate(.cfRange, &range)!
    }

    private func text(_ element: AXUIElement, _ location: Int, _ length: Int) -> (AXError, String?) {
        let (error, result) = parameter(element, "AXStringForRange", axRange(location, length))
        return (error, result as? String)
    }

    private func sourceLines() -> [NSRange] {
        let units = Array((expected as String).utf16)
        var lines: [NSRange] = []
        var start = 0
        var cursor = 0
        while cursor < units.count {
            if units[cursor] == 13 {
                cursor += 1
                if cursor < units.count && units[cursor] == 10 { cursor += 1 }
                lines.append(NSRange(location: start, length: cursor - start))
                start = cursor
            } else if units[cursor] == 10 {
                cursor += 1
                lines.append(NSRange(location: start, length: cursor - start))
                start = cursor
            } else { cursor += 1 }
        }
        lines.append(NSRange(location: start, length: units.count - start))
        return lines
    }

    private func findEditor() -> (AXUIElement?, Int, Int, String) {
        var queue: [(AXUIElement, Int)] = [(app, 0)]
        var cursor = 0
        var textAreas = 0
        var sourceBacked: [AXUIElement] = []
        var sourceLabelAttribute = "<missing>"
        let tail = expected.range(of: "OFFSCREEN-TARGET-😀\n")
        while cursor < queue.count && cursor < 256 {
            let (element, depth) = queue[cursor]
            cursor += 1
            let role = attribute(element, "AXRole").1 as? String
            if role == "AXTextArea" {
                textAreas += 1
                let count = (attribute(element, "AXNumberOfCharacters").1 as? NSNumber)?.intValue
                if count == expected.length && tail.location != NSNotFound {
                    let (error, value) = text(element, tail.location, tail.length)
                    if error == .success && value == "OFFSCREEN-TARGET-😀\n" {
                        sourceBacked.append(element)
                        if attribute(element, "AXDescription").1 as? String == "Mote editor" {
                            sourceLabelAttribute = "AXDescription"
                        } else if attribute(element, "AXTitle").1 as? String == "Mote editor" {
                            sourceLabelAttribute = "AXTitle"
                        }
                    }
                }
            }
            if depth < 10, let children = attribute(element, "AXChildren").1 as? [AXUIElement] {
                queue.append(contentsOf: children.map { ($0, depth + 1) })
            }
        }
        return (sourceBacked.count == 1 ? sourceBacked[0] : nil,
                textAreas, sourceBacked.count, sourceLabelAttribute)
    }

    private func waitForEditor() -> (AXUIElement?, Int, Int, String) {
        let deadline = Date().addingTimeInterval(20)
        var found: (AXUIElement?, Int, Int, String) = (nil, 0, 0, "<missing>")
        repeat {
            found = findEditor()
            if let editor = found.0,
               (attribute(editor, "AXNumberOfCharacters").1 as? NSNumber)?.intValue == expected.length {
                return found
            }
            Thread.sleep(forTimeInterval: 0.1)
        } while Date() < deadline
        return found
    }

    func run() -> Report {
        var observedPID: pid_t = 0
        let pidError = AXUIElementGetPid(app, &observedPID)
        record("app-pid", pidError == .success && observedPID == pid && pid != getpid(),
               "AX=\(pidError.rawValue), app=\(observedPID), editor=\(pid), client=\(getpid())")

        let trusted = AXIsProcessTrusted()
        let (element, textAreaCount, matchingCount, labelAttribute) = waitForEditor()
        record("one-source-backed-text-area", matchingCount == 1,
               "trusted=\(trusted), all AXTextArea (including preview)=\(textAreaCount), source-backed candidates=\(matchingCount)")
        record("source-editor-label", labelAttribute != "<missing>",
               "Mote editor exposed by \(labelAttribute)")
        guard let editor = element else {
            let (windowError, _) = attribute(app, "AXWindows")
            return report(!trusted ? "external-accessibility-unavailable" : "failed",
                          "Provider not found; AXIsProcessTrusted=\(trusted), AXWindows=\(windowError.rawValue).")
        }
        AXUIElementSetMessagingTimeout(editor, 2.0)

        var editorPID: pid_t = 0
        let editorPIDError = AXUIElementGetPid(editor, &editorPID)
        record("provider-pid", editorPIDError == .success && editorPID == pid,
               "AX=\(editorPIDError.rawValue), provider=\(editorPID)")
        var ancestor = editor
        var ancestry: [String] = []
        var reachedApplication = false
        var ancestryHasWrongPID = false
        for _ in 0..<16 {
            let role = attribute(ancestor, "AXRole").1 as? String ?? "<unavailable>"
            ancestry.append(role)
            var parentPID: pid_t = 0
            if AXUIElementGetPid(ancestor, &parentPID) != .success || parentPID != pid {
                ancestryHasWrongPID = true
            }
            if role == "AXApplication" { reachedApplication = true; break }
            guard let parent = attribute(ancestor, "AXParent").1 as? AXUIElement else { break }
            ancestor = parent
        }
        record("provider-parent-pid-tree", reachedApplication && !ancestryHasWrongPID &&
               ancestry.contains("AXWindow"), "roles=\(ancestry.joined(separator: ">"))")

        let (countError, rawCount) = attribute(editor, "AXNumberOfCharacters")
        let count = (rawCount as? NSNumber)?.intValue
        record("full-utf16-count", countError == .success && count == expected.length,
               "AX=\(countError.rawValue), observed=\(String(describing: count)), expected=\(expected.length)")

        let (selectionError, rawSelection) = attribute(editor, "AXSelectedTextRange")
        let selection = rangeValue(rawSelection)
        let selectionValid = selection.map { $0.location >= 0 && $0.length >= 0 &&
            $0.location <= expected.length && $0.length <= expected.length - $0.location } ?? false
        record("global-selection", selectionError == .success && selectionValid,
               "AX=\(selectionError.rawValue), range=\(String(describing: selection))")

        let (visibleError, rawVisible) = attribute(editor, "AXVisibleCharacterRange")
        let visible = rangeValue(rawVisible)
        let lines = sourceLines()
        let visibleValid = visible.map { $0.location >= 0 && $0.length > 0 &&
            $0.location <= expected.length && $0.length <= expected.length - $0.location } ?? false
        let visibleWholeLines = visible.map { range in
            lines.contains { $0.location == range.location } &&
            lines.contains { $0.location + $0.length == range.location + range.length }
        } ?? false
        record("visible-whole-source-lines", visibleError == .success && visibleValid && visibleWholeLines,
               "AX=\(visibleError.rawValue), range=\(String(describing: visible))")

        let indices = [0, 1, 81, 82, 83, 84, 85, lines.count - 2, lines.count - 1]
        for line in Set(indices).sorted() where line >= 0 && line < lines.count {
            let expectedLine = lines[line]
            let (rangeError, rawRange) = parameter(editor, "AXRangeForLine", NSNumber(value: line))
            let range = rangeValue(rawRange)
            let matches = range.map { $0.location == expectedLine.location && $0.length == expectedLine.length } ?? false
            record("line-range-\(line)", rangeError == .success && matches,
                   "AX=\(rangeError.rawValue), observed=\(String(describing: range)), expected=\(expectedLine)")
            let (indexError, rawIndex) = parameter(editor, "AXLineForIndex", NSNumber(value: expectedLine.location))
            let observedLine = (rawIndex as? NSNumber)?.intValue
            record("line-index-\(line)", indexError == .success && observedLine == line,
                   "AX=\(indexError.rawValue), observed=\(String(describing: observedLine))")
        }

        for token in ["HEAD\r\n", "CR\r", "LF\n", "PAIR\r\n", "EMOJI-😀-TAIL\n", "OFFSCREEN-TARGET-😀\n"] {
            let occurrence = expected.range(of: token)
            let (error, result) = text(editor, occurrence.location, occurrence.length)
            record("source-string-\(token.unicodeScalars.first!.value)",
                   occurrence.location != NSNotFound && error == .success && result == token,
                   "AX=\(error.rawValue), offset=\(occurrence.location), expected UTF16=\(occurrence.length), exact=\(result == token)")
        }

        let large = expected.range(of: String(repeating: "q", count: 70_000))
        let (oversizeError, oversizeValue) = text(editor, large.location, 65_537)
        record("oversize-rejected-not-truncated", large.location != NSNotFound &&
               (oversizeError != .success || oversizeValue == nil),
               "AX=\(oversizeError.rawValue), returnedValue=\(oversizeValue != nil)")
        let tail = expected.range(of: "OFFSCREEN-TARGET-😀\n")
        let trulyOffscreen = visible.map { tail.location >= $0.location + $0.length } ?? false
        record("tail-is-offscreen", trulyOffscreen,
               "tail=\(tail.location), visible=\(String(describing: visible))")
        let (afterError, afterValue) = text(editor, tail.location, tail.length)
        record("usable-after-oversize", afterError == .success && afterValue == "OFFSCREEN-TARGET-😀\n",
               "AX=\(afterError.rawValue), exact=\(afterValue == "OFFSCREEN-TARGET-😀\n")")

        return report(checks.allSatisfy(\.passed) ? "passed" : "failed",
                      "External AX API only; not VoiceOver, physical keyboard, or IME evidence.")
    }

    private func report(_ status: String, _ note: String) -> Report {
        Report(status: status, editorPID: pid, clientPID: getpid(), checks: checks, note: note)
    }
}

/// The wrapper owns app launch and persistence; this executable owns independent AX calls.
func main() -> Int32 {
    guard CommandLine.arguments.count == 3, let pid = Int32(CommandLine.arguments[1]) else {
        fputs("Usage: mote-ax-probe <editor-pid> <fixture-path>\n", stderr)
        return 2
    }
    let report: Report
    do {
        let probe = try Probe(pid: pid, fixture: CommandLine.arguments[2])
        report = probe.run()
    } catch {
        report = Report(status: "probe-error", editorPID: pid, clientPID: getpid(),
                        checks: [], note: String(describing: error))
    }
    let encoder = JSONEncoder()
    encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
    if let data = try? encoder.encode(report), let json = String(data: data, encoding: .utf8) {
        print(json)
    }
    return report.status == "passed" ? 0 : 1
}

exit(main())
