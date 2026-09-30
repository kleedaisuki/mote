// External macOS preview navigation diagnostic for a published mote Mach-O.
// Output contains only bounded offsets, roles, and status; no document text.
import AppKit
import ApplicationServices
import Foundation

/// A content-free observation of the source proxy and read-only preview.
private struct Report: Codable {
    let status: String
    let trusted: Bool
    let requestedPid: Int32
    let applicationPid: Int32
    let sourcePid: Int32?
    let previewPid: Int32?
    let frontmostPid: Int32
    let sourceCandidates: Int
    let previewCandidates: Int
    let sourceLength: Int?
    let sourceSelectionStart: Int?
    let sourceSelectionLength: Int?
    let sourceFocused: Bool?
    let windowDirty: Bool?
    let previewLength: Int?
    let previewOffset: Int?
    let previewRangeLength: Int?
    let previewSelectionStart: Int?
    let previewSelectionLength: Int?
    let previewFocused: Bool?
    let previewEditable: Bool?
    let boundsX: Double?
    let boundsY: Double?
    let boundsWidth: Double?
    let boundsHeight: Double?
    let actionError: String?
}

/// Reads an AX attribute without requesting complete source text.
private func attribute(_ element: AXUIElement, _ name: String) -> (AXError, AnyObject?) {
    var value: CFTypeRef?
    let error = AXUIElementCopyAttributeValue(element, name as CFString, &value)
    return (error, value)
}

/// Reads a bounded preview substring or glyph rectangle.
private func parameter(_ element: AXUIElement, _ name: String, _ input: AXValue)
    -> (AXError, AnyObject?) {
    var value: CFTypeRef?
    let error = AXUIElementCopyParameterizedAttributeValue(element, name as CFString, input, &value)
    return (error, value)
}

/// Creates an AX UTF-16 character range.
private func axRange(_ start: Int, _ length: Int) -> AXValue {
    var range = CFRange(location: start, length: length)
    return AXValueCreate(.cfRange, &range)!
}

/// Decodes only an AX CFRange value.
private func range(_ raw: AnyObject?) -> CFRange? {
    guard let raw, CFGetTypeID(raw) == AXValueGetTypeID() else { return nil }
    let value = unsafeBitCast(raw, to: AXValue.self)
    guard AXValueGetType(value) == .cfRange else { return nil }
    var result = CFRange(location: 0, length: 0)
    return AXValueGetValue(value, .cfRange, &result) ? result : nil
}

/// Decodes only an AX CGRect value.
private func rect(_ raw: AnyObject?) -> CGRect? {
    guard let raw, CFGetTypeID(raw) == AXValueGetTypeID() else { return nil }
    let value = unsafeBitCast(raw, to: AXValue.self)
    guard AXValueGetType(value) == .cgRect else { return nil }
    var result = CGRect.zero
    return AXValueGetValue(value, .cgRect, &result) ? result : nil
}

/// Finds exactly one labeled source and one labeled native preview in a bounded AX tree.
private func elements(_ app: AXUIElement) -> (source: [AXUIElement], preview: [AXUIElement]) {
    var queue: [(AXUIElement, Int)] = [(app, 0)]
    var index = 0
    var sources: [AXUIElement] = []
    var previews: [AXUIElement] = []
    while index < queue.count && index < 256 {
        let (element, depth) = queue[index]
        index += 1
        let role = attribute(element, "AXRole").1 as? String
        if role == "AXTextArea" {
            let description = attribute(element, "AXDescription").1 as? String
            let title = attribute(element, "AXTitle").1 as? String
            if description == "Mote editor" || title == "Mote editor" { sources.append(element) }
            if description == "Mote preview" || title == "Mote preview" { previews.append(element) }
        }
        if depth < 10, let children = attribute(element, "AXChildren").1 as? [AXUIElement] {
            queue.append(contentsOf: children.map { ($0, depth + 1) })
        }
    }
    return (sources, previews)
}

