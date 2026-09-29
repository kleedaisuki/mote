import ApplicationServices
import AppKit
import Foundation

/// A text-free, exact-PID accessibility observation of the canvas source proxy.
/// No AXValue or source characters are requested; keyboard routing stays in PowerShell.
private struct GateReport: Codable {
    let status: String
    let trusted: Bool
    let requestedPid: Int32
    let applicationPid: Int32
    let workspaceFrontmostPid: Int32
    let focusedWindowPid: Int32
    let focusedWindowMatches: Bool
    let focusedWindowError: Int32
    let sourceCandidates: Int
    let sourceLength: Int?
    let proxyFocused: Bool?
    let focusError: Int32
    let selectionStart: Int?
    let selectionLength: Int?
    let selectionError: Int32
    let visibleStart: Int?
    let visibleLength: Int?
}

/// Reads one AX attribute without making an unbounded text request.
private func attribute(_ element: AXUIElement, _ name: String) -> (AXError, AnyObject?) {
    var value: CFTypeRef?
    let error = AXUIElementCopyAttributeValue(element, name as CFString, &value)
    return (error, value)
}

/// Decodes an AX range while rejecting a different AXValue representation.
private func range(_ raw: AnyObject?) -> CFRange? {
    guard let raw, CFGetTypeID(raw) == AXValueGetTypeID() else { return nil }
    let value = unsafeBitCast(raw, to: AXValue.self)
    guard AXValueGetType(value) == .cfRange else { return nil }
    var result = CFRange(location: 0, length: 0)
    return AXValueGetValue(value, .cfRange, &result) ? result : nil
}

/// Enumerates a bounded AX tree to distinguish the full source proxy from preview text.
private func sourceProxy(_ app: AXUIElement, expectedLength: Int) -> (AXUIElement?, Int) {
    var queue: [(AXUIElement, Int)] = [(app, 0)]
    var cursor = 0
    var candidates: [AXUIElement] = []
    while cursor < queue.count && cursor < 256 {
        let (element, depth) = queue[cursor]
        cursor += 1
        if attribute(element, "AXRole").1 as? String == "AXTextArea",
           attribute(element, "AXDescription").1 as? String == "Mote editor",
           (attribute(element, "AXNumberOfCharacters").1 as? NSNumber)?.intValue == expectedLength {
            candidates.append(element)
        }
        if depth < 10, let children = attribute(element, "AXChildren").1 as? [AXUIElement] {
            queue.append(contentsOf: children.map { ($0, depth + 1) })
        }
    }
    return (candidates.count == 1 ? candidates[0] : nil, candidates.count)
}

/// Captures process, focused-window, physical-first-responder proxy, and range metadata.
private func observe(pid: pid_t, expectedLength: Int, expectedFileName: String) -> GateReport {
    let app = AXUIElementCreateApplication(pid)
    AXUIElementSetMessagingTimeout(app, 2.0)
    var applicationPid: pid_t = 0
    _ = AXUIElementGetPid(app, &applicationPid)

    let (windowError, windowRaw) = attribute(app, "AXFocusedWindow")
    var windowPid: pid_t = 0
    var windowMatches = false
    if windowError == .success, let windowRaw,
       CFGetTypeID(windowRaw) == AXUIElementGetTypeID() {
        let window = unsafeBitCast(windowRaw, to: AXUIElement.self)
        _ = AXUIElementGetPid(window, &windowPid)
        let title = attribute(window, "AXTitle").1 as? String ?? ""
        windowMatches = windowPid == pid && title.contains(expectedFileName)
    }

    let (proxy, candidates) = sourceProxy(app, expectedLength: expectedLength)
    var focused: Bool?
    var focusError: AXError = .failure
    var selected: CFRange?
    var selectionError: AXError = .failure
    var visible: CFRange?
    if let proxy {
        let focus = attribute(proxy, "AXFocused")
        focusError = focus.0
        focused = (focus.1 as? NSNumber)?.boolValue
        let selection = attribute(proxy, "AXSelectedTextRange")
        selectionError = selection.0
        selected = range(selection.1)
        visible = range(attribute(proxy, "AXVisibleCharacterRange").1)
    }

    return GateReport(
        status: "observed", trusted: AXIsProcessTrusted(), requestedPid: pid,
        applicationPid: applicationPid,
        workspaceFrontmostPid: NSWorkspace.shared.frontmostApplication?.processIdentifier ?? 0,
        focusedWindowPid: windowPid,
        focusedWindowMatches: windowMatches, focusedWindowError: windowError.rawValue,
        sourceCandidates: candidates, sourceLength: proxy == nil ? nil : expectedLength,
        proxyFocused: focused, focusError: focusError.rawValue,
        selectionStart: selected?.location, selectionLength: selected?.length,
        selectionError: selectionError.rawValue,
        visibleStart: visible?.location, visibleLength: visible?.length)
}

/// Emits only bounded numeric/role metadata as one JSON line for the external harness.
private func main() throws {
    guard CommandLine.arguments.count == 4,
          let pid = Int32(CommandLine.arguments[1]), pid > 0,
          let expectedLength = Int(CommandLine.arguments[2]), expectedLength > 0 else {
        throw NSError(domain: "CanvasAxGate", code: 2,
                      userInfo: [NSLocalizedDescriptionKey: "Expected pid, UTF-16 length, synthetic filename."])
    }
    let fileName = CommandLine.arguments[3]
    guard !fileName.contains("/") && !fileName.contains("\\") else {
        throw NSError(domain: "CanvasAxGate", code: 3,
                      userInfo: [NSLocalizedDescriptionKey: "Filename must be a synthetic basename."])
    }
    let report = observe(pid: pid, expectedLength: expectedLength, expectedFileName: fileName)
    let data = try JSONEncoder().encode(report)
    print(String(decoding: data, as: UTF8.self))
}

do { try main() }
catch {
    fputs("canvas-ax-gate-error: \(error.localizedDescription)\n", stderr)
    exit(2)
}
