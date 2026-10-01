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
    /// <summary>Closed refusal code from the same guards used to certify final semantic evidence.</summary>
    internal static string Refusal(NativeSourceInstallation? installation, NativeSourceSemantics? semantics,
        NativeAnalysisView? analysis, bool styleReady, bool geometryKnown, GridAccessibilityFrame? grid, DocumentKind kind)
    {
        if (installation is null) return "source_installation_missing";
        if (semantics is null) return "source_semantics_missing";
        if (analysis is null) return "analysis_missing";
        if (semantics.Stamp != installation.Stamp) return "source_semantic_identity";
        if (semantics.Nonce != installation.Nonce) return "source_semantic_nonce";
        if (analysis.Stamp != semantics.Stamp) return "analysis_identity";
        if (analysis.PresentationSequence != semantics.PresentationSequence) return "analysis_sequence";
        if (semantics.Completeness != AnalysisCompleteness.Complete) return "source_semantics_incomplete";
        if (semantics.Coverage != new TextSpan(0, installation.Snapshot.Length)) return "source_semantics_coverage";
        if (!styleReady) return "source_style_pending";
        if (!geometryKnown) return "source_geometry_unknown";
        if (kind != DocumentKind.Csv) return "none";
        if (grid is null) return "grid_frame_missing";
        if (grid.Id.Document != installation.Stamp) return "grid_document_identity";
        if (grid.Ready != analysis.Identity) return "grid_ready_identity";
        if (grid.Navigation is { Pending: true }) return "grid_navigation_pending";
        if (grid.Projection?.Version != installation.Stamp.Version) return "grid_projection_identity";
        if (grid.Rows.Count == 0 || grid.Columns.Count == 0) return "grid_empty_window";
        if ((long)grid.Rows.Count * grid.Columns.Count > GridRenderProjection.MaxCells) return "grid_capacity";
        for (var row = 0; row < grid.Rows.Count; row++)
            for (var column = 0; column < grid.Columns.Count; column++)
                if (grid.Cell(grid.Coordinate(row, column)).State == GridValueState.Pending) return "grid_pending_cells";
        return "none";
    }
    /// <summary>Requires exact current complete source coverage, installed style and actual ready visible CSV cells.</summary>
    internal static MacReleaseSemanticEvidence? Observe(NativeSourceInstallation? installation,
        NativeSourceSemantics? semantics, NativeAnalysisView? analysis, bool styleReady, bool geometryKnown,
        GridAccessibilityFrame? grid, DocumentKind kind)
    {
        if (Refusal(installation, semantics, analysis, styleReady, geometryKnown, grid, kind) != "none") return null;
        var rows = 0; var columns = 0; var cells = 0; var pending = 0;
        if (kind == DocumentKind.Csv)
        {
            rows = grid!.Rows.Count; columns = grid.Columns.Count;
            cells = checked(rows * columns);
            if (cells > GridRenderProjection.MaxCells) return null;
            for (var row = 0; row < rows; row++)
                for (var column = 0; column < columns; column++)
                    if (grid.Cell(grid.Coordinate(row, column)).State == GridValueState.Pending) pending++;
            if (pending != 0) return null;
        }
        return new(installation!.Stamp, installation.Nonce, semantics!.PresentationSequence, kind,
            installation.Snapshot.Length, semantics.Tokens.Count, semantics.Diagnostics.Count, rows, columns, cells, pending);
    }
}