/// Reads only the capped 16 Ki-unit preview to identify an exact semantic glyph range.
private func destination(_ preview: AXUIElement, _ marker: String) -> (Int?, CGRect?) {
    guard let count = (attribute(preview, "AXNumberOfCharacters").1 as? NSNumber)?.intValue,
          count > 0, count <= 16 * 1024 + 128 else { return (nil, nil) }
    let (error, raw) = parameter(preview, "AXStringForRange", axRange(0, count))
    guard error == .success, let text = raw as? String else { return (nil, nil) }
    let source = text as NSString
    let hit = source.range(of: marker)
    guard hit.location != NSNotFound else { return (nil, nil) }
    let (boundsError, boundsRaw) = parameter(preview, "AXBoundsForRange",
                                              axRange(hit.location, hit.length))
    return (hit.location, boundsError == .success ? rect(boundsRaw) : nil)
}

/// Makes a gesture against the real preview without invoking mote internals.
private func act(_ action: String, _ pid: pid_t, _ preview: AXUIElement,
                 _ offset: Int, _ bounds: CGRect?) -> String? {
    guard let process = NSRunningApplication(processIdentifier: pid) else {
        return "NSRunningApplication unavailable"
    }
    _ = process.activate(options: [.activateIgnoringOtherApps])
    Thread.sleep(forTimeInterval: 0.15)
    guard NSWorkspace.shared.frontmostApplication?.processIdentifier == pid else {
        return "target editor is not frontmost; refused global input event"
    }
    switch action {
    case "activate": break
    case "select":
        let error = AXUIElementSetAttributeValue(preview, "AXSelectedTextRange" as CFString,
                                                 axRange(offset, 0))
        if error != .success { return "AXSelectedTextRange setter error \(error.rawValue)" }
        let focused = AXUIElementSetAttributeValue(preview, "AXFocused" as CFString,
                                                   kCFBooleanTrue)
        if focused != .success { return "AXFocused setter error \(focused.rawValue)" }
    case "click":
        guard let bounds, bounds.width > 0, bounds.height > 0 else {
            return "AXBoundsForRange unavailable or empty"
        }
        let point = CGPoint(x: bounds.minX + min(8, bounds.width / 2),
                            y: bounds.midY)
        guard let down = CGEvent(mouseEventSource: nil, mouseType: .leftMouseDown,
                                 mouseCursorPosition: point, mouseButton: .left),
              let up = CGEvent(mouseEventSource: nil, mouseType: .leftMouseUp,
                               mouseCursorPosition: point, mouseButton: .left) else {
            return "CGEvent mouse creation failed"
        }
        down.post(tap: .cghidEventTap)
        up.post(tap: .cghidEventTap)
    case "enter", "space", "save", "undo":
        let key: CGKeyCode = switch action {
        case "enter": 36
        case "space": 49
        case "save": 1
        default: 6
        }
        guard let down = CGEvent(keyboardEventSource: nil, virtualKey: key, keyDown: true),
              let up = CGEvent(keyboardEventSource: nil, virtualKey: key, keyDown: false) else {
            return "CGEvent keyboard creation failed"
        }
        if action == "save" || action == "undo" {
            down.flags = .maskCommand
            up.flags = .maskCommand
        }
        down.post(tap: .cghidEventTap)
        up.post(tap: .cghidEventTap)
    default: return "unknown action"
    }
    return nil
}

