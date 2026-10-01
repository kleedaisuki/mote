using Mote.Native.Mac.Canvas;
using Mote.Formats;
using System.Text.Json;

namespace Mote.Native.Mac;

/// <summary>One-shot process-local release diagnostics; ordinary windows never set these answers.</summary>
internal sealed unsafe partial class MacEditorShell
{
    /// <summary>Consumed by the next real Find command, avoiding modal automation in hosted diagnostics.</summary>
    private string? _releaseFindQuery;
    /// <summary>Consumed by the next real Go To Line command.</summary>
    private int? _releaseGoToLine;
    /// <summary>One diagnostic-only discard answer; production windows always display their native alert.</summary>
    private bool? _releaseDiscardAnswer;
    /// <summary>Records actual controller discard confirmation rather than assuming performClose reached it.</summary>
    private bool _releaseDiscardObserved;

    /// <summary>Supplies only the next native prompt result; does not execute a controller command.</summary>
    internal void ProbeReleaseFind(string query) => _releaseFindQuery = query;
    /// <summary>Supplies only the next native prompt result; all navigation remains controller-owned.</summary>
    internal void ProbeReleaseGoToLine(int line) => _releaseGoToLine = line;
    /// <summary>Invokes the actual text responder history selector rather than bypassing the shell.</summary>
    internal void ProbeReleaseHistory(bool redo) => ObjC.Send(_editor, ObjC.Sel(redo ? "redo:" : "undo:"), 0);
    /// <summary>Reads the actual AppKit ordered selection, without inferring an active endpoint.</summary>
    internal ObjC.Range ProbeReleaseSelection => ObjC.SendRange(_editor, ObjC.Sel("selectedRange"));
    /// <summary>Attempts the real window close path with a one-shot Cancel answer.</summary>
    internal void ProbeReleaseCancelClose()
    {
        _releaseDiscardObserved = false;
        _releaseDiscardAnswer = false;
        ObjC.Send(_window, ObjC.Sel("performClose:"), 0);
    }
    /// <summary>True only when the real dirty-close request consumed Cancel and the window remains visible.</summary>
    internal bool ProbeReleaseCloseCancelled => _releaseDiscardObserved && ObjC.Send(_window, ObjC.Sel("isVisible")) != 0;
    /// <summary>Rejects an actual external-open callback through the dirty-document Cancel barrier.</summary>
    internal bool ProbeReleaseCancelExternalOpen(string path)
    {
        _releaseDiscardObserved = false;
        _releaseDiscardAnswer = false;
        var admitted = ProbeReleaseExternalOpen(path);
        return !admitted && _releaseDiscardObserved;
    }
    /// <summary>Replaces a known fixture range through the real native text-input protocol.</summary>
    internal void ProbeReleaseReplace(int start, int length, string value)
    {
        ObjC.Send(_editor, ObjC.Sel("setSelectedRange:"), new ObjC.Range((nuint)start, (nuint)length));
        ObjC.Send(_editor, ObjC.Sel("insertText:replacementRange:"), ObjC.String(value), new ObjC.Range(nuint.MaxValue, 0));
    }
    /// <summary>Stages marked input at the fixture's edited marker without touching system input sources.</summary>
    internal void ProbeReleaseMarked(int caret, string value)
    {
        ObjC.Send(_editor, ObjC.Sel("setSelectedRange:"), new ObjC.Range((nuint)caret, 0));
        ObjC.Send(_editor, ObjC.Sel("setMarkedText:selectedRange:replacementRange:"), ObjC.String(value),
            new ObjC.Range((nuint)value.Length, 0), new ObjC.Range(nuint.MaxValue, 0));
    }

