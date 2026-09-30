# Observe only the ordinary product's UIA tree; never read TextPattern document text.
param(
    [Parameter(Mandatory)][long] $WindowHandle,
    [Parameter(Mandatory)][long] $CanvasHandle,
    [Parameter(Mandatory)][int] $ExpectedPid
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Windows UI Automation is required.' }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ContinuousFocusProbe {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(
        IntPtr window, out uint processId);
    public static uint ForegroundPid() {
        uint pid;
        GetWindowThreadProcessId(GetForegroundWindow(), out pid);
        return pid;
    }
}
'@

function Get-DocumentNodes {
    param(
        [System.Windows.Automation.AutomationElement] $Root,
        [System.Windows.Automation.TreeWalker] $Walker
    )
    $stack = [Collections.Stack]::new()
    $stack.Push($Root)
    $documents = [Collections.Generic.List[object]]::new()
    $visited = 0
    while ($stack.Count -gt 0) {
        $node = [System.Windows.Automation.AutomationElement]$stack.Pop()
        $visited++
        if ($visited -gt 256) { throw 'UIA source subtree exceeded the bounded probe.' }
        if ($node.Current.ControlType -eq [System.Windows.Automation.ControlType]::Document) {
            $valueObject = $null
            $textObject = $null
            $hasValue = $node.TryGetCurrentPattern(
                [System.Windows.Automation.ValuePattern]::Pattern, [ref]$valueObject)
            $hasText = $node.TryGetCurrentPattern(
                [System.Windows.Automation.TextPattern]::Pattern, [ref]$textObject)
            $name = [string]$node.Current.Name
            $safeName = if ($name -cmatch '^(Mote editor|Mote preview|Preview|Document preview|RichEdit Control)$') {
                $name
            }
            else { "<unrecognized-name-length:$($name.Length)>" }
            $parent = $Walker.GetParent($node)
            $documents.Add([ordered]@{
                automation_id = [string]$node.Current.AutomationId
                accessible_name = $safeName
                name_length = $name.Length
                class_name = [string]$node.Current.ClassName
                native_window_handle = [int]$node.Current.NativeWindowHandle
                parent_automation_id = if ($null -eq $parent) { $null }
                    else { [string]$parent.Current.AutomationId }
                is_enabled = [bool]$node.Current.IsEnabled
                is_keyboard_focusable = [bool]$node.Current.IsKeyboardFocusable
                has_keyboard_focus = [bool]$node.Current.HasKeyboardFocus
                has_text_pattern = [bool]$hasText
                text_is_read_only = if ($hasText) {
                    $textPattern = [System.Windows.Automation.TextPattern]$textObject
                    $readonlyAttribute = $textPattern.DocumentRange.GetAttributeValue(
                        [System.Windows.Automation.TextPattern]::IsReadOnlyAttribute)
                    if ($readonlyAttribute -is [bool]) { $readonlyAttribute }
                    else { $null }
                } else { $null }
                has_value_pattern = [bool]$hasValue
                value_is_read_only = if ($hasValue) {
                    [bool]([System.Windows.Automation.ValuePattern]$valueObject).Current.IsReadOnly
                } else { $null }
            })
        }
        $child = $Walker.GetFirstChild($node)
        while ($null -ne $child) {
            $stack.Push($child)
            $child = $Walker.GetNextSibling($child)
        }
    }
    return $documents.ToArray()
}

$window = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]::new($WindowHandle))
$canvas = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]::new($CanvasHandle))
if ($null -eq $window -or $window.Current.ProcessId -ne $ExpectedPid -or
    $null -eq $canvas -or $canvas.Current.ProcessId -ne $ExpectedPid) {
    throw 'Window/Canvas AX root is absent or belongs to another process.'
}
$foregroundBefore = [ContinuousFocusProbe]::ForegroundPid()
$raw = @(Get-DocumentNodes $window ([System.Windows.Automation.TreeWalker]::RawViewWalker))
$control = @(Get-DocumentNodes $window ([System.Windows.Automation.TreeWalker]::ControlViewWalker))
$content = @(Get-DocumentNodes $window ([System.Windows.Automation.TreeWalker]::ContentViewWalker))
$canvasRaw = @(Get-DocumentNodes $canvas ([System.Windows.Automation.TreeWalker]::RawViewWalker))
$focused = [System.Windows.Automation.AutomationElement]::FocusedElement
$focusedPid = if ($null -eq $focused) { 0 } else { $focused.Current.ProcessId }
$focusedId = if ($null -eq $focused) { '' } else { [string]$focused.Current.AutomationId }
$foregroundAfter = [ContinuousFocusProbe]::ForegroundPid()
$focusStatus = if ($foregroundBefore -ne $ExpectedPid -or
    $foregroundAfter -ne $ExpectedPid) { 'inconclusive-foreign-or-changing-foreground' }
elseif ($focusedPid -eq $ExpectedPid -and
    $focusedId -ceq 'mote.source.document') { 'source-focused' }
else { 'foreground-focus-mismatch' }
@{
    window_pid = $window.Current.ProcessId
    canvas_pid = $canvas.Current.ProcessId
    raw_documents = $raw
    control_documents = $control
    content_documents = $content
    canvas_raw_documents = $canvasRaw
    foreground_pid_before = $foregroundBefore
    foreground_pid_after = $foregroundAfter
    focused_pid = $focusedPid
    focused_automation_id = $focusedId
    focus_status = $focusStatus
} | ConvertTo-Json -Compress -Depth 4
