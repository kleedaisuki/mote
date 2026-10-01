using System.Threading.Channels;

namespace Mote.Native;

/// <summary>Content-free boundaries of one ordinary Save; never Save acceptance.</summary>
internal enum NativeSaveDiagnosticStage
{
    /// <summary>The AppKit menu selector entered, before composition or shell guards.</summary>
    SelectorEntered,
    /// <summary>The controller passed synchronous guards, before scheduling its worker.</summary>
    ControllerAdmitted
}

/// <summary>
/// Default-off, process-owned Save observation. The UI only admits fixed enums;
/// an independent background thread writes the inherited diagnostic pipe.
/// </summary>
internal static class NativeSaveDiagnostic
{
    /// <summary>Null in ordinary editing; hooks never initialize or read the environment.</summary>
    private static NativeSaveDiagnosticSession? s_session;

    /// <summary>Called once by the Mac owner before entering AppKit, not by a Save hook.</summary>
    internal static void Initialize()
    {
        try
        {
            if (Environment.GetEnvironmentVariable("MOTE_NATIVE_MAC_SAVE_TRACE") != "1") return;
            Volatile.Write(ref s_session, new NativeSaveDiagnosticSession(Console.OpenStandardError));
        }
        catch (Exception)
        {
            // No diagnostic initialization failure may change ordinary product startup.
        }
    }

    /// <summary>Nonwaiting observation; no clocks, formatting, file I/O or lazy initialization.</summary>
    internal static void Record(NativeSaveDiagnosticStage stage) =>
        Volatile.Read(ref s_session)?.TryRecord(stage);

    /// <summary>
    /// Called after the AppKit loop and owner-thread teardown. No native Save hook
    /// may execute after this lifetime boundary; a stuck writer is abandoned.
    /// </summary>
    internal static void Shutdown()
    {
        Interlocked.Exchange(ref s_session, null)?.Shutdown(TimeSpan.FromMilliseconds(100));
    }
}

/// <summary>
/// Bounded transport with linearized producer closure. Admitted producers report
/// loss before release; channel completion follows the last such release.
/// Calls rejected after closure lie outside the closed producer lifetime.
/// </summary>
internal sealed class NativeSaveDiagnosticSession
{
    /// <summary>Only fixed stages occupy this bounded queue; no silent dropping modes.</summary>
    private readonly Channel<NativeSaveDiagnosticStage> _channel = Channel.CreateBounded<NativeSaveDiagnosticStage>(
        new BoundedChannelOptions(16)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    /// <summary>Opens an owned wrapper on the writer; never dispose it concurrently.</summary>
    private readonly Func<Stream> _openOutput;
    /// <summary>Dedicated background thread; writes never resume on the Save task pool.</summary>
    private readonly Thread _writer;
    /// <summary>Synchronous wake avoids a channel ValueTask continuation on the shared task pool.</summary>
    private readonly AutoResetEvent _wake = new(false);
    /// <summary>Only a successful writer join may retire the wake handle, once.</summary>
    private int _wakeDisposed;
    /// <summary>High bit closes admission; remaining bits count admitted in-flight producers.</summary>
    private int _admission;
    /// <summary>Saturated loss bit, published before admitted producer release.</summary>
    private int _lost;
    /// <summary>Suppresses a not-yet-started terminal record after bounded shutdown abandonment.</summary>
    private int _abandoned;

    /// <summary>Starts the diagnostic writer. Tests inject bounded/blocked/broken streams.</summary>
    internal NativeSaveDiagnosticSession(Func<Stream> openOutput)
    {
        _openOutput = openOutput;
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "mote-save-diagnostic" };
        _writer.Start();
    }

