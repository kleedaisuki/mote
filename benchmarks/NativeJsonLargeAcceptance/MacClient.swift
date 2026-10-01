import ApplicationServices
import AppKit
import CoreGraphics
import Foundation

/// One exact-process, bounded AX/Quartz transaction. Never activates an app,
/// creates a system-wide AX object, prompts for trust, or posts global input.
private struct Report: Codable {
    var status = "observed"
    var requested_pid: Int32
    var trusted = false
    var post_event_access = false
    var ready = false
    var complete = false
    var source_candidates = 0
    var source_units: Int?
    var focused: Bool?
    var selection_start: Int?
    var selection_length: Int?
    var tree_bounded = true
    var dispatched_events = 0
    var guard_stage = "observe"
    var ax_error: Int32?
    var window_count: Int?
    var window_copy_error: Int32?
    var window_copy_count: Int?
    var modified: Bool?
    var target_app_active: Bool?
    var frontmost_is_target: Bool?
    var window_main: Bool?
    var window_focused: Bool?
}

/// Retrieve only one bounded metadata attribute with an element-local timeout.
private func attribute(_ element: AXUIElement, _ name: String) -> AnyObject? {
    _ = AXUIElementSetMessagingTimeout(element, 0.15)
    var raw: CFTypeRef?
    return AXUIElementCopyAttributeValue(element, name as CFString, &raw) == .success ? raw : nil
}

/// Decode a canonical source range, not AXValue text or an arbitrary representation.
private func range(_ value: AnyObject?) -> CFRange? {
    guard let value, CFGetTypeID(value) == AXValueGetTypeID() else { return nil }
    let ax = unsafeBitCast(value, to: AXValue.self)
    guard AXValueGetType(ax) == .cfRange else { return nil }
    var result = CFRange(location: 0, length: 0)
    return AXValueGetValue(ax, .cfRange, &result) ? result : nil
}

/// Reject stale/foreign elements immediately before every modifying operation.
private func owned(_ element: AXUIElement, _ pid: pid_t) -> Bool {
    var actual: pid_t = 0
    return AXUIElementGetPid(element, &actual) == .success && actual == pid
}

