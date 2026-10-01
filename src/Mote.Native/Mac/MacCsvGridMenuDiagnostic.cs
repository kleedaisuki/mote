using System.Globalization;

namespace Mote.Native.Mac;

/// <summary>Fixed events from the existing Grid menu action and delegate; not menu display outcomes.</summary>
internal enum MacCsvGridMenuPhase
{
    /// <summary>The proxy is about to invoke the physical Table's menu action.</summary>
    ShowEnter,
    /// <summary>The physical action returned its actual BOOL result.</summary>
    NativeReturn,
    /// <summary>The custom AX action admitted/queued its request, not completed presentation.</summary>
    ScheduleReturn,
    /// <summary>A still-current deferred request is entering native menu tracking.</summary>
    PopupBegin,
    /// <summary>NSMenu tracking returned selected vs cancelled, not source-command completion.</summary>
    PopupReturn,
    /// <summary>AppKit notified the existing menu delegate that its menu will open.</summary>
    WillOpen,
    /// <summary>AppKit notified the existing menu delegate that its menu closed.</summary>
    DidClose
}

/// <summary>Content-free native facts read only for an admitted diagnostic event.</summary>
/// <param name="Configured">The physical Table has an installed menu.</param>
/// <param name="Items">Exact item count up to 16, or -1 when it exceeds the diagnostic bound.</param>
/// <param name="Coordinate">A bounded installed item has the exact established coordinate-command title.</param>
/// <param name="Shown">The physical current-menu getter is non-nil.</param>
/// <param name="Key">The physical Table's window is key.</param>
/// <param name="First">The physical Table is its window's first responder.</param>
/// <param name="Active">The process's NSApplication is active.</param>
internal readonly record struct MacCsvGridMenuFacts(bool Configured, int Items, bool Coordinate,
    bool Shown, bool Key, bool First, bool Active);

/// <summary>One fixed-format lifecycle sample, bounded independently of source/document size.</summary>
/// <param name="Phase">The admitted native action/delegate event.</param>
/// <param name="Sequence">One-based diagnostic order, at most 16.</param>
/// <param name="Requests">Observed ShowEnter count.</param>
/// <param name="Opens">Observed WillOpen callback count, not proof of painted menu visibility.</param>
/// <param name="Closes">Observed DidClose callback count.</param>
/// <param name="Open">The last observed delegate transition is WillOpen.</param>
internal readonly record struct MacCsvGridMenuTrace(MacCsvGridMenuPhase Phase, int Sequence,
    int Requests, int Opens, int Closes, bool Open)
{
    /// <summary>Only emits ASCII fixed names and bounded numbers, never native/source strings or identities.</summary>
    internal string Format(MacCsvGridMenuFacts facts, bool? result = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(facts.Items, -1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(facts.Items, MacCsvGridMenuDiagnostic.Limit);
        if ((Phase is MacCsvGridMenuPhase.NativeReturn or MacCsvGridMenuPhase.ScheduleReturn or MacCsvGridMenuPhase.PopupReturn) != result.HasValue)
            throw new ArgumentException("Only return events carry their respective admission/native result.", nameof(result));
        var phase = Phase switch
        {
            MacCsvGridMenuPhase.ShowEnter => "show-enter",
            MacCsvGridMenuPhase.NativeReturn => "native-return",
            MacCsvGridMenuPhase.ScheduleReturn => "schedule-return",
            MacCsvGridMenuPhase.PopupBegin => "popup-begin",
            MacCsvGridMenuPhase.PopupReturn => "popup-return",
            MacCsvGridMenuPhase.WillOpen => "will-open",
            MacCsvGridMenuPhase.DidClose => "did-close",
            _ => throw new ArgumentOutOfRangeException(nameof(Phase))
        };
        return string.Create(CultureInfo.InvariantCulture,
            $"mote-grid-menu-v1 phase={phase} seq={Sequence} requests={Requests} opens={Opens} closes={Closes} open={(Open ? 1 : 0)} result={(result is null ? -1 : result.Value ? 1 : 0)} configured={(facts.Configured ? 1 : 0)} items={facts.Items} coordinate={(facts.Coordinate ? 1 : 0)} shown={(facts.Shown ? 1 : 0)} key={(facts.Key ? 1 : 0)} first={(facts.First ? 1 : 0)} active={(facts.Active ? 1 : 0)}");
    }
}

/// <summary>Process-owned diagnostic counter with a hard 16-event ceiling; never instantiated on the normal path.</summary>
/// <remarks>Counts delegate observations, not inferred display or action completion. No native object is retained.</remarks>
internal sealed class MacCsvGridMenuDiagnostic
{
    /// <summary>Maximum process samples, maximum diagnostic menu-item scan and bounded counter values.</summary>
    internal const int Limit = 16;
    /// <summary>Number of samples admitted so far; it cannot grow after the ceiling.</summary>
    private int _sequence;
    /// <summary>Action requests independently counted from native callbacks.</summary>
    private int _requests;
    /// <summary>Observed open callbacks, with no guessed lifecycle transition.</summary>
    private int _opens;
    /// <summary>Observed close callbacks, with no invented matching open.</summary>
    private int _closes;
    /// <summary>Last observed delegate transition; absent callbacks are not inferred from native BOOL.</summary>
    private bool _open;

    /// <summary>Refuses an unknown phase or exhausted budget before native inspection or output allocation.</summary>
    internal bool TryNext(MacCsvGridMenuPhase phase, out MacCsvGridMenuTrace trace)
    {
        trace = default;
        if (_sequence >= Limit) return false;
        switch (phase)
        {
            case MacCsvGridMenuPhase.ShowEnter: _requests++; break;
            case MacCsvGridMenuPhase.NativeReturn:
            case MacCsvGridMenuPhase.ScheduleReturn:
            case MacCsvGridMenuPhase.PopupBegin:
            case MacCsvGridMenuPhase.PopupReturn: break;
            case MacCsvGridMenuPhase.WillOpen: _opens++; _open = true; break;
            case MacCsvGridMenuPhase.DidClose: _closes++; _open = false; break;
            default: return false;
        }
        trace = new(phase, ++_sequence, _requests, _opens, _closes, _open);
        return true;
    }
}
