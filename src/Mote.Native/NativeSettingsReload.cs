using Mote.Configuration;

namespace Mote.Native;

/// <summary>
/// Runs one bounded config read at a time, coalescing requests to the newest serial.
/// Call Request and Dispose on the UI thread. Publication never holds Engine state.
/// </summary>
internal sealed class NativeSettingsReload : IDisposable
{
    /// <summary>Background-only bounded reader, with no document locks.</summary>
    private readonly Func<MoteConfiguration> _load;
    /// <summary>Existing native UI dispatch; a disposed window may reject publication.</summary>
    private readonly Action<Action> _post;
    /// <summary>UI-only latest-snapshot publication, including a recoverable reader failure.</summary>
    private readonly Action<MoteConfiguration?, Exception?> _publish;
    /// <summary>Monotonic request identity; cancellation is not the correctness mechanism.</summary>
    private long _serial;
    /// <summary>One admitted read, including its pending UI completion.</summary>
    private bool _reading;
    /// <summary>UI-owned lifetime gate checked before every publication.</summary>
    private bool _disposed;

    /// <summary>Injectable I/O and UI publication make stale/disposal races reproducible.</summary>
    internal NativeSettingsReload(Func<MoteConfiguration> load, Action<Action> post,
        Action<MoteConfiguration?, Exception?> publish)
    {
        _load = load;
        _post = post;
        _publish = publish;
    }

    /// <summary>Requests a new snapshot; a running old read can finish but cannot publish.</summary>
    internal void Request()
    {
        if (_disposed) return;
        ++_serial;
        if (!_reading) Start(_serial);
    }

    /// <inheritdoc />
    public void Dispose() { _disposed = true; ++_serial; }

    private void Start(long serial)
    {
        _reading = true;
        _ = Task.Run(() =>
        {
            MoteConfiguration? config = null;
            Exception? error = null;
            try { config = _load(); }
            catch (Exception failure) when (failure is not OutOfMemoryException) { error = failure; }
            try { _post(() => Complete(serial, config, error)); }
            catch (Exception failure) when (failure is ObjectDisposedException or InvalidOperationException)
            { /* The native window no longer accepts completions. */ }
        });
    }

    private void Complete(long serial, MoteConfiguration? config, Exception? error)
    {
        _reading = false;
        if (_disposed) return;
        if (serial == _serial) _publish(config, error);
        else Start(_serial);
    }
}