/// Traverse at most 64 elements/depth 8 and fetch at most 32 children per node.
/// The only string values read are static status chrome, never an AXTextArea body.
private func observe(_ app: AXUIElement, _ pid: pid_t, _ size: Int, _ version: Int)
    -> (Report, AXUIElement?, AXUIElement?) {
    var report = Report(requested_pid: pid)
    report.trusted = AXIsProcessTrusted()
    report.post_event_access = CGPreflightPostEventAccess()
    // Read target-only activity and redact the global foreground identity to a
    // Boolean comparison. Neither observation activates an app or admits input.
    report.target_app_active = NSRunningApplication(processIdentifier: pid)?.isActive
    report.frontmost_is_target = NSWorkspace.shared.frontmostApplication.map {
        $0.processIdentifier == pid
    }
    guard report.trusted else { report.status = "blocked"; report.guard_stage = "ax-trust"; return (report, nil, nil) }
    guard owned(app, pid) else { report.status = "failed"; report.guard_stage = "app-ownership"; return (report, nil, nil) }
    _ = AXUIElementSetMessagingTimeout(app, 0.15)
    var windowCount = 0
    let countError = AXUIElementGetAttributeValueCount(app, "AXWindows" as CFString, &windowCount)
    report.ax_error = countError.rawValue
    // An unsuccessful count call does not establish an empty window list.
    report.window_count = countError == .success ? windowCount : nil
    if countError == .cannotComplete {
        // Startup AX messaging can race the application's run loop. This one
        // read-only pending state belongs to the parent's existing 60s wait;
        // it never authorizes a key/Save/close action or extends that deadline.
        report.guard_stage = "window-count"
        return (report, nil, nil)
    }
    guard countError == .success, windowCount <= 1 else {
        report.status = "failed"; report.guard_stage = "window-count"; return (report, nil, nil)
    }
    if windowCount == 0 { return (report, nil, nil) }
    var rawWindows: CFArray?
    let copyError = AXUIElementCopyAttributeValues(app, "AXWindows" as CFString, 0, 1, &rawWindows)
    report.window_copy_error = copyError.rawValue
    let windows = rawWindows as? [AXUIElement]
    report.window_copy_count = copyError == .success ? windows?.count : nil
    if copyError == .cannotComplete {
        // Count success does not guarantee the following bounded read can
        // complete while the app is busy. Keep this read pending only; main
        // rejects it before every modifying branch and the parent keeps its
        // existing phase deadline and liveness checks.
        report.guard_stage = "window-read"
        return (report, nil, nil)
    }
    guard copyError == .success, let windows, windows.count == 1 else {
        report.status = "failed"; report.guard_stage = "window-read"; return (report, nil, nil)
    }
    var queue: [(AXUIElement, Int)] = [(windows[0], 0)]
    var cursor = 0
    var sources: [AXUIElement] = []
    var window: AXUIElement?
    while cursor < queue.count && cursor < 64 {
        let (element, depth) = queue[cursor]
        cursor += 1
        guard owned(element, pid) else { report.status = "failed"; report.guard_stage = "tree-ownership"; return (report, nil, nil) }
        let role = attribute(element, "AXRole") as? String
        if role == "AXWindow" {
            if window != nil { report.status = "failed"; report.guard_stage = "window-count"; return (report, nil, nil) }
            guard let title = attribute(element, "AXTitle") as? String,
                  title.utf16.count <= 512, title.contains("working.json") else {
                report.guard_stage = "window-title"; return (report, nil, nil)
            }
            report.modified = title.contains(" •")
            // These are the owned window's AX attributes, not a relabeling of
            // the source proxy's first-responder-only AXFocused implementation.
            report.window_main = (attribute(element, "AXMain") as? NSNumber)?.boolValue
            report.window_focused = (attribute(element, "AXFocused") as? NSNumber)?.boolValue
            window = element
        }
        if role == "AXTextArea", attribute(element, "AXDescription") as? String == "Mote editor" {
            sources.append(element)
        }
        if role == "AXStaticText", let status = attribute(element, "AXValue") as? String,
           status.utf16.count <= 1024,
           status.contains("JSON · Complete · v\(version)"), status.contains("No diagnostics.") {
            report.complete = true
        }
        if depth < 8 {
            var count = 0
            if AXUIElementGetAttributeValueCount(element, "AXChildren" as CFString, &count) == .success, count > 0 {
                if count > 32 || queue.count + count > 64 {
                    report.tree_bounded = false
                    report.status = "failed"
                    report.guard_stage = "tree-bound"
                    return (report, nil, nil)
                }
                var children: CFArray?
                if AXUIElementCopyAttributeValues(element, "AXChildren" as CFString, 0, count, &children) == .success,
                   let children = children as? [AXUIElement] {
                    queue.append(contentsOf: children.map { ($0, depth + 1) })
                }
            }
        }
    }
    report.source_candidates = sources.count
    guard sources.count == 1, let source = sources.first else {
        report.guard_stage = "source-candidates"; return (report, nil, window)
    }
    report.source_units = (attribute(source, "AXNumberOfCharacters") as? NSNumber)?.intValue
    report.focused = (attribute(source, "AXFocused") as? NSNumber)?.boolValue
    let selected = range(attribute(source, "AXSelectedTextRange"))
    report.selection_start = selected?.location
    report.selection_length = selected?.length
    report.ready = report.source_units == size && window != nil
    report.guard_stage = report.ready ? "ready" : "source-length"
    return (report, source, window)
}

/// Send a key pair only to the owned PID; return means dispatch, not acceptance.
private func key(_ pid: pid_t, _ code: CGKeyCode, _ flags: CGEventFlags = [], _ text: [UniChar]? = nil) -> Bool {
    guard CGPreflightPostEventAccess(),
          let down = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: true),
          let up = CGEvent(keyboardEventSource: nil, virtualKey: code, keyDown: false) else { return false }
    down.flags = flags
    up.flags = flags
    if let text {
        text.withUnsafeBufferPointer { buffer in
            down.keyboardSetUnicodeString(stringLength: buffer.count, unicodeString: buffer.baseAddress!)
            up.keyboardSetUnicodeString(stringLength: buffer.count, unicodeString: buffer.baseAddress!)
        }
    }
    down.postToPid(pid)
    up.postToPid(pid)
    return true
}

