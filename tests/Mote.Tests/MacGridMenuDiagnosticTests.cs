using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Mote.Native.Mac;

namespace Mote.Tests;

/// <summary>Portable protocol/counter checks; they do not load AppKit or establish native menu visibility.</summary>
public sealed class MacGridMenuDiagnosticTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NativeReturnDoesNotInventDelegateTransitions(bool success)
    {
        var counter = new MacCsvGridMenuDiagnostic();
        Assert.True(counter.TryNext(MacCsvGridMenuPhase.ShowEnter, out var enter));
        Assert.Equal(new(MacCsvGridMenuPhase.ShowEnter, 1, 1, 0, 0, false), enter);
        Assert.True(counter.TryNext(MacCsvGridMenuPhase.NativeReturn, out var returned));
        Assert.Equal(new(MacCsvGridMenuPhase.NativeReturn, 2, 1, 0, 0, false), returned);
        Assert.Contains($"open=0 result={(success ? 1 : 0)}", returned.Format(new(false, 0, false, false, false, false, false), success));
    }

    [Fact]
    public void DelegateEventsDistinguishOpenAfterReturnFromCloseBeforeReturn()
    {
        var asyncCounter = new MacCsvGridMenuDiagnostic();
        Assert.True(asyncCounter.TryNext(MacCsvGridMenuPhase.ShowEnter, out _));
        Assert.True(asyncCounter.TryNext(MacCsvGridMenuPhase.NativeReturn, out _));
        Assert.True(asyncCounter.TryNext(MacCsvGridMenuPhase.WillOpen, out var asyncOpen));
        Assert.Equal(new(MacCsvGridMenuPhase.WillOpen, 3, 1, 1, 0, true), asyncOpen);

        var syncCounter = new MacCsvGridMenuDiagnostic();
        Assert.True(syncCounter.TryNext(MacCsvGridMenuPhase.ShowEnter, out _));
        Assert.True(syncCounter.TryNext(MacCsvGridMenuPhase.WillOpen, out _));
        Assert.True(syncCounter.TryNext(MacCsvGridMenuPhase.DidClose, out _));
        Assert.True(syncCounter.TryNext(MacCsvGridMenuPhase.NativeReturn, out var syncReturn));
        Assert.Equal(new(MacCsvGridMenuPhase.NativeReturn, 4, 1, 1, 1, false), syncReturn);
    }

    [Fact]
    public void BudgetAndUnknownPhasesRefuseWithoutCounterGrowth()
    {
        var counter = new MacCsvGridMenuDiagnostic();
        Assert.False(counter.TryNext((MacCsvGridMenuPhase)99, out var refused));
        Assert.Equal(default, refused);
        for (var i = 1; i <= MacCsvGridMenuDiagnostic.Limit; i++)
        {
            Assert.True(counter.TryNext(MacCsvGridMenuPhase.WillOpen, out var trace));
            Assert.Equal(i, trace.Sequence);
            Assert.Equal(i, trace.Opens);
        }
        for (var i = 0; i < 100; i++) Assert.False(counter.TryNext(MacCsvGridMenuPhase.DidClose, out _));
    }

    [Fact]
    public void ProtocolIsInvariantAndContainsOnlyFixedAsciiFields()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            var sample = new MacCsvGridMenuTrace(MacCsvGridMenuPhase.ShowEnter, 1, 1, 0, 0, false);
            var line = sample.Format(new(true, 12, true, false, true, false, true));
            Assert.Equal("mote-grid-menu-v1 phase=show-enter seq=1 requests=1 opens=0 closes=0 open=0 result=-1 configured=1 items=12 coordinate=1 shown=0 key=1 first=0 active=1 allowaction=0 allowshown=0", line);
            Assert.True(line.Length < 384);
            Assert.Throws<ArgumentOutOfRangeException>(() => sample.Format(new(true, 17, false, false, false, false, false)));
            Assert.Throws<ArgumentOutOfRangeException>(() => sample.Format(new(true, -2, false, false, false, false, false)));
            Assert.Throws<ArgumentException>(() => sample.Format(new(false, 0, false, false, false, false, false), true));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PermissionObservationsRemainIndependentFromMenuTransition(bool action, bool shown)
    {
        var counter = new MacCsvGridMenuDiagnostic();
        Assert.True(counter.TryNext(MacCsvGridMenuPhase.WillOpen, out var trace));
        var line = trace.Format(new(true, 12, true, true, true, false, true, action, shown));
        Assert.EndsWith($"allowaction={(action ? 1 : 0)} allowshown={(shown ? 1 : 0)}", line);
        Assert.Contains("opens=1 closes=0 open=1 result=-1", line);
        Assert.True(line.Length < 384);
    }

    [Fact]
    public void EveryAdmittedPhaseCarriesBothPermissionFacts()
    {
        var counter = new MacCsvGridMenuDiagnostic();
        foreach (var phase in Enum.GetValues<MacCsvGridMenuPhase>())
        {
            Assert.True(counter.TryNext(phase, out var trace));
            bool? result = phase is MacCsvGridMenuPhase.NativeReturn or MacCsvGridMenuPhase.ScheduleReturn or
                MacCsvGridMenuPhase.PopupReturn ? true : null;
            var line = trace.Format(new(true, 12, true, true, true, false, true, true, true), result);
            Assert.EndsWith("active=1 allowaction=1 allowshown=1", line);
            Assert.True(line.Length < 384);
        }
    }

    [Fact]
    public void NativeBoolBridgeDeclaresExactByteReturn()
    {
#pragma warning disable CA1416 // Inspecting method metadata does not call a macOS native entry point.
        var bridge = typeof(MacCsvGrid).GetMethod("AccessibilityNativeBool", BindingFlags.NonPublic | BindingFlags.Static)!;
#pragma warning restore CA1416
        Assert.Equal(typeof(byte), bridge.ReturnType);
        Assert.Equal([typeof(nint), typeof(nint)], bridge.GetParameters().Select(p => p.ParameterType));
        Assert.Equal("objc_msgSend", bridge.GetCustomAttribute<DllImportAttribute>()!.EntryPoint);
#pragma warning disable CA1416 // Metadata inspection does not call the macOS permission getter.
        var permission = typeof(MacCsvGrid).GetMethod("AccessibilitySelectorPermission", BindingFlags.NonPublic | BindingFlags.Static)!;
#pragma warning restore CA1416
        Assert.Equal(typeof(byte), permission.ReturnType);
        Assert.Equal([typeof(nint), typeof(nint), typeof(nint)], permission.GetParameters().Select(p => p.ParameterType));
        Assert.Equal("objc_msgSend", permission.GetCustomAttribute<DllImportAttribute>()!.EntryPoint);
    }
}
