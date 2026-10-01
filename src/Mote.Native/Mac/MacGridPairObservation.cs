using System.Text.Json;

namespace Mote.Native.Mac;

/// <summary>Preallocated, content-free first-pair facts; callbacks never format or perform I/O.</summary>
internal sealed class MacGridPairObservation
{
    /// <summary>Unexpected repeated entries remain counted; bounded rows cannot hide overflow.</summary>
    internal const int EntryLimit = 16, LifetimeLimit = 16;
    /// <summary>Serializes the rare off-main refusal with main-thread snapshots without native owner lookup.</summary>
    private readonly object _gate = new();
    /// <summary>Fixed entry rows; excess invocations remain in total/overflow accounting.</summary>
    private readonly Entry[] _entries = new Entry[EntryLimit];
    /// <summary>Fixed lifecycle rows sampled at existing managed admission/delegate/disposal sites.</summary>
    private readonly Life[] _life = new Life[LifetimeLimit];
    /// <summary>Total invocations/transitions, separate from stored row capacity and admission effects.</summary>
    private int _entryCount, _lifeCount, _offMain, _requests, _dispatches, _opens, _closes, _detaches;
    /// <summary>Main-thread ready/finish/owned-close facts; actual process exit belongs to the driver.</summary>
    internal bool Ready, Finish, NormalShutdown;

    /// <summary>Null fields are unobserved, especially for off-main owner state.</summary>
    internal record struct Entry(bool? Main, bool? Owner, bool? Current, bool? Attached,
        bool? Installing, bool? Frame, bool? Baseline, bool? Attachment, bool? Epoch,
        bool? Queued, bool? Returned, bool Exception);
    /// <summary>Only equality classes survive export; no native identity or document epoch number is retained.</summary>
    private readonly record struct Life(int Phase, bool Attachment, bool Epoch, bool Current, bool Attached);

    /// <summary>Records every action entry, including refusal; no native handle is retained.</summary>
    internal int Begin(bool? main)
    {
        lock (_gate)
        {
            var index = _entryCount++;
            if (index < EntryLimit) _entries[index] = new() { Main = main };
            return index;
        }
    }

    /// <summary>Completes one fixed fact row; an overflow entry still contributes to total accounting.</summary>
    internal void Complete(int index, Entry entry)
    {
        lock (_gate)
        {
            if (entry.Main == false) _offMain++;
            if (index >= 0 && index < EntryLimit) _entries[index] = entry;
        }
    }

    /// <summary>Captures existing lifecycle sites without requesting any AX/native attributes.</summary>
    internal void Transition(int phase, bool attachment, bool epoch, bool current, bool attached)
    {
        lock (_gate)
        {
            switch (phase)
            {
                case 0: _requests++; break;
                case 1: _dispatches++; break;
                case 2: _opens++; break;
                case 3: _closes++; break;
                case 4: _detaches++; break;
                default: throw new ArgumentOutOfRangeException(nameof(phase));
            }
            var index = _lifeCount++;
            if (index < LifetimeLimit) _life[index] = new(phase, attachment, epoch, current, attached);
        }
    }

    /// <summary>Exports only fixed keys, enum words and primitive facts outside accessibility callbacks.</summary>
    internal byte[] Export()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            lock (_gate)
            {
                writer.WriteStartObject();
                writer.WriteString("schema", "mote-grid-action-server-v1");
                writer.WriteBoolean("ready_published", Ready);
                writer.WriteBoolean("finish_consumed", Finish);
                writer.WriteBoolean("normal_shutdown", NormalShutdown);
                writer.WriteBoolean("dispatcher_observation_available", false);
                writer.WriteNumber("callback_entries", _entryCount);
                writer.WriteNumber("off_main_entries", _offMain);
                writer.WriteNumber("entry_overflow", Math.Max(0, _entryCount - EntryLimit));
                writer.WriteNumber("lifetime_overflow", Math.Max(0, _lifeCount - LifetimeLimit));
                writer.WriteNumber("requests", _requests); writer.WriteNumber("dispatches", _dispatches);
                writer.WriteNumber("opens", _opens); writer.WriteNumber("closes", _closes);
                writer.WriteNumber("detaches", _detaches);
                writer.WriteStartArray("action_entries");
                for (var i = 0; i < Math.Min(_entryCount, EntryLimit); i++) WriteEntry(writer, _entries[i]);
                writer.WriteEndArray(); writer.WriteStartArray("lifetime");
                for (var i = 0; i < Math.Min(_lifeCount, LifetimeLimit); i++)
                {
                    var item = _life[i]; writer.WriteStartObject();
                    writer.WriteString("phase", item.Phase switch { 0 => "queue", 1 => "dispatch", 2 => "open", 3 => "close", _ => "detach" });
                    writer.WriteBoolean("attachment_equal_baseline", item.Attachment);
                    writer.WriteBoolean("epoch_equal_baseline", item.Epoch);
                    writer.WriteBoolean("receiver_equal_current_root", item.Current);
                    writer.WriteBoolean("attached", item.Attached); writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteStartArray("server_calls"); writer.WriteEndArray(); writer.WriteEndObject();
            }
        }
        return stream.ToArray();
    }

    /// <summary>Nullable Boolean encoding preserves unknown rather than converting it to refusal.</summary>
    private static void Flag(Utf8JsonWriter writer, string key, bool? value)
    { if (value is { } flag) writer.WriteBoolean(key, flag); else writer.WriteNull(key); }

    /// <summary>One total method entry, not merely an admitted menu effect.</summary>
    private static void WriteEntry(Utf8JsonWriter writer, Entry e)
    {
        writer.WriteStartObject(); Flag(writer, "on_main_thread", e.Main);
        Flag(writer, "owner_lookup", e.Owner); Flag(writer, "receiver_equal_current_root", e.Current);
        Flag(writer, "attached", e.Attached); Flag(writer, "installing", e.Installing);
        Flag(writer, "frame_present", e.Frame); Flag(writer, "ready_baseline_available", e.Baseline);
        Flag(writer, "attachment_equal_baseline", e.Attachment); Flag(writer, "epoch_equal_baseline", e.Epoch);
        Flag(writer, "queue_result", e.Queued); Flag(writer, "method_return", e.Returned);
        writer.WriteBoolean("caught_exception", e.Exception); writer.WriteEndObject();
    }
}
