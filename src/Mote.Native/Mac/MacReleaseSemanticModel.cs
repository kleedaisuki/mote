using System.Text.Json;
using Mote.Formats;

namespace Mote.Native.Mac;

/// <summary>Content-free final product evidence. Counts describe actual admitted semantic and visible Grid facts.</summary>
internal sealed record MacReleaseSemanticEvidence(NativeDocumentStamp Stamp, long Nonce, long Sequence,
    DocumentKind Kind, int SourceUnits, int TokenCount, int DiagnosticCount, int GridRows, int GridColumns,
    int GridCells, int GridPendingCells)
{
    /// <summary>Writes a fixed bounded schema without reflection serialization, source content or paths.</summary>
    internal void Write(string path)
    {
        MacReleaseWorkflowProbe.ValidateOutput(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteNumber("schema_version", 1);
        writer.WriteNumber("generation", Stamp.Generation);
        writer.WriteNumber("version", Stamp.Version);
        writer.WriteNumber("installation_nonce", Nonce);
        writer.WriteNumber("presentation_sequence", Sequence);
        writer.WriteString("document_kind", Kind.ToString());
        writer.WriteString("completeness", "Complete");
        writer.WriteNumber("coverage_start", 0);
        writer.WriteNumber("coverage_length", SourceUnits);
        writer.WriteNumber("source_units", SourceUnits);
        writer.WriteNumber("token_count", TokenCount);
        writer.WriteNumber("diagnostic_count", DiagnosticCount);
        writer.WriteBoolean("style_ready", true);
        writer.WriteBoolean("geometry_known", true);
        writer.WriteBoolean("grid_required", Kind == DocumentKind.Csv);
        writer.WriteBoolean("grid_ready", Kind == DocumentKind.Csv);
        writer.WriteNumber("grid_rows", GridRows);
        writer.WriteNumber("grid_columns", GridColumns);
        writer.WriteNumber("grid_cells", GridCells);
        writer.WriteNumber("grid_pending_cells", GridPendingCells);
        writer.WriteEndObject();
    }
}

/// <summary>Portable qualification guards; success certifies current facts, never inferred completion from task timing.</summary>
internal static class MacReleaseSemanticModel
{
    /// <summary>Requires exact current complete source coverage, installed style and actual ready visible CSV cells.</summary>
    internal static MacReleaseSemanticEvidence? Observe(NativeSourceInstallation? installation,
        NativeSourceSemantics? semantics, NativeAnalysisView? analysis, bool styleReady, bool geometryKnown,
        GridAccessibilityFrame? grid, DocumentKind kind)
    {
        if (installation is null || semantics is null || analysis is null || !styleReady || !geometryKnown ||
            semantics.Stamp != installation.Stamp || semantics.Nonce != installation.Nonce ||
            analysis.Stamp != semantics.Stamp || analysis.PresentationSequence != semantics.PresentationSequence ||
            semantics.Completeness != AnalysisCompleteness.Complete || semantics.Coverage != new TextSpan(0, installation.Snapshot.Length))
            return null;
        var rows = 0; var columns = 0; var cells = 0; var pending = 0;
        if (kind == DocumentKind.Csv)
        {
            if (grid is null || grid.Id.Document != installation.Stamp || grid.Ready != analysis.Identity ||
                grid.Navigation is { Pending: true } || grid.Projection?.Version != installation.Stamp.Version ||
                grid.Rows.Count == 0 || grid.Columns.Count == 0) return null;
            rows = grid.Rows.Count; columns = grid.Columns.Count;
            cells = checked(rows * columns);
            if (cells > GridRenderProjection.MaxCells) return null;
            for (var row = 0; row < rows; row++)
                for (var column = 0; column < columns; column++)
                    if (grid.Cell(grid.Coordinate(row, column)).State == GridValueState.Pending) pending++;
            if (pending != 0) return null;
        }
        return new(installation.Stamp, installation.Nonce, semantics.PresentationSequence, kind,
            installation.Snapshot.Length, semantics.Tokens.Count, semantics.Diagnostics.Count, rows, columns, cells, pending);
    }
}