    /// <summary>
    /// Attempts one fixed observation. A false result means queue loss or closed
    /// lifetime; only admitted queue loss contributes to the terminal watermark.
    /// </summary>
    internal bool TryRecord(NativeSaveDiagnosticStage stage)
    {
        if (!TryAdmit()) return false;
        try
        {
            if ((stage is NativeSaveDiagnosticStage.SelectorEntered or NativeSaveDiagnosticStage.ControllerAdmitted) &&
                _channel.Writer.TryWrite(stage))
            {
                SignalWriter();
                return true;
            }
            Interlocked.Exchange(ref _lost, 1);
            return false;
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref _lost, 1);
            return false; // Diagnostics cannot escape the unmanaged selector.
        }
        finally
        {
            if (Interlocked.Decrement(ref _admission) == int.MinValue)
                CompleteProducers();
        }
    }

    /// <summary>Admits a producer atomically without waiting on the writer or doing pipe I/O.</summary>
    private bool TryAdmit()
    {
        var state = Volatile.Read(ref _admission);
        while (state >= 0)
        {
            if (state == int.MaxValue) return false;
            var observed = Interlocked.CompareExchange(ref _admission, state + 1, state);
            if (observed == state) return true;
            state = observed;
        }
        return false;
    }

    /// <summary>
    /// Closes producer lifetime and joins for only the supplied budget. False is
    /// censored transport completion, never absence of a Save selector/admission.
    /// A terminal raw write already in progress cannot safely be cancelled; its
    /// later receipt proves only the stable watermark, not a timely writer join.
    /// </summary>
    internal bool Shutdown(TimeSpan budget)
    {
        var state = Volatile.Read(ref _admission);
        while (state >= 0)
        {
            var observed = Interlocked.CompareExchange(ref _admission, state | int.MinValue, state);
            if (observed == state)
            {
                if (state == 0) CompleteProducers();
                break;
            }
            state = observed;
        }
        if (_writer.Join(budget))
        {
            if (Interlocked.Exchange(ref _wakeDisposed, 1) == 0) _wake.Dispose();
            return true;
        }
        Volatile.Write(ref _abandoned, 1);
        return false;
    }

    /// <summary>Channel closure follows all admitted releases; wake never awaits a task-pool continuation.</summary>
    private void CompleteProducers()
    {
        _channel.Writer.TryComplete();
        SignalWriter();
    }

    /// <summary>
    /// Nonwaiting wake. A final releaser can race a joined writer's handle retirement;
    /// that redundant signal must not escape a diagnostic hook. Abandonment retains
    /// the handle for the still-live writer rather than disposing under WaitOne.
    /// </summary>
    private void SignalWriter()
    {
        try { _wake.Set(); }
        catch (Exception) { } // Wake failure censors the stream; never changes Save behavior.
    }

    /// <summary>
    /// Synchronously writes on this dedicated thread only. Stream disposal is
    /// also writer-owned; shutdown never closes a handle underneath blocked I/O.
    /// </summary>
    private void WriteLoop()
    {
        try
        {
            using var output = _openOutput();
            output.Write("mote-save-diag-v1:ready\n"u8);
            while (true)
            {
                DrainStages(output);
                if (Volatile.Read(ref _admission) == int.MinValue)
                {
                    // A producer may have enqueued between the preceding empty
                    // read and closure observation. Closure now makes this final
                    // drain stable: no later admitted enqueue remains possible.
                    DrainStages(output);
                    break;
                }
                _wake.WaitOne();
            }
            if (Volatile.Read(ref _abandoned) != 0) return;
            if (Volatile.Read(ref _lost) != 0) output.Write("mote-save-diag-v1:overflow\n"u8);
            if (Volatile.Read(ref _abandoned) == 0) output.Write("mote-save-diag-v1:completed\n"u8);
        }
        catch (Exception)
        {
            // A broken inherited pipe is unavailable evidence, never a Save failure.
        }
    }

    /// <summary>Only this background writer reads the queue; no async channel wait is registered.</summary>
    private void DrainStages(Stream output)
    {
        while (_channel.Reader.TryRead(out var stage)) WriteStage(output, stage);
    }

    /// <summary>Maps only the two closed enum values to fixed ASCII byte literals.</summary>
    private static void WriteStage(Stream output, NativeSaveDiagnosticStage stage)
    {
        if (stage == NativeSaveDiagnosticStage.SelectorEntered)
            output.Write("mote-save-diag-v1:selector_entered\n"u8);
        else output.Write("mote-save-diag-v1:controller_admitted\n"u8);
    }
}
