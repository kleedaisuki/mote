using System.Runtime.Versioning;

namespace Mote.Native.Mac;

/// <summary>Checks primary and reporter fault containment through the actual AppKit posted queue.</summary>
/// <remarks>
/// Used only by the existing isolated Flow diagnostic. The hostile Message getter
/// fails before NSAlert construction, so this check never presents a modal dialog.
/// Counts are UI-thread confined; no input, clipboard, tracing, or system settings change.
/// </remarks>
[SupportedOSPlatform("macos")]
internal sealed class MacPostedCallbackProbe
{
    /// <summary>Rejects accidental duplicate queueing rather than silently retrying the fault.</summary>
    private bool _queued;
    /// <summary>Number of actual dequeued invocations, not number of queue requests.</summary>
    private int _callbackCalls;
    /// <summary>Number of actual platform reporter accesses to the hostile exception message.</summary>
    private int _messageCalls;

    /// <summary>Queues exactly one nonfatal primary fault before the caller's later successful item.</summary>
    internal void Queue(MacEditorShell shell)
    {
        if (_queued) throw new InvalidOperationException("Posted fault probe queued twice.");
        _queued = true;
        shell.Post(() =>
        {
            _callbackCalls++;
            throw new ReporterFaultException(this);
        });
    }

    /// <summary>Requires the later item to observe one primary invocation and one contained report fault.</summary>
    internal void VerifyContinuation()
    {
        if (!_queued || _callbackCalls != 1 || _messageCalls != 1)
            throw new InvalidOperationException("Posted fault probe did not contain both faults exactly once.");
        Console.WriteLine("Mac posted callback primary/report fault containment passed.");
    }

    /// <summary>Injects a secondary managed reporter failure before any native modal UI is entered.</summary>
    private sealed class ReporterFaultException(MacPostedCallbackProbe owner) : Exception
    {
        /// <summary>Counts the platform reporter access, then throws without producing exception text.</summary>
        public override string Message
        {
            get
            {
                owner._messageCalls++;
                throw new InvalidOperationException("Posted fault probe reporter failure.");
            }
        }
    }
}
