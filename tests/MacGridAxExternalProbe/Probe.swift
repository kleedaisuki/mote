// A separate-process, exact-PID AX client. Never uses the system-wide AX root or key events.
import ApplicationServices
import AppKit
import Foundation

/// Each assertion retains its result even when a later gate fails.
struct Check: Codable {
    let name: String; let passed: Bool; let detail: String
    let admissionCount: Int; let elapsedSeconds: Double
}
/// Bounded relation facts retain only owned node counts and fixed command equality.
struct MenuRelationObservation: Codable {
    let node: String; let attribute: String; let axError: Int32; let kind: String; let count: Int?
    let ownedPID: Bool?; let role: String?; let childCount: Int?; let exactCoordinateTitles: Int?
}
/// Fixed phase counters distinguish traversal pressure from unavailable menu structure.
struct Diagnostics: Codable {
    let showMenuWireKeyMatches: Bool; let actionFailureFollowup: String
    let actionNamesError: Int32?; let actionNamesCount: Int?; let showMenuActionAdvertised: Bool?
    let shownMenuRelations: [MenuRelationObservation]
    let elapsedSeconds: Double; let phaseAdmissions: [String: Int]; let phasePolls: [String: Int]
    let phaseTraversals: [String: Int]; let maximumTreeNodes: Int
    let lastTreeMenus: Int; let lastTreeMenuItems: Int; let lastExactMenuMatches: Int; let lastExactMenuTitleMatches: Int
}
/// Content-free metadata facts distinguish label transport from ordinal semantics.
struct OrdinalObservation: Codable {
    let node: String; let attribute: String; let axError: Int32
    let kind: String; let utf16Length: Int?; let classification: String; let ordinal: Int?
}
/// Synthetic-only evidence; does not certify VoiceOver, IME, paint or performance.
struct Report: Codable {
    let status: String; let phase: String; let editorPID: Int32; let clientPID: Int32
    let trusted: Bool; let closedByProbe: Bool; let admissionCount: Int; let diagnostics: Diagnostics?; let checks: [Check]; let observations: [OrdinalObservation]; let note: String
}
/// Fail-closed termination with an intentionally content-free reason.
struct GateFailure: Error { let reason: String }

/// Bounds each API call, each array, the traversal, and the complete client lifetime.
final class Probe {
    let pid: pid_t
    let app: AXUIElement
    let expected: NSString
    var checks: [Check] = []
    /// Fixed two-node/eight-attribute diagnostic, never a source or desktop dump.
    var observations: [OrdinalObservation] = []
    var phase = "preconditions"
    var closed = false
    var queries = 0
    let started = ProcessInfo.processInfo.systemUptime
    let deadline = ProcessInfo.processInfo.systemUptime + 55
    /// Keys are fixed phase names, never node text, identifiers or desktop metadata.
    var phaseAdmissions: [String: Int] = [:]
    var phasePolls: [String: Int] = [:]
    var phaseTraversals: [String: Int] = [:]
    var actionNamesError: Int32? = nil
    var actionNamesCount: Int? = nil
    var showMenuActionAdvertised: Bool? = nil
    var actionFailureFollowup = "not-exercised"
    var shownMenuRelations: [MenuRelationObservation] = []
    var maximumTreeNodes = 0
    var lastTreeMenus = 0
    var lastTreeMenuItems = 0
    var lastExactMenuTitleMatches = 0
    var lastExactMenuMatches = 0

