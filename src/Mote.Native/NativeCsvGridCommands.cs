using Mote.Engine;
using Mote.Formats;

namespace Mote.Native;

/// <summary>A prepared, non-mutating command; failure carries neither clipboard data nor an edit.</summary>
internal readonly record struct NativeCsvGridCommandResult(bool Success, string? Payload, TextChange? Change, string? Error);

/// <summary>Admits native intent identity and delegates all CSV semantics to Formats.</summary>
internal static class NativeCsvGridCommands
{
    /// <summary>Formats owns the output resource contract, measured in UTF-16 units.</summary>
    internal const int MaxPayloadLength = CsvGridCommands.MaxPayloadLength;

    /// <summary>Maps clipboard intent without reading, decoding, or serializing field syntax.</summary>
    internal static NativeCsvGridCommandResult PrepareCopy(TextSnapshot snapshot, GridRenderProjection grid,
        NativeGridIntent intent, CancellationToken cancellationToken = default)
    {
        if (intent.Identity.Document.Version != snapshot.Version) return Stale();
        CsvGridCopyKind? kind = intent.Kind switch
        {
            NativeGridIntentKind.CopyValue => CsvGridCopyKind.Value,
            NativeGridIntentKind.CopyCsv => CsvGridCopyKind.Csv,
            NativeGridIntentKind.CopyTsv => CsvGridCopyKind.Tsv,
            NativeGridIntentKind.CopyRows => CsvGridCopyKind.Rows,
            NativeGridIntentKind.CopySource => CsvGridCopyKind.Source,
            NativeGridIntentKind.CopyCsvPadded => CsvGridCopyKind.CsvPadded,
            _ => null
        };
        return kind is { } admitted
            ? Convert(CsvGridCommands.PrepareCopy(snapshot, grid, Selection(intent), admitted, cancellationToken))
            : new(false, null, null, "This is not a Copy command.");
    }

    /// <summary>Admits a replacement intent; Formats prepares the exact Engine edit.</summary>
    internal static NativeCsvGridCommandResult PrepareReplace(TextSnapshot snapshot, GridRenderProjection grid,
        NativeGridIntent intent, string replacement, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (intent.Identity.Document.Version != snapshot.Version) return Stale();
        return intent.Kind == NativeGridIntentKind.Replace
            ? Convert(CsvGridCommands.PrepareReplace(snapshot, grid, Selection(intent), replacement, cancellationToken))
            : new(false, null, null, "Replacement requires one selected cell.");
    }

    /// <summary>Identity stays in Native; only logical delivered coordinates cross the policy boundary.</summary>
    private static CsvGridSelection Selection(NativeGridIntent intent) =>
        new(intent.Row, intent.Column, intent.EndRow, intent.EndColumn, intent.WholeRows);

    /// <summary>Preserves the existing controller result contract.</summary>
    private static NativeCsvGridCommandResult Convert(CsvGridCommandResult result) =>
        new(result.Success, result.Payload, result.Change, result.Error);

    /// <summary>Stale native intents cannot enter source-backed policy preparation.</summary>
    private static NativeCsvGridCommandResult Stale() =>
        new(false, null, null, "The table selection belongs to an obsolete document version.");
}