/// Require the same physically focused source proxy and exact range after one key sequence.
private func selection(_ source: AXUIElement, _ pid: pid_t, _ start: Int, _ length: Int) -> Bool {
    guard owned(source, pid), (attribute(source, "AXFocused") as? NSNumber)?.boolValue == true,
          let selected = range(attribute(source, "AXSelectedTextRange")) else { return false }
    return selected.location == start && selected.length == length
}

/// A small bounded read-only acknowledgement loop; never retries a modifying event.
private func acknowledge(_ source: AXUIElement, _ pid: pid_t, _ start: Int, _ length: Int) -> Bool {
    for _ in 0..<20 {
        if selection(source, pid, start, length) { return true }
        Thread.sleep(forTimeInterval: 0.025)
    }
    return false
}

/// Perform at most one edit/Save/close transaction under explicit ownership/trust guards.
private func main() throws {
    let arguments = CommandLine.arguments
    guard arguments.count == 5, let pid = Int32(arguments[1]), pid > 1,
          let size = Int(arguments[2]), size == 1048576 || size == 104857600,
          let version = Int(arguments[4]), version == 0 || version == 1 else { exit(2) }
    let operation = arguments[3]
    guard ["observe", "edit", "save", "close"].contains(operation) else { exit(2) }
    let app = AXUIElementCreateApplication(pid)
    var (report, source, window) = observe(app, pid, size, version)
    if operation != "observe", report.ax_error == AXError.cannotComplete.rawValue ||
        report.window_copy_error == AXError.cannotComplete.rawValue {
        report.status = "failed"
        emit(report)
        return
    }
    if operation == "edit" || operation == "save" {
        guard report.trusted && report.post_event_access else {
            report.status = "blocked"
            report.guard_stage = "post-access"
            emit(report)
            return
        }
        guard report.ready, let source, report.focused == true else {
            report.status = "blocked"
            report.guard_stage = "source-focus"
            emit(report)
            return
        }
        if operation == "edit" {
            // The source proxy currently has a range getter but no range setter.
            // Navigate exactly once from the certified fresh-open caret instead.
            report.guard_stage = "initial-selection"
            guard selection(source, pid, 0, 0) else { report.status = "failed"; emit(report); return }
            report.guard_stage = "navigate"
            for _ in 0..<9 {
                guard owned(source, pid), key(pid, 124) else { report.status = "failed"; emit(report); return }
                report.dispatched_events += 2
                Thread.sleep(forTimeInterval: 0.01)
            }
            report.guard_stage = "selection-ack"
            guard acknowledge(source, pid, 9, 0), key(pid, 124, .maskShift),
                  acknowledge(source, pid, 9, 1) else { report.status = "failed"; emit(report); return }
            report.dispatched_events += 2
            report.guard_stage = "edit-dispatch"
            guard owned(source, pid), key(pid, 0, [], [88]) else { report.status = "failed"; emit(report); return }
            report.dispatched_events += 2
            report.guard_stage = "edit-ack"
            guard acknowledge(source, pid, 10, 0) else { report.status = "failed"; emit(report); return }
        } else {
            report.guard_stage = "save-selection"
            guard selection(source, pid, 10, 0), key(pid, 1, .maskCommand) else {
                report.status = "failed"; emit(report); return
            }
            report.dispatched_events = 2
            report.guard_stage = "save-dispatch"
        }
    } else if operation == "close" {
        report.guard_stage = "close-window"
        guard report.ready, let window, owned(window, pid),
              let raw = attribute(window, "AXCloseButton"), CFGetTypeID(raw) == AXUIElementGetTypeID() else {
            report.status = "failed"; emit(report); return
        }
        let button = unsafeBitCast(raw, to: AXUIElement.self)
        report.guard_stage = "close-button"
        guard owned(button, pid), AXUIElementPerformAction(button, "AXPress" as CFString) == .success else {
            report.status = "failed"; emit(report); return
        }
    }
    emit(report)
}

/// Emit only fixed classifications, numeric metadata and Boolean capability observations.
private func emit(_ report: Report) {
    let bytes = try! JSONEncoder().encode(report)
    FileHandle.standardOutput.write(bytes)
    FileHandle.standardOutput.write(Data([10]))
}

do { try main() } catch { exit(3) }
