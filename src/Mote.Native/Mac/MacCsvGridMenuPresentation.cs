namespace Mote.Native.Mac;

/// <summary>Main-thread state for one deferred native menu request; no native handle or document is retained.</summary>
internal struct MacCsvGridMenuPresentation
{
    /// <summary>The exact installation that admitted the pending selector, or no pending request.</summary>
    private long? _pending;
    /// <summary>Rejects reentrant AX requests throughout native popup tracking, including its close callback.</summary>
    private bool _presenting;

    /// <summary>Admits at most one request and never queues during an existing native popup.</summary>
    internal bool TryQueue(long installation)
    {
        if (_pending is not null || _presenting) return false;
        _pending = installation;
        return true;
    }

    /// <summary>Consumes the request once and refuses detached or superseded installations without displaying anything.</summary>
    internal bool TryBegin(long installation, bool attached)
    {
        var pending = _pending;
        _pending = null;
        if (!attached || pending != installation || _presenting) return false;
        _presenting = true;
        return true;
    }

    /// <summary>Called after the native popup returns, not when a menu merely announces that it will close.</summary>
    internal void End() => _presenting = false;

    /// <summary>Invalidates only this attachment's pending callback; in-progress native tracking has its own cancellation.</summary>
    internal void CancelPending() => _pending = null;
}
