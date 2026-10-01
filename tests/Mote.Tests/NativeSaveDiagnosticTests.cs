using System.Text;
using Mote.Native;

namespace Mote.Tests;

/// <summary>Portable transport invariants; no native input or Save acceptance is fabricated.</summary>
public sealed class NativeSaveDiagnosticTests
{
    /// <summary>Ordinary hooks allocate nothing after their null-reference cold path is warmed.</summary>
    [Fact]
    public void Disabled_hooks_do_not_allocate_or_initialize()
    {
        NativeSaveDiagnostic.Record(NativeSaveDiagnosticStage.SelectorEntered);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10000; index++)
            NativeSaveDiagnostic.Record(NativeSaveDiagnosticStage.ControllerAdmitted);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    /// <summary>Fixed stages, including duplicates, stay ordered between startup and terminal records.</summary>
    [Fact]
    public void Normal_close_drains_fixed_stages_on_background_writer()
    {
        using var output = new ControlledStream();
        var session = new NativeSaveDiagnosticSession(() => output);
        Assert.True(session.TryRecord(NativeSaveDiagnosticStage.SelectorEntered));
        Assert.True(session.TryRecord(NativeSaveDiagnosticStage.SelectorEntered));
        Assert.True(session.TryRecord(NativeSaveDiagnosticStage.ControllerAdmitted));
        Assert.True(session.Shutdown(TimeSpan.FromSeconds(2)));
        Assert.Equal(new[] { "ready", "selector_entered", "selector_entered", "controller_admitted", "completed" }, output.Stages());
        Assert.True(output.BackgroundOnly);
        Assert.False(session.TryRecord(NativeSaveDiagnosticStage.SelectorEntered));
    }

    /// <summary>Queue saturation cannot silently discard a marker beneath a healthy completion.</summary>
    [Fact]
    public void Overflow_is_reported_before_completion()
    {
        using var output = new ControlledStream(blockFirst: true);
        var session = new NativeSaveDiagnosticSession(() => output);
        Assert.True(output.Entered.Wait(TimeSpan.FromSeconds(2)));
        for (var index = 0; index < 16; index++) Assert.True(session.TryRecord(NativeSaveDiagnosticStage.SelectorEntered));
        Assert.False(session.TryRecord(NativeSaveDiagnosticStage.ControllerAdmitted));
        output.Release.Set();
        Assert.True(session.Shutdown(TimeSpan.FromSeconds(2)));
        var stages = output.Stages();
        Assert.Equal(19, stages.Length);
        Assert.Equal("overflow", stages[^2]);
        Assert.Equal("completed", stages[^1]);
    }

