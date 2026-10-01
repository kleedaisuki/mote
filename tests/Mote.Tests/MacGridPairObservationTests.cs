using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Mote.Native.Mac;

namespace Mote.Tests;

/// <summary>Portable facts/path/ABI checks; no AppKit invocation or external AX success is implied.</summary>
public sealed class MacGridPairObservationTests
{
    [Fact]
    public void OffMainRefusalPreservesUnknownOwnerFacts()
    {
        var facts = new MacGridPairObservation();
        var entry = facts.Begin(null);
        facts.Complete(entry, new() { Main = false, Returned = false });
        using var report = JsonDocument.Parse(facts.Export());
        var root = report.RootElement;
        Assert.Equal(1, root.GetProperty("callback_entries").GetInt32());
        Assert.Equal(1, root.GetProperty("off_main_entries").GetInt32());
        var row = root.GetProperty("action_entries")[0];
        Assert.Equal(JsonValueKind.Null, row.GetProperty("owner_lookup").ValueKind);
        Assert.False(row.GetProperty("method_return").GetBoolean());
        Assert.False(root.GetProperty("dispatcher_observation_available").GetBoolean());
        Assert.Equal(0, root.GetProperty("server_calls").GetArrayLength());
    }

    [Fact]
    public void TotalEntriesAndLifecycleDoNotDisappearWhenRowBuffersOverflow()
    {
        var facts = new MacGridPairObservation { Ready = true };
        for (var i = 0; i < 20; i++)
        {
            var entry = facts.Begin(true);
            facts.Complete(entry, new(true, true, true, true, false, true, true, true, true, false, false, false));
            facts.Transition(2, true, true, true, true);
        }
        using var report = JsonDocument.Parse(facts.Export());
        var root = report.RootElement;
        Assert.Equal(20, root.GetProperty("callback_entries").GetInt32());
        Assert.Equal(4, root.GetProperty("entry_overflow").GetInt32());
        Assert.Equal(16, root.GetProperty("action_entries").GetArrayLength());
        Assert.Equal(20, root.GetProperty("opens").GetInt32());
        Assert.Equal(4, root.GetProperty("lifetime_overflow").GetInt32());
        Assert.Equal(16, root.GetProperty("lifetime").GetArrayLength());
        Assert.Equal(0, root.GetProperty("requests").GetInt32());
    }

    [Fact]
    public void MissingMainThreadObservationIsNotInventedOnCaughtException()
    {
        var facts = new MacGridPairObservation();
        var entry = facts.Begin(null);
        facts.Complete(entry, new() { Exception = true, Returned = false });
        using var report = JsonDocument.Parse(facts.Export());
        var row = report.RootElement.GetProperty("action_entries")[0];
        Assert.Equal(JsonValueKind.Null, row.GetProperty("on_main_thread").ValueKind);
        Assert.True(row.GetProperty("caught_exception").GetBoolean());
    }

    [Fact]
    public void SessionPathMustBeExistingAbsoluteCacheDescendant()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var root = directory!.FullName;
        var session = Path.Combine(root, ".cache", "tests", "mac-grid-pair", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(session);
#pragma warning disable CA1416 // Pure managed path validation does not call AppKit.
        Assert.Equal(session, MacCsvGrid.ResolvePairSession(root, session));
        Assert.Throws<ArgumentException>(() => MacCsvGrid.ResolvePairSession(root, Path.Combine(root, ".cache")));
        Assert.Throws<ArgumentException>(() => MacCsvGrid.ResolvePairSession(root, Path.Combine(root, ".cache-other")));
        Assert.Throws<ArgumentException>(() => MacCsvGrid.ResolvePairSession(root, ".cache/tests"));
        Assert.Throws<ArgumentException>(() => MacCsvGrid.ResolvePairSession(root, Path.Combine(session, "missing")));
#pragma warning restore CA1416
    }

    [Fact]
    public void TimerBridgeUsesExactScalarObjectAndBoolArgumentClasses()
    {
#pragma warning disable CA1416 // Metadata-only ABI check.
        var method = typeof(MacCsvGrid).GetMethod("PairTimerCreate", BindingFlags.NonPublic | BindingFlags.Static)!;
#pragma warning restore CA1416
        Assert.Equal(typeof(nint), method.ReturnType);
        Assert.Equal([typeof(nint), typeof(nint), typeof(double), typeof(nint), typeof(nint), typeof(nint), typeof(byte)],
            method.GetParameters().Select(p => p.ParameterType));
        Assert.Equal("objc_msgSend", method.GetCustomAttribute<DllImportAttribute>()!.EntryPoint);
    }
}