    /// <summary>Caches this window's actual AppKit content view as PNG, without global screen capture or permissions.</summary>
    internal void ProbeReleaseCapture(string path)
    {
        MacReleaseWorkflowProbe.ValidateOutput(path);
        var view = ObjC.Send(_window, ObjC.Sel("contentView"));
        var bounds = MacOnScreenCanvasNative.GetRect(view, ObjC.Sel("bounds"));
        if (bounds.Size.Width <= 0 || bounds.Size.Height <= 0 || bounds.Size.Width > 4096 || bounds.Size.Height > 4096)
            throw new InvalidOperationException("Product capture bounds are invalid.");
        var bitmap = ObjC.Send(view, ObjC.Sel("bitmapImageRepForCachingDisplayInRect:"), bounds);
        if (bitmap == 0) throw new InvalidOperationException("AppKit product bitmap allocation failed.");
        MacOnScreenCanvasNative.Send(view, ObjC.Sel("cacheDisplayInRect:toBitmapImageRep:"), bounds, bitmap);
        var png = ObjC.Send(bitmap, ObjC.Sel("representationUsingType:properties:"), 4,
            ObjC.Send(ObjC.Class("NSDictionary"), ObjC.Sel("dictionary")));
        if (png == 0 || ObjC.Send(png, ObjC.Sel("writeToFile:atomically:"), ObjC.String(path), 1) == 0)
            throw new IOException("AppKit product PNG write failed.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new IOException("AppKit product capture is not PNG.");
    }

    /// <summary>Observes final installed semantic/style/Grid identities without scheduling or changing native state.</summary>
    internal MacReleaseSemanticEvidence? ProbeReleaseSemantics(DocumentKind kind)
    {
        var styleReady = _sourceInstalledStyle is { } style && _sourceInstallation is { } installed &&
            style.Stamp == installed.Stamp && style.Nonce == installed.Nonce &&
            style.Semantics == _sourceSemanticRevision && ReferenceEquals(style.Theme, _theme);
        return !_sourceUnavailable && !IsTextComposing
            ? MacReleaseSemanticModel.Observe(_sourceInstallation, _sourceSemantics, _pendingAnalysis,
                styleReady, !_sourceGeometryUnknown, _csvGrid?.ProbeReleaseRenderedGrid.Frame, kind)
            : null;
    }

    /// <summary>Freezes refusal facts at the existing deadline; no retry, admission, layout or success sidecar is fabricated.</summary>
    internal void ProbeReleaseWriteRefusal(string path, DocumentKind kind,
        NativeAnalysisFailure? lastAnalysisFailure, long currentAnalysisSerial)
    {
        MacReleaseWorkflowProbe.ValidateOutput(path);
        var installation = _sourceInstallation;
        var semantics = _sourceSemantics;
        var analysis = _pendingAnalysis;
        var style = _sourceInstalledStyle;
        var rendered = _csvGrid?.ProbeReleaseRenderedGrid;
        var grid = rendered?.Frame;
        var styleReady = style is { } palette && installation is { } installed &&
            palette.Stamp == installed.Stamp && palette.Nonce == installed.Nonce &&
            palette.Semantics == _sourceSemanticRevision && ReferenceEquals(palette.Theme, _theme);
        var reason = _sourceUnavailable ? "source_unavailable" : IsTextComposing ? "source_composing" :
            MacReleaseSemanticModel.Refusal(installation, semantics, analysis, styleReady, !_sourceGeometryUnknown, grid, kind);
        if (reason == "grid_frame_missing" && rendered is { MissingNativeSlots: > 0 }) reason = "grid_missing_native_slots";
        if (reason == "grid_frame_missing" && rendered is { WindowPending: true }) reason = "grid_native_install_pending";
        if (reason == "grid_frame_missing" && rendered is { ColumnsInstalled: false }) reason = "grid_columns_uninstalled";
        if (reason == "grid_frame_missing" && rendered is { RenderShapeMatches: false }) reason = "grid_render_shape";
        var pending = 0;
        if (grid is not null)
            for (var row = 0; row < grid.Rows.Count; row++)
                for (var column = 0; column < grid.Columns.Count; column++)
                    if (grid.Cell(grid.Coordinate(row, column)).State == GridValueState.Pending) pending++;
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteNumber("schema_version", 1);
        writer.WriteString("outcome", "refused");
        writer.WriteString("reason", reason);
        writer.WriteString("document_kind", kind.ToString());
        Stamp("installed", installation?.Stamp);
        writer.WriteNumber("installation_nonce", installation?.Nonce ?? 0);
        writer.WriteNumber("source_units", installation?.Snapshot.Length ?? 0);
        Stamp("semantic", semantics?.Stamp);
        writer.WriteNumber("semantic_nonce", semantics?.Nonce ?? 0);
        writer.WriteNumber("semantic_sequence", semantics?.PresentationSequence ?? 0);
        writer.WriteString("completeness", semantics?.Completeness.ToString() ?? "missing");
        writer.WriteNumber("coverage_start", semantics?.Coverage.Start ?? 0);
        writer.WriteNumber("coverage_length", semantics?.Coverage.Length ?? 0);
        Stamp("analysis", analysis?.Stamp);
        writer.WriteNumber("analysis_sequence", analysis?.PresentationSequence ?? 0);
        Stamp("style", style?.Stamp);
        writer.WriteNumber("style_nonce", style?.Nonce ?? 0);
        writer.WriteNumber("semantic_revision", _sourceSemanticRevision);
        writer.WriteNumber("style_revision", style?.Semantics ?? 0);
        writer.WriteBoolean("style_ready", styleReady);
        writer.WriteBoolean("geometry_known", !_sourceGeometryUnknown);
        writer.WriteBoolean("experimental_ax_frame_present", _csvGrid?.AccessibilityFrame is not null);
        Stamp("grid", grid?.Id.Document);
        Stamp("grid_ready", grid?.Ready?.Document);
        writer.WriteNumber("grid_ready_sequence", grid?.Ready?.Sequence ?? 0);
        writer.WriteNumber("grid_rows", grid?.Rows.Count ?? 0);
        writer.WriteNumber("grid_columns", grid?.Columns.Count ?? 0);
        writer.WriteNumber("grid_pending_cells", pending);
        writer.WriteNumber("grid_native_slots", rendered?.NativeSlots ?? 0);
        writer.WriteNumber("grid_missing_native_slots", rendered?.MissingNativeSlots ?? 0);
        writer.WriteBoolean("grid_columns_installed", rendered?.ColumnsInstalled ?? false);
        writer.WriteBoolean("grid_render_shape_matches", rendered?.RenderShapeMatches ?? false);
        writer.WriteBoolean("grid_window_pending", rendered?.WindowPending ?? false);
        Stamp("grid_native_ready", rendered?.NativeIdentity?.Document);
        writer.WriteNumber("grid_native_ready_sequence", rendered?.NativeIdentity?.Sequence ?? 0);
        writer.WriteNumber("grid_native_projection_version", rendered?.ProjectionVersion ?? 0);
        writer.WriteBoolean("grid_navigation_pending", rendered?.NavigationPending ?? false);
        MacReleaseAnalysisFailureEvidence.WriteFields(writer, lastAnalysisFailure, installation?.Stamp, currentAnalysisSerial);
        writer.WriteEndObject();
        void Stamp(string prefix, NativeDocumentStamp? stamp)
        {
            writer.WriteBoolean(prefix + "_present", stamp.HasValue);
            writer.WriteNumber(prefix + "_generation", stamp?.Generation ?? 0);
            writer.WriteNumber(prefix + "_version", stamp?.Version ?? 0);
        }
    }
}