    /// <summary>A blocked raw write never requires UI-thread disposal or an unbounded shutdown.</summary>
    [Fact]
    public void Blocked_writer_is_abandoned_without_terminal_success()
    {
        using var output = new ControlledStream(blockFirst: true);
        var session = new NativeSaveDiagnosticSession(() => output);
        Assert.True(output.Entered.Wait(TimeSpan.FromSeconds(2)));
        Assert.True(session.TryRecord(NativeSaveDiagnosticStage.SelectorEntered));
        var started = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(session.Shutdown(TimeSpan.FromMilliseconds(10)));
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(1));
        Assert.False(output.Disposed);
        output.Release.Set();
        Assert.True(session.Shutdown(TimeSpan.FromSeconds(2)));
        Assert.DoesNotContain("completed", output.Stages());
        Assert.True(output.Disposed);
    }

    /// <summary>A started terminal syscall may finish late; its watermark is not a timely-join claim.</summary>
    [Fact]
    public async Task Already_started_terminal_write_may_arrive_after_shutdown_budget()
    {
        using var output = new ControlledStream(blockCompleted: true);
        var session = new NativeSaveDiagnosticSession(() => output);
        Assert.True(output.Entered.Wait(TimeSpan.FromSeconds(2)));
        var close = Task.Run(() => session.Shutdown(TimeSpan.FromMilliseconds(100)));
        Assert.True(output.Blocked.Wait(TimeSpan.FromSeconds(2)));
        Assert.False(await close.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(output.Disposed);
        Assert.DoesNotContain("completed", output.Stages());
        output.Release.Set();
        Assert.True(session.Shutdown(TimeSpan.FromSeconds(2)));
        Assert.Equal(new[] { "ready", "completed" }, output.Stages());
        Assert.True(output.Disposed);
    }

    /// <summary>Broken inherited pipes and failed opening are contained on the background writer.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Transport_failure_does_not_escape_hooks(bool failOpen)
    {
        using var output = new ControlledStream(failWrite: true);
        var session = new NativeSaveDiagnosticSession(() => failOpen ? throw new IOException("test") : output);
        session.TryRecord(NativeSaveDiagnosticStage.SelectorEntered);
        Assert.True(session.Shutdown(TimeSpan.FromSeconds(2)));
        Assert.DoesNotContain("completed", output.Stages());
    }

    /// <summary>Closure waits for all admitted losses/enqueues, not merely a momentarily empty queue.</summary>
    [Fact]
    public async Task Racing_producers_and_closure_preserve_admitted_write_watermark()
    {
        for (var round = 0; round < 40; round++)
        {
            using var output = new ControlledStream();
            var session = new NativeSaveDiagnosticSession(() => output);
            var accepted = 0;
            using var start = new ManualResetEventSlim();
            var producers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                start.Wait();
                for (var index = 0; index < 200; index++)
                    if (session.TryRecord(NativeSaveDiagnosticStage.ControllerAdmitted)) Interlocked.Increment(ref accepted);
            })).ToArray();
            start.Set();
            Assert.True(session.Shutdown(TimeSpan.FromSeconds(2)));
            await Task.WhenAll(producers).WaitAsync(TimeSpan.FromSeconds(2));
            var stages = output.Stages();
            Assert.Equal(accepted, stages.Count(stage => stage == "controller_admitted"));
            Assert.Equal("ready", stages[0]);
            Assert.Equal("completed", stages[^1]);
            Assert.InRange(stages.Count(stage => stage == "overflow"), 0, 1);
        }
    }

    /// <summary>Invalid internal enums are loss, never an invented controller marker.</summary>
    [Fact]
    public void Invalid_stage_is_loss_not_fabricated_marker()
    {
        using var output = new ControlledStream();
        var session = new NativeSaveDiagnosticSession(() => output);
        Assert.False(session.TryRecord((NativeSaveDiagnosticStage)999));
        Assert.True(session.Shutdown(TimeSpan.FromSeconds(2)));
        Assert.Equal(new[] { "ready", "overflow", "completed" }, output.Stages());
    }

    /// <summary>Owned test stream exposing precise writer blocking without external artifacts.</summary>
    private sealed class ControlledStream(bool blockFirst = false, bool failWrite = false, bool blockCompleted = false) : MemoryStream
    {
        /// <summary>Signals the first raw write, allowing deterministic queue saturation.</summary>
        internal ManualResetEventSlim Entered { get; } = new();
        /// <summary>Signals the exact deliberately blocked write, not merely startup.</summary>
        internal ManualResetEventSlim Blocked { get; } = new();
        /// <summary>Releases the first write; only tests wait on or signal this gate.</summary>
        internal ManualResetEventSlim Release { get; } = new();
        /// <summary>True only if every write was performed by a background thread.</summary>
        internal bool BackgroundOnly { get; private set; } = true;
        /// <summary>Observes wrapper disposal, including after abandonment release.</summary>
        internal bool Disposed { get; private set; }
        /// <summary>Serial writer invocation count; never accessed concurrently by producers.</summary>
        private int _writes;

        /// <summary>Controls raw writes but never formats or records unknown output.</summary>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            BackgroundOnly &= Thread.CurrentThread.IsBackground;
            if (_writes++ == 0)
            {
                Entered.Set();
                if (blockFirst)
                {
                    Blocked.Set();
                    Release.Wait();
                }
            }
            if (blockCompleted && buffer.SequenceEqual("mote-save-diag-v1:completed\n"u8))
            {
                Blocked.Set();
                Release.Wait();
            }
            if (failWrite) throw new IOException("test");
            base.Write(buffer);
        }

        /// <summary>Reads after successful writer join; MemoryStream retains bytes after disposal.</summary>
        internal string[] Stages() => Encoding.ASCII.GetString(ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line["mote-save-diag-v1:".Length..]).ToArray();

        /// <summary>Only the writer owns transport disposal; test teardown is idempotent.</summary>
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