/// Inspects one process and optionally performs a requested gesture.
private func inspect(pid: pid_t, expectedLength: Int, action: String) -> Report {
    if action == "activate", let process = NSRunningApplication(processIdentifier: pid) {
        _ = process.activate(options: [.activateIgnoringOtherApps])
        Thread.sleep(forTimeInterval: 0.15)
    }
    let app = AXUIElementCreateApplication(pid)
    AXUIElementSetMessagingTimeout(app, 2.0)
    var applicationPid: pid_t = 0
    _ = AXUIElementGetPid(app, &applicationPid)
    let found = elements(app)
    let source = found.source.count == 1 ? found.source[0] : nil
    let preview = found.preview.count == 1 ? found.preview[0] : nil
    var sourcePid: pid_t = 0
    var previewPid: pid_t = 0
    if let source { _ = AXUIElementGetPid(source, &sourcePid) }
    if let preview { _ = AXUIElementGetPid(preview, &previewPid) }
    let marked = preview.flatMap { destination($0, "Destination") }
    var actionError: String?
    if action != "observe" && action != "activate" {
        if let preview, let offset = marked?.0 {
            actionError = act(action, pid, preview, offset, marked?.1)
            Thread.sleep(forTimeInterval: 0.1)
        } else { actionError = "unique preview or Destination not found" }
    }
    let selected = source.flatMap { range(attribute($0, "AXSelectedTextRange").1) }
    let previewSelected = preview.flatMap { range(attribute($0, "AXSelectedTextRange").1) }
    let frame = marked?.1
    let length = source.flatMap { (attribute($0, "AXNumberOfCharacters").1 as? NSNumber)?.intValue }
    let previewLength = preview.flatMap {
        (attribute($0, "AXNumberOfCharacters").1 as? NSNumber)?.intValue
    }
    let editable = preview.flatMap { (attribute($0, "AXEditable").1 as? NSNumber)?.boolValue }
    let sourceFocused = source.flatMap { (attribute($0, "AXFocused").1 as? NSNumber)?.boolValue }
    let previewFocused = preview.flatMap { (attribute($0, "AXFocused").1 as? NSNumber)?.boolValue }
    let windowRaw = attribute(app, "AXFocusedWindow").1
    let window: AXUIElement? = windowRaw.flatMap {
        CFGetTypeID($0) == AXUIElementGetTypeID() ? unsafeBitCast($0, to: AXUIElement.self) : nil
    }
    let title = window.flatMap { attribute($0, "AXTitle").1 as? String }
    let healthy = AXIsProcessTrusted() && applicationPid == pid &&
        sourcePid == pid && previewPid == pid && found.source.count == 1 &&
        found.preview.count == 1 && length == expectedLength &&
        marked?.0 != nil && frame != nil && editable == false
    return Report(status: !AXIsProcessTrusted() ? "ax-unavailable" :
                  actionError != nil ? "action-failed" : healthy ? "observed" : "incomplete",
                  trusted: AXIsProcessTrusted(), requestedPid: pid,
                  applicationPid: applicationPid,
                  sourcePid: source == nil ? nil : sourcePid,
                  previewPid: preview == nil ? nil : previewPid,
                  frontmostPid: NSWorkspace.shared.frontmostApplication?.processIdentifier ?? 0,
                  sourceCandidates: found.source.count, previewCandidates: found.preview.count,
                  sourceLength: length, sourceSelectionStart: selected?.location,
                  sourceSelectionLength: selected?.length, sourceFocused: sourceFocused,
                  windowDirty: title.map { $0.contains("•") },
                  previewLength: previewLength, previewOffset: marked?.0,
                  previewRangeLength: marked?.0 == nil ? nil : ("Destination" as NSString).length,
                  previewSelectionStart: previewSelected?.location,
                  previewSelectionLength: previewSelected?.length,
                  previewFocused: previewFocused, previewEditable: editable,
                  boundsX: frame.map { Double($0.minX) },
                  boundsY: frame.map { Double($0.minY) },
                  boundsWidth: frame.map { Double($0.width) },
                  boundsHeight: frame.map { Double($0.height) },
                  actionError: actionError)
}

/// Emits one compact JSON object; exit status is intentionally independent of assertions.
private func main() throws {
    guard CommandLine.arguments.count == 4,
          let pid = Int32(CommandLine.arguments[1]), pid > 0,
          let length = Int(CommandLine.arguments[2]), length > 0 else {
        throw NSError(domain: "mote-preview-probe", code: 2)
    }
    let report = inspect(pid: pid, expectedLength: length, action: CommandLine.arguments[3])
    let data = try JSONEncoder().encode(report)
    print(String(decoding: data, as: UTF8.self))
}

try main()