    init(pid: pid_t, fixture: String) throws {
        self.pid = pid
        app = AXUIElementCreateApplication(pid)
        expected = (try String(contentsOfFile: fixture, encoding: .utf8)) as NSString
        AXUIElementSetMessagingTimeout(app, 1.0)
    }
    /// No unbounded app array or unknown-PID query is ever admitted.
    func admit(_ node: AXUIElement) throws {
        guard queries < 12000, ProcessInfo.processInfo.systemUptime < deadline else {
            throw GateFailure(reason: "external AX query/lifetime budget exhausted")
        }
        var observed: pid_t = 0
        guard AXUIElementGetPid(node, &observed) == .success, observed == pid else {
            throw GateFailure(reason: "AX node PID mismatch/unavailable")
        }
        guard AXUIElementSetMessagingTimeout(node, 1.0) == .success else {
            throw GateFailure(reason: "AX node timeout could not be installed")
        }
        queries += 1
        phaseAdmissions[phase, default: 0] += 1
    }
    func attribute(_ node: AXUIElement, _ name: String) throws -> (AXError, AnyObject?) {
        try admit(node)
        var value: CFTypeRef?
        let error = AXUIElementCopyAttributeValue(node, name as CFString, &value)
        return (error, value)
    }
    /// Counts before copying; a truncated array is never accepted as the whole attribute.
    func elements(_ node: AXUIElement, _ name: String, limit: Int) throws -> [AXUIElement] {
        try admit(node)
        var count: CFIndex = 0
        let countError = AXUIElementGetAttributeValueCount(node, name as CFString, &count)
        guard countError == .success, count >= 0, count <= limit else {
            throw GateFailure(reason: "\(name) count unavailable/over budget (AX=\(countError.rawValue))")
        }
        if count == 0 { return [] }
        var value: CFArray?
        let error = AXUIElementCopyAttributeValues(node, name as CFString, 0, count, &value)
        guard error == .success, let array = value as? [AXUIElement], array.count == count else {
            throw GateFailure(reason: "\(name) bounded copy unavailable (AX=\(error.rawValue))")
        }
        for child in array { try admit(child) }
        return array
    }
    /// Probe the documented contextual-menu relation on two already owned roots.
    /// This does not relax the existing exact menu-item discovery predicate.
    func observeShownMenu(_ node: AXUIElement, category: String, key: String = "AXShownMenuUIElement") throws {
        let (error, raw) = try attribute(node, key)
        var kind = raw == nil ? "absent" : "other"
        var count: Int? = nil
        var ownedPID: Bool? = nil
        var role: String? = nil
        var childCount: Int? = nil
        var exactCoordinateTitles: Int? = nil
        if let raw {
            if CFGetTypeID(raw) == AXUIElementGetTypeID() {
                kind = "element"; count = 1
                let menu = unsafeBitCast(raw, to: AXUIElement.self)
                var observed: pid_t = 0
                let pidError = AXUIElementGetPid(menu, &observed)
                ownedPID = pidError == .success && observed == pid
                // Never query role or children on an unknown/foreign owner.
                if ownedPID == true {
                    let name = try text(menu, "AXRole")
                    role = name == "AXMenu" ? "expected-menu" : (name == nil ? "absent" : "other")
                    try admit(menu)
                    var size: CFIndex = 0
                    let childError = AXUIElementGetAttributeValueCount(menu, "AXChildren" as CFString, &size)
                    if childError == .success && size >= 0 && size <= 128 {
                        childCount = size
                        var exact = 0
                        for item in try elements(menu, "AXChildren", limit: 128) {
                            if try text(item, "AXRole") == "AXMenuItem",
                               try text(item, "AXTitle") == "Go to row:column…" { exact += 1 }
                        }
                        exactCoordinateTitles = exact
                    }
                }
            }
            else if CFGetTypeID(raw) == CFArrayGetTypeID() {
                let array = unsafeBitCast(raw, to: CFArray.self)
                let size = CFArrayGetCount(array)
                kind = size <= 8 ? "bounded-array" : "overbound-array"
                if size <= 8 { count = size }
            }
        }
        shownMenuRelations.append(MenuRelationObservation(node: category, attribute: key, axError: error.rawValue, kind: kind, count: count, ownedPID: ownedPID, role: role, childCount: childCount, exactCoordinateTitles: exactCoordinateTitles))
    }
    /// The framework has no action-name count-before-copy API. Reject arrays above
    /// 32 entries before examining strings; retain only error/count/fixed membership.
    func observeActionNames(_ node: AXUIElement) throws {
        try admit(node)
        var raw: CFArray?
        let error = AXUIElementCopyActionNames(node, &raw)
        actionNamesError = error.rawValue
        guard error == .success, let raw else { return }
        let count = CFArrayGetCount(raw)
        guard count >= 0 && count <= 32 else { return }
        actionNamesCount = count
        guard let names = raw as? [String], names.count == count else { return }
        showMenuActionAdvertised = names.contains(NSAccessibility.Action.showMenu.rawValue)
    }
    /// String attributes remain optional: absence is not an invented empty value.
    func text(_ node: AXUIElement, _ name: String) throws -> String? {
        try attribute(node, name).1 as? String
    }
    /// AppKit labels may use description or title; comparisons stay exact.
    func label(_ node: AXUIElement) throws -> String? {
        if let name = try text(node, "AXDescription") { return name }
        return try text(node, "AXTitle")
    }
    /// Retain only type/error, fixed equality classes and parsed bounded ordinal numbers.
    /// The original label predicate is intentionally unchanged pending native evidence.
    func observeOrdinal(_ node: AXUIElement, category: String, axis: String, expected: Int) throws {
        for name in ["AXRole", "AXRoleDescription", "AXDescription", "AXTitle", "AXIndex", "AXIdentifier", "AXValue", "AXHelp"] {
            let (error, raw) = try attribute(node, name)
            var kind = raw == nil ? "absent" : "other"
            var length: Int? = nil
            var classification = "unclassified"
            var ordinal: Int? = nil
            if let value = raw as? String {
                kind = "string"; length = value.utf16.count
                if value.isEmpty { classification = "empty" }
                else if value == "\(axis) \(expected)" { classification = "expected-ordinal"; ordinal = expected }
                else if value == "AX\(axis)" { classification = "expected-role" }
                else if ["AXRow", "AXColumn", "AXCell", "AXStaticText", "AXTextField", "AXGroup"].contains(value) {
                    classification = "known-role-" + value
                }
                else if value == axis.lowercased() { classification = "generic-axis-role" }
                else if value.utf16.count <= 160 {
                    let prefix = axis + " "
                    if value.hasPrefix(prefix) {
                        let suffix = value.dropFirst(prefix.count)
                        if !suffix.isEmpty && suffix.count <= 10 && suffix.allSatisfy({ $0 >= "0" && $0 <= "9" }) {
                            ordinal = Int(suffix); classification = "other-ordinal"
                        }
                    }
                    if value.hasPrefix("mote.csv.window.") {
                        classification = "window-wrapper-identifier"
                        let parts = value.split(separator: ".", omittingEmptySubsequences: false)
                        let nodeKind = axis.lowercased()
                        if parts.count == 7 && parts[4] == nodeKind && parts[5] == (axis == "Row" ? "0" : "-1") &&
                            parts[6] == (axis == "Row" ? "-1" : "0") {
                            classification = "expected-window-axis-identifier"
                        }
                    }
                } else { classification = "over-diagnostic-string-bound" }
            } else if let value = raw as? NSNumber {
                kind = "number"
                if value.intValue >= 0 && value.intValue <= 256 { ordinal = value.intValue; classification = "bounded-numeric-index" }
            }
            observations.append(OrdinalObservation(node: category, attribute: name, axError: error.rawValue,
                kind: kind, utf16Length: length, classification: classification, ordinal: ordinal))
        }
    }
    /// Persist the first falsifier before aborting dependent actions.
    func require(_ name: String, _ passed: Bool, _ detail: String = "") throws {
        checks.append(Check(name: name, passed: passed, detail: detail,
                            admissionCount: queries, elapsedSeconds: ProcessInfo.processInfo.systemUptime - started))
        if !passed { throw GateFailure(reason: name) }
    }
    /// Mutates only a previously validated node within the editor PID.
    func set(_ node: AXUIElement, _ name: String, _ value: CFTypeRef) throws -> AXError {
        try admit(node)
        return AXUIElementSetAttributeValue(node, name as CFString, value)
    }
    /// Invokes an AX action on the exact target, never a global event.
    func action(_ node: AXUIElement, _ name: String) throws -> AXError {
        try admit(node)
        return AXUIElementPerformAction(node, name as CFString)
    }
    /// Does not expand table rows/cells when finding unrelated source or transient dialogs.
    func tree() throws -> [AXUIElement] {
        phaseTraversals[phase, default: 0] += 1
        lastTreeMenus = 0
        lastTreeMenuItems = 0
        var queue: [(AXUIElement, Int)] = [(app, 0)]
        var result: [AXUIElement] = []
        var index = 0
        while index < queue.count {
            guard queue.count <= 256 else { throw GateFailure(reason: "AX tree exceeds 256 nodes") }
            let (node, depth) = queue[index]; index += 1; result.append(node)
            maximumTreeNodes = max(maximumTreeNodes, queue.count)
            let role = try text(node, "AXRole")
            if role == "AXMenu" { lastTreeMenus += 1 }
            if role == "AXMenuItem" { lastTreeMenuItems += 1 }
            if depth < 12 && role != "AXTable" && role != "AXRow" && role != "AXColumn" {
                // Unsupported leaf children are normal; successful over-budget arrays are not.
                try admit(node)
                var count: CFIndex = 0
                let error = AXUIElementGetAttributeValueCount(node, "AXChildren" as CFString, &count)
                if error == .success {
                    let children = try elements(node, "AXChildren", limit: 128)
                    queue.append(contentsOf: children.map { ($0, depth + 1) })
                }
            }
        }
        return result
    }
    /// Exact role/name matching refuses ambiguous target selection.
    func matches(_ role: String, _ name: String? = nil) throws -> [AXUIElement] {
        var found: [AXUIElement] = []
        if role == "AXMenuItem" { lastExactMenuMatches = 0; lastExactMenuTitleMatches = 0 }
        defer { if role == "AXMenuItem" { lastExactMenuMatches = found.count } }
        for node in try tree() {
            if try text(node, "AXRole") == role {
                // Only classify equality with the fixed synthetic coordinate command.
                // Keep the established description-first assertion unchanged.
                if role == "AXMenuItem", name != nil, try text(node, "AXTitle") == name {
                    lastExactMenuTitleMatches += 1
                }
                if name == nil { found.append(node) }
                else if try label(node) == name { found.append(node) }
            }
        }
        return found
    }
    /// Retries only read-only readiness predicates within a monotonic bound.
    func wait(_ seconds: Double, _ predicate: () throws -> Bool) throws -> Bool {
        let until = min(deadline, ProcessInfo.processInfo.systemUptime + seconds)
        repeat {
            phasePolls[phase, default: 0] += 1
            if try predicate() { return true }
            Thread.sleep(forTimeInterval: 0.15)
        } while ProcessInfo.processInfo.systemUptime < until
        return false
    }
    /// Parameterized lookups remain external framework requests.
    func parameter(_ node: AXUIElement, _ name: String, _ input: CFTypeRef) throws -> (AXError, AnyObject?) {
        try admit(node)
        var value: CFTypeRef?
        let error = AXUIElementCopyParameterizedAttributeValue(node, name as CFString, input, &value)
        return (error, value)
    }
    /// Input order is column then row, both local to the installed window.
    func cell(_ table: AXUIElement, _ row: Int, _ column: Int) throws -> AXUIElement {
        let (error, value) = try parameter(table, "AXCellForColumnAndRow", [NSNumber(value: column), NSNumber(value: row)] as CFArray)
        guard error == .success, let value, CFGetTypeID(value) == AXUIElementGetTypeID() else {
            throw GateFailure(reason: "external local cell lookup unavailable (AX=\(error.rawValue))")
        }
        let result = unsafeBitCast(value, to: AXUIElement.self)
        try admit(result)
        return result
    }
    /// Validate CF/AX type tags before interpreting native CFRange storage.
    func range(_ node: AXUIElement, _ name: String) throws -> CFRange? {
        let (_, raw) = try attribute(node, name)
        guard let raw, CFGetTypeID(raw) == AXValueGetTypeID() else { return nil }
        let value = unsafeBitCast(raw, to: AXValue.self)
        guard AXValueGetType(value) == .cfRange else { return nil }
        var range = CFRange(location: 0, length: 0)
        return AXValueGetValue(value, .cfRange, &range) ? range : nil
    }
    /// Read the whole bounded selected-cell array, never a truncated prefix.
    func selected(_ table: AXUIElement) throws -> [AXUIElement] {
        try elements(table, "AXSelectedCells", limit: 8192)
    }
    /// Reject duplicate readback nodes rather than comparing lossy sets.
    func same(_ lhs: [AXUIElement], _ rhs: [AXUIElement]) -> Bool {
        guard lhs.count == rhs.count else { return false }
        for array in [lhs, rhs] {
            for index in array.indices {
                if array[..<index].contains(where: { CFEqual($0, array[index]) }) { return false }
            }
        }
        return lhs.allSatisfy { item in rhs.contains { CFEqual(item, $0) } }
    }
    /// Transport timeouts or disabled APIs never prove an unavailable object.
    func unavailable(_ error: AXError) -> Bool {
        [.success, .noValue, .attributeUnsupported, .invalidUIElement].contains(error)
    }
    /// Source corroboration requests only the short synthetic tail marker.
    /// Refusal must be terminal/server-declared, not an indeterminate timeout.
    func refusal(_ error: AXError) -> Bool {
        [.success, .illegalArgument, .attributeUnsupported, .noValue, .invalidUIElement].contains(error)
    }
    func sourceString(_ source: AXUIElement, _ offset: Int, _ length: Int) throws -> String? {
        var range = CFRange(location: offset, length: length)
        guard let value = AXValueCreate(.cfRange, &range) else { return nil }
        return try parameter(source, "AXStringForRange", value).1 as? String
    }
    /// Gate sequence stops before any dependent mutation after a falsifier.
    func run() -> Report {
        let trusted = AXIsProcessTrusted() // Query only; never request/prompt/grant permission.
        if !trusted { return report("external-accessibility-unavailable", trusted, "AXIsProcessTrusted=false; no product verdict") }
        do {
            try require("separate-editor-pid", pid > 0 && pid != getpid())
            var table: AXUIElement?
            let found = try wait(12) {
                let candidates = try self.matches("AXTable", "CSV grid window")
                if candidates.count == 1 { table = candidates[0]; return true }
                return false
            }
            try require("one-opt-in-grid-table", found && table != nil)
            guard let table else { throw GateFailure(reason: "table absent") }
            try require("one-semantic-table-without-default-duplicate", try matches("AXTable").count == 1)
            let sources = try matches("AXTextArea", "Mote editor")
            try require("source-document-coexists", sources.count == 1)
            let source = sources[0]
            let count = try attribute(source, "AXNumberOfCharacters").1 as? NSNumber
            try require("source-full-fixture-count", count?.intValue == expected.length)
            let originalSourceSelection = try range(source, "AXSelectedTextRange")
            try require("source-selection-readable", originalSourceSelection != nil)
            let tail = expected.range(of: "r01100c24")
            try require("source-offscreen-exact", tail.location != NSNotFound && (try sourceString(source, tail.location, tail.length)) == "r01100c24")

            phase = "origin-window"
            let rows = try elements(table, "AXRows", limit: 256)
            let columns = try elements(table, "AXColumns", limit: 64)
            let rowCount = try attribute(table, "AXRowCount").1 as? NSNumber
            let colCount = try attribute(table, "AXColumnCount").1 as? NSNumber
            try require("bounded-counts-match-arrays", rows.count >= 3 && columns.count >= 3 && rowCount?.intValue == rows.count && colCount?.intValue == columns.count)
            // The first hosted run failed this predicate without observing which
            // label attribute AppKit transported. Preserve the original assertion
            // while collecting the smallest content-free discriminating facts.
            try observeOrdinal(rows[0], category: "first-row", axis: "Row", expected: 1)
            try observeOrdinal(columns[0], category: "first-column", axis: "Column", expected: 1)
            try require("first-record-is-data", try label(rows[0]) == "Row 1")
            let rowHeaders = try elements(table, "AXRowHeaderUIElements", limit: 256)
            let columnHeaders = try elements(table, "AXColumnHeaderUIElements", limit: 64)
            try require("ordinal-header-counts", rowHeaders.count == rows.count && columnHeaders.count == columns.count)
            try require("ordinal-header-labels", try label(rowHeaders[0]) == "Row 1" && label(columnHeaders[0]) == "Column 1")
            let first = try cell(table, 0, 0)
            let originReady = try wait(4) { try self.text(first, "AXValue") == "r00001c01" }
            try require("first-cell-exact-value", originReady)
            try require("semantic-cell-role-and-ordinal", try text(first, "AXRole") == "AXCell" && (label(first)?.hasPrefix("Row 1, Column 1") == true))
            let (_, parentRaw) = try attribute(first, "AXParent")
            try require("cell-parent-is-local-row", parentRaw != nil && CFEqual(parentRaw!, rows[0]))
            let empty = try cell(table, 0, 1)
            let missing = try cell(table, 1, 1)
            try require("empty-distinct-from-missing", try text(empty, "AXValue") == "" && text(missing, "AXValue") == nil && (text(missing, "AXHelp")?.contains("Missing field") == true))
            let local = try range(first, "AXRowIndexRange")
            try require("local-cell-index-range", local?.location == 0 && local?.length == 1 && (try range(first, "AXColumnIndexRange"))?.location == 0)
            let (invalidError, invalidValue) = try parameter(table, "AXCellForColumnAndRow", [NSNumber(value: columns.count), NSNumber(value: rows.count)] as CFArray)
            try require("out-of-window-cell-refused", invalidValue == nil && (unavailable(invalidError) || invalidError == .illegalArgument), "AX=\(invalidError.rawValue)")

            phase = "selection"
            let a = try cell(table, 2, 0), b = try cell(table, 2, 1)
            let setError = try set(table, "AXSelectedCells", [a, b] as CFArray)
            try require("rectangle-setter-applied", setError == .success && (try same(selected(table), [a, b])), "AX=\(setError.rawValue)")
            let aSelected = try attribute(a, "AXSelected").1 as? NSNumber
            let firstSelected = try attribute(first, "AXSelected").1 as? NSNumber
            try require("cell-selection-flags-coherent", aSelected?.boolValue == true && firstSelected?.boolValue == false)
            let saved = try selected(table)
            let sparse = try cell(table, 3, 2)
            let sparseError = try set(table, "AXSelectedCells", [a, sparse] as CFArray)
            try require("sparse-selection-refused-atomically", try same(selected(table), saved) && refusal(sparseError), "AX=\(sparseError.rawValue)")
            let duplicateError = try set(table, "AXSelectedCells", [a, a, b] as CFArray)
            try require("duplicate-selection-deduplicated", duplicateError == .success && (try same(selected(table), saved)))
            let overError = try set(table, "AXSelectedCells", Array(repeating: a, count: 8193) as CFArray)
            try require("over-budget-selection-refused", try same(selected(table), saved) && refusal(overError), "AX=\(overError.rawValue)")
            let rowError = try set(table, "AXSelectedRows", [rows[0]] as CFArray)
            try require("row-setter-cannot-bypass", try same(selected(table), saved) && refusal(rowError), "AX=\(rowError.rawValue)")
            let sourceAfterSelection = try range(source, "AXSelectedTextRange")
            try require("grid-selection-does-not-mutate-source", sourceAfterSelection?.location == originalSourceSelection?.location && sourceAfterSelection?.length == originalSourceSelection?.length)
            let clearError = try set(table, "AXSelectedCells", [] as CFArray)
            try require("empty-selection-clears", clearError == .success && (try selected(table)).isEmpty)

            phase = "logical-navigation"
            let menuError = try action(table, "AXShowMenu")
            if menuError != .success {
                // Diagnose once, read-only, and preserve the original action falsifier.
                // No retry, menu press, relaxed predicate, or readiness wait is admitted.
                phase = "action-failure-diagnostic"
                do {
                    try observeActionNames(table)
                    try observeShownMenu(table, category: "table-modern", key: NSAccessibility.Attribute.shownMenu.rawValue)
                    try observeShownMenu(app, category: "application-modern", key: NSAccessibility.Attribute.shownMenu.rawValue)
                    try observeShownMenu(table, category: "table-legacy")
                    try observeShownMenu(app, category: "application-legacy")
                    _ = try matches("AXMenuItem", "Go to row:column…")
                    actionFailureFollowup = "completed-read-only"
                } catch {
                    actionFailureFollowup = error is GateFailure ? "bounded-gate-failure" : "client-error"
                }
                phase = "logical-navigation"
            }
            try require("context-menu-accessible", menuError == .success, "AX=\(menuError.rawValue); no key-injection fallback")
            try observeShownMenu(table, category: "table-modern", key: NSAccessibility.Attribute.shownMenu.rawValue)
            try observeShownMenu(table, category: "table-legacy")
            try observeShownMenu(app, category: "application-legacy")
            var goItem: AXUIElement?
            let menuFound = try wait(3) {
                let items = try self.matches("AXMenuItem", "Go to row:column…")
                if items.count == 1 { goItem = items[0]; return true }; return false
            }
            try require("unique-coordinate-menu-item", menuFound)
            guard let goItem else { throw GateFailure(reason: "coordinate menu unavailable") }
            try require("coordinate-menu-press", try action(goItem, "AXPress") == .success)
            var field: AXUIElement?, goButton: AXUIElement?
            let promptFound = try wait(3) {
                let nodes = try self.tree()
                var buttons: [AXUIElement] = [], fields: [AXUIElement] = []
                var messageCount = 0
                for node in nodes {
                    let role = try self.text(node, "AXRole")
                    if role == "AXButton", try self.label(node) == "Go" { buttons.append(node) }
                    if role == "AXTextField" { fields.append(node) }
                    if role == "AXStaticText", try self.text(node, "AXValue") == "Go to CSV row:column" { messageCount += 1 }
                }
                if buttons.count == 1 && fields.count == 1 && messageCount == 1 {
                    field = fields[0]; goButton = buttons[0]; return true
                }; return false
            }
            try require("unique-coordinate-prompt", promptFound)
            guard let field, let goButton else { throw GateFailure(reason: "coordinate prompt unavailable") }
            try require("numeric-coordinate-only-setter", try set(field, "AXValue", "1001:17" as CFString) == .success)
            try require("coordinate-prompt-press", try action(goButton, "AXPress") == .success)
            var shifted: AXUIElement?
            let shiftedReady = try wait(5) {
                let tables = try self.matches("AXTable", "CSV grid window")
                guard tables.count == 1 else { return false }
                let r = try self.elements(tables[0], "AXRows", limit: 256)
                let c = try self.elements(tables[0], "AXColumns", limit: 64)
                guard !r.isEmpty && !c.isEmpty else { return false }
                let rowLabel = try self.label(r[0]), columnLabel = try self.label(c[0])
                if rowLabel == "Row 1001" && columnLabel == "Column 17" {
                    shifted = tables[0]; return true
                }; return false
            }
            try require("absolute-ordinals-after-logical-jump", shiftedReady)
            guard let shifted else { throw GateFailure(reason: "shifted table absent") }
            let remote = try cell(shifted, 0, 0)
            let remoteReady = try wait(4) { try self.text(remote, "AXValue") == "r01001c17" }
            try require("shifted-value-matches-absolute-coordinate", remoteReady)
            let remoteRow = try range(remote, "AXRowIndexRange"), remoteColumn = try range(remote, "AXColumnIndexRange")
            try require("shifted-ranges-still-local", remoteRow?.location == 0 && remoteRow?.length == 1 && remoteColumn?.location == 0 && remoteColumn?.length == 1)
            // A verified retained handle may have lost PID availability after retirement.
            // Direct queries remain scoped to that original handle, never a replacement.
            var retiredValue: CFTypeRef?
            let retiredError = AXUIElementCopyAttributeValue(first, "AXValue" as CFString, &retiredValue)
            var retiredRaw: CFTypeRef?
            let retiredRangeError = AXUIElementCopyAttributeValue(first, "AXRowIndexRange" as CFString, &retiredRaw)
            var retiredEmpty = retiredRaw == nil
            if let retiredRaw, CFGetTypeID(retiredRaw) == AXValueGetTypeID() {
                let value = unsafeBitCast(retiredRaw, to: AXValue.self)
                var oldRange = CFRange(location: 0, length: 0)
                retiredEmpty = AXValueGetType(value) == .cfRange && AXValueGetValue(value, .cfRange, &oldRange) && oldRange.length == 0
            }
            try require("retained-old-cell-cannot-be-recycled", retiredValue == nil && retiredEmpty && unavailable(retiredError) && unavailable(retiredRangeError),
                        "value AX=\(retiredError.rawValue), range AX=\(retiredRangeError.rawValue)")
            try require("remote-selection-applied", try set(shifted, "AXSelectedCells", [remote] as CFArray) == .success && same(selected(shifted), [remote]))
            let mixedError = try set(shifted, "AXSelectedCells", [remote, first] as CFArray)
            try require("mixed-retired-selection-refused", try same(selected(shifted), [remote]) && refusal(mixedError), "AX=\(mixedError.rawValue)")
            try require("source-still-full-after-navigation", try sourceString(source, tail.location, tail.length) == "r01100c24")

            phase = "normal-close"
            let windows = try elements(app, "AXWindows", limit: 8)
            try require("one-fixture-window", windows.count == 1)
            let (_, closeRaw) = try attribute(windows[0], "AXCloseButton")
            guard let closeRaw, CFGetTypeID(closeRaw) == AXUIElementGetTypeID() else { throw GateFailure(reason: "close button missing") }
            let closeButton = unsafeBitCast(closeRaw, to: AXUIElement.self)
            try require("normal-close-press", try action(closeButton, "AXPress") == .success)
            closed = true
            // Process exit may invalidate PID readback: query this retained handle directly,
            // without traversing any new object or accepting a new PID.
            var oldValue: CFTypeRef?
            var staleError = AXError.success
            let noOldValue = try wait(2) {
                oldValue = nil
                staleError = AXUIElementCopyAttributeValue(remote, "AXValue" as CFString, &oldValue)
                return oldValue == nil
            }
            // A transport error alone is not retirement evidence; Run.ps1 additionally
            // requires normal editor exit before accepting this close phase.
            try require("closed-cell-no-old-value", noOldValue, "AX=\(staleError.rawValue); conditional on wrapper normal exit")
            phase = "complete"
            return report("passed", trusted, "External bounded AX API gate only; no VoiceOver/IME/geometry/performance acceptance")
        } catch {
            return report("failed", trusted, (error as? GateFailure)?.reason ?? "client-error")
        }
    }
    /// Preserve prior checks without promoting a partial run to acceptance.
    func report(_ status: String, _ trusted: Bool, _ note: String) -> Report {
        Report(status: status, phase: phase, editorPID: pid, clientPID: getpid(), trusted: trusted, closedByProbe: closed, admissionCount: queries,
            diagnostics: Diagnostics(showMenuWireKeyMatches: NSAccessibility.Action.showMenu.rawValue == "AXShowMenu",
                actionFailureFollowup: actionFailureFollowup, actionNamesError: actionNamesError,
                actionNamesCount: actionNamesCount, showMenuActionAdvertised: showMenuActionAdvertised, shownMenuRelations: shownMenuRelations, elapsedSeconds: ProcessInfo.processInfo.systemUptime - started,
                phaseAdmissions: phaseAdmissions, phasePolls: phasePolls, phaseTraversals: phaseTraversals,
                maximumTreeNodes: maximumTreeNodes, lastTreeMenus: lastTreeMenus, lastTreeMenuItems: lastTreeMenuItems,
                lastExactMenuMatches: lastExactMenuMatches, lastExactMenuTitleMatches: lastExactMenuTitleMatches), checks: checks, observations: observations, note: note)
    }
}

var result: Report
if CommandLine.arguments.count == 3, let pid = Int32(CommandLine.arguments[1]), pid > 0 {
    do { result = try Probe(pid: pid, fixture: CommandLine.arguments[2]).run() }
    catch { result = Report(status: "probe-error", phase: "fixture", editorPID: pid, clientPID: getpid(), trusted: false, closedByProbe: false, admissionCount: 0, diagnostics: nil, checks: [], observations: [], note: "synthetic fixture unavailable") }
} else {
    result = Report(status: "probe-error", phase: "arguments", editorPID: 0, clientPID: getpid(), trusted: false, closedByProbe: false, admissionCount: 0, diagnostics: nil, checks: [], observations: [], note: "expected editor PID and synthetic CSV")
}
let encoder = JSONEncoder()
encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
if let data = try? encoder.encode(result), let json = String(data: data, encoding: .utf8) { print(json) }
exit(result.status == "passed" || result.status == "external-accessibility-unavailable" ? 0 : 1)
