using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Mote.Engine;
using Mote.Formats;
using Mote.Telemetry;
using Mote.Themes;
using System.Text;

namespace Mote.Desktop;

/// <summary>
/// Single-file window. The engine document is authoritative; AvaloniaEdit is an
/// editable, disposable viewport projection, never a second persistence owner.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int LargeDocumentThreshold = 8 * 1024 * 1024;
    private const int PageSize = 256 * 1024;
    private const int AutoMarkdownAnalysisBudget = 1024 * 1024;
    private const int AutoStructuredAnalysisBudget = 4 * 1024 * 1024;
    private const int FullMarkdownAnalysisBudget = 2 * 1024 * 1024;
    private const int FullStructuredAnalysisBudget = 8 * 1024 * 1024;
    private Document _document = new();
    private IDocumentPolicy _policy = DocumentPolicies.ForKind(DocumentKind.PlainText);
    private FormatAnalysis? _analysis;
    private readonly SemanticColorizer _colorizer = new(Program.Theme);
    private int _analysisStart;
    private bool _analysisIsFull;
    private CancellationTokenSource? _analysisCancellation;
    private int _pageStart;
    private int _pageLength;
    private readonly Stack<int> _pageHistory = new();
    private bool _largeMode;
    private bool _loadingView;
    private bool _closingAfterConfirmation;
    private long _analysisSerial;
    private int _openRequestId;
    private TelemetryMark _pendingEditMark;
    private readonly List<Diagnostic> _shownDiagnostics = [];
    private readonly List<SemanticNode> _shownStructure = [];

    private bool HasUnsavedChanges => _document.IsModified &&
        (_document.FilePath is not null || _document.Snapshot.Version != 0);

    /// <summary>Creates a ready-to-edit untitled document and wires input once.</summary>
    public MainWindow()
    {
        InitializeComponent();
        var palette = Program.Theme.Palette;
        Editor.FontFamily = FontFamily.Parse(Program.Theme.Typography.EditorFontFamilies);
        Editor.FontSize = Program.Theme.Typography.EditorFontSize;
        Editor.LineNumbersForeground = ThemeBrush(palette.GutterForeground);
        Editor.TextArea.SelectionBrush = ThemeBrush(palette.SelectionBackground);
        Editor.TextArea.SelectionForeground = ThemeBrush(palette.SelectionForeground);
        Editor.TextArea.Caret.CaretBrush = ThemeBrush(palette.Cursor);
        Editor.TextArea.TextView.CurrentLineBackground = ThemeBrush(palette.ActiveLineBackground);
        Editor.Document.Changed += EditorDocumentChanged;
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdatePosition();
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        AddHandler(KeyDownEvent, HandleWindowKeyDown, RoutingStrategies.Tunnel);
        Closing += WindowClosing;
        Opened += (_, _) => Program.CompleteStartup();
        ShowDocument(resetPage: true);
        ScheduleAnalysis();
    }

    /// <summary>Schedules a command-line file for asynchronous loading after window creation.</summary>
    public void OpenAtStartup(string path) =>
        Dispatcher.UIThread.Post(() => _ = OpenPathAsync(path));

    /// <summary>Handles OS file activation after checking the current unsaved file.</summary>
    public void OpenFromActivation(string path) =>
        Dispatcher.UIThread.Post(async () =>
        {
            if (await ConfirmDiscardAsync()) await OpenPathAsync(path);
        });

    private async void NewClicked(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync()) return;
        ReplaceDocument(new Document(), null);
    }

    private async void OpenClicked(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync()) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open one text file",
            AllowMultiple = false
        });
        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (path is null)
        {
            await ShowErrorAsync("This editor currently needs a local file path.");
            return;
        }
        await OpenPathAsync(path);
    }

    private async Task OpenPathAsync(string path)
    {
        var requestId = ++_openRequestId;
        var previous = _document;
        var previousVersion = previous.Snapshot.Version;
        using var openSpan = MoteTelemetry.Start(TelemetryOperation.OpenToEditable);
        try
        {
            StatusText.Text = "Opening…";
            var opened = await Document.OpenAsync(path);
            if (requestId != _openRequestId)
            {
                opened.Dispose();
                return;
            }
            if (!ReferenceEquals(previous, _document) ||
                previousVersion != _document.Snapshot.Version)
            {
                if (!await ConfirmDiscardAsync() || requestId != _openRequestId)
                {
                    opened.Dispose();
                    openSpan?.SetStatus(TelemetryStatus.Cancelled);
                    return;
                }
            }
            ReplaceDocument(opened, path);
            StatusText.Text = "Opened";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or DecoderFallbackException)
        {
            openSpan?.SetStatus(TelemetryStatus.Failure);
            StatusText.Text = "Open failed";
            await ShowErrorAsync(ex.Message);
        }
    }

    private void ReplaceDocument(Document replacement, string? path)
    {
        ++_openRequestId;
        CancelAnalysis();
        _document.Dispose();
        _document = replacement;
        _pageHistory.Clear();
        _policy = path is null ? DocumentPolicies.ForKind(DocumentKind.PlainText) : DocumentPolicies.ForPath(path);
        _analysis = null;
        ShowDocument(resetPage: true);
        ScheduleAnalysis();
    }

    private async void SaveClicked(object? sender, RoutedEventArgs e) => await SaveAsync(forcePicker: false);

    private async void SaveAsClicked(object? sender, RoutedEventArgs e) => await SaveAsync(forcePicker: true);

    private async Task<bool> SaveAsync(bool forcePicker)
    {
        var document = _document;
        string? path = forcePicker ? null : document.FilePath;
        if (path is null)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save text file",
                SuggestedFileName = document.FilePath is null ? "Untitled.md" : Path.GetFileName(document.FilePath),
                DefaultExtension = "md",
                ShowOverwritePrompt = true
            });
            if (file is null) return false;
            if (!ReferenceEquals(document, _document)) return false;
            path = file.TryGetLocalPath();
            if (path is null)
            {
                await ShowErrorAsync("This editor currently needs a local file path.");
                return false;
            }
        }

        using var saveSpan = MoteTelemetry.Start(TelemetryOperation.Save,
            CurrentDimensions());
        try
        {
            StatusText.Text = "Saving…";
            var differentPath = document.FilePath is null || !string.Equals(
                Path.GetFullPath(path), document.FilePath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            if (differentPath && File.Exists(path))
            {
                var token = await FileOverwriteToken.CaptureAsync(path);
                if (!ReferenceEquals(document, _document)) return false;
                var choice = await ShowChoiceAsync("Replace existing file?",
                    $"Replace {Path.GetFileName(path)}? Changes to that file since this confirmation will cancel the save.",
                    "Replace", "Cancel");
                if (choice != 0 || !ReferenceEquals(document, _document))
                {
                    saveSpan?.SetStatus(TelemetryStatus.Cancelled);
                    return false;
                }
                await document.SaveOverAsync(token);
            }
            else
            {
                await document.SaveAsync(path);
            }
            if (!ReferenceEquals(document, _document)) return false;
            _policy = DocumentPolicies.ForPath(path);
            UpdateChrome();
            ScheduleAnalysis();
            StatusText.Text = document.IsModified
                ? "Saved a snapshot; newer edits remain unsaved"
                : "Saved";
            MoteTelemetry.Record(TelemetryEvent.SaveCompleted, dimensions: CurrentDimensions());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or EncoderFallbackException)
        {
            saveSpan?.SetStatus(TelemetryStatus.Failure);
            StatusText.Text = "Save failed";
            await ShowErrorAsync(ex.Message);
            return false;
        }
        catch (ObjectDisposedException)
        {
            saveSpan?.SetStatus(TelemetryStatus.Cancelled);
            return false;
        }
    }

    private void UndoClicked(object? sender, RoutedEventArgs e) => Undo();
    private void RedoClicked(object? sender, RoutedEventArgs e) => Redo();

    private void Undo()
    {
        if (!_document.Undo()) return;
        ShowDocument(resetPage: false);
        ScheduleAnalysis();
    }

    private void Redo()
    {
        if (!_document.Redo()) return;
        ShowDocument(resetPage: false);
        ScheduleAnalysis();
    }

    private void FormatClicked(object? sender, RoutedEventArgs e)
    {
        if (_policy.Kind == DocumentKind.PlainText)
        {
            StatusText.Text = "Formatting is unavailable for this format.";
            return;
        }
        if (_largeMode)
        {
            StatusText.Text = "Format disabled for paged documents; inspect the whole file first.";
            return;
        }
        var before = _document.Snapshot.GetText();
        var after = _policy.Format(before);
        if (after == before)
        {
            StatusText.Text = _analysis?.Diagnostics.Any(
                diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) == true
                ? "Fix diagnostics before formatting."
                : "Already formatted";
            return;
        }
        _document.Apply(new TextChange(0, before.Length, after));
        ShowDocument(resetPage: false);
        ScheduleAnalysis();
        StatusText.Text = "Formatted";
    }

    private void InspectClicked(object? sender, RoutedEventArgs e)
    {
        var budget = _policy.Kind == DocumentKind.Markdown
            ? FullMarkdownAnalysisBudget : FullStructuredAnalysisBudget;
        if (_document.Snapshot.Length > budget)
        {
            StatusText.Text = $"Full inspection capped at {budget / (1024 * 1024)} Mi characters for this format; partial diagnostics remain available.";
            return;
        }
        ScheduleAnalysis(fullDocument: true);
    }

    private async void CopyHtmlClicked(object? sender, RoutedEventArgs e)
    {
        var analysis = _analysis;
        if (analysis is null)
        {
            StatusText.Text = "Wait for analysis before copying HTML.";
            return;
        }
        try
        {
            var html = await Task.Run(() => _policy.RenderHtml(analysis));
            if (Clipboard is null) return;
            await Clipboard.SetTextAsync(html);
            StatusText.Text = !_analysisIsFull
                ? "Copied partial HTML (not full file)"
                : "Copied rendered HTML";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"HTML export failed: {ex.Message}";
        }
    }

    private void PreviousChunkClicked(object? sender, RoutedEventArgs e)
    {
        if (!_largeMode || _pageStart == 0) return;
        if (_pageHistory.TryPop(out var prior))
            _pageStart = prior;
        else
        {
            var target = Math.Max(0, _pageStart - PageSize);
            var lineStart = _document.Snapshot.GetLineStartOffset(
                _document.Snapshot.GetLineIndexFromOffset(target));
            _pageStart = target - lineStart > PageSize ? target : lineStart;
        }
        ShowDocument(resetPage: false, caretGlobal: _pageStart);
        ScheduleAnalysis();
    }

    private void NextChunkClicked(object? sender, RoutedEventArgs e)
    {
        if (!_largeMode || _pageStart + _pageLength >= _document.Snapshot.Length) return;
        _pageHistory.Push(_pageStart);
        _pageStart += _pageLength;
        ShowDocument(resetPage: false, caretGlobal: _pageStart);
        ScheduleAnalysis();
    }

    private void EditorDocumentChanged(object? sender, DocumentChangeEventArgs e)
    {
        if (_loadingView) return;
        var inserted = e.InsertedText.Text;
        _pendingEditMark = MoteTelemetry.Mark();
        _document.Apply(new TextChange(_pageStart + e.Offset, e.RemovalLength, inserted));
        MoteTelemetry.Record(TelemetryEvent.EditCommitted, dimensions: CurrentDimensions());
        _pageLength += inserted.Length - e.RemovalLength;
        _analysis = null;
        DiagnosticsList.ItemsSource = Array.Empty<string>();
        StructureList.ItemsSource = Array.Empty<string>();
        RenderedPanel.Children.Clear();
        ProblemCount.Text = "Checking…";
        _colorizer.SetTokens([], 0, 0);
        Editor.TextArea.TextView.Redraw();
        UpdateChrome();
        ScheduleAnalysis();
    }

    /// <summary>
    /// Replaces the view as a projection. The guard prevents programmatic text
    /// replacement from being replayed as a second engine edit.
    /// </summary>
    private void ShowDocument(bool resetPage, int? caretGlobal = null)
    {
        var desiredCaret = resetPage ? 0 : caretGlobal ?? (_pageStart + Editor.CaretOffset);
        _analysis = null;
        _colorizer.SetTokens([], 0, 0);
        DiagnosticsList.ItemsSource = Array.Empty<string>();
        StructureList.ItemsSource = Array.Empty<string>();
        RenderedPanel.Children.Clear();
        ProblemCount.Text = "Checking…";
        var snapshot = _document.Snapshot;
        _largeMode = snapshot.Length > LargeDocumentThreshold;
        if (resetPage || !_largeMode) _pageStart = 0;
        if (resetPage) _pageHistory.Clear();
        _pageStart = Math.Clamp(_pageStart, 0, snapshot.Length);
        _pageLength = _largeMode ? Math.Min(PageSize, snapshot.Length - _pageStart) : snapshot.Length;
        if (_largeMode && _pageStart + _pageLength < snapshot.Length)
        {
            var lastLine = snapshot.GetLineIndexFromOffset(_pageStart + _pageLength);
            var boundary = snapshot.GetLineStartOffset(lastLine);
            if (boundary > _pageStart) _pageLength = boundary - _pageStart;
        }

        _loadingView = true;
        try
        {
            Editor.Document.Text = snapshot.GetText(_pageStart, _pageLength);
            Editor.CaretOffset = Math.Clamp(desiredCaret - _pageStart, 0, _pageLength);
            Editor.Document.UndoStack.ClearAll();
        }
        finally
        {
            _loadingView = false;
        }
        PreviousChunkButton.IsVisible = _largeMode;
        NextChunkButton.IsVisible = _largeMode;
        PreviousChunkButton.IsEnabled = _pageStart > 0;
        NextChunkButton.IsEnabled = _pageStart + _pageLength < snapshot.Length;
        UpdateChrome();
    }

    private void UpdateChrome()
    {
        var path = _document.FilePath;
        FileTitle.Text = path is null ? "Untitled" : Path.GetFileName(path);
        FilePathText.Text = path ?? "A single file. No workspace.";
        DirtyMark.Text = HasUnsavedChanges ? "●" : "";
        FormatLabel.Text = _policy.DisplayName;
        var length = _document.Snapshot.Length;
        SizeText.Text = _largeMode
            ? $"{length:N0} chars · page {_pageStart:N0}–{_pageStart + _pageLength:N0}"
            : $"{length:N0} chars";
        Title = $"{(HasUnsavedChanges ? "● " : "")}{FileTitle.Text} — mote";
        var traceHealth = MoteTelemetry.Health;
        var traceText = traceHealth.SinkFaulted
            ? "Trace unavailable"
            : traceHealth.Enabled
                ? traceHealth.DroppedRecords > 0
                    ? $"Trace on · {traceHealth.DroppedRecords} dropped"
                    : "Trace on"
                : "";
        var configurationWarning = Program.Configuration.Diagnostics.Count > 0
            ? $"Config: {Program.Configuration.Diagnostics.Count} warning(s)"
            : "";
        TraceStatus.Text = string.Join(" · ", new[] { configurationWarning, traceText }
            .Where(value => value.Length > 0));
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        var globalOffset = Math.Min(_document.Snapshot.Length, _pageStart + Editor.CaretOffset);
        var line = _document.Snapshot.GetLineIndexFromOffset(globalOffset);
        var start = _document.Snapshot.GetLineStartOffset(line);
        PositionText.Text = $"Ln {line + 1:N0}, Col {globalOffset - start + 1:N0}";
    }

    private void ScheduleAnalysis(bool fullDocument = false)
    {
        CancelAnalysis();
        var editMark = _pendingEditMark;
        _pendingEditMark = default;
        var cts = new CancellationTokenSource();
        _analysisCancellation = cts;
        var serial = ++_analysisSerial;
        var snapshot = _document.Snapshot;
        var policy = _policy;
        var autoBudget = policy.Kind == DocumentKind.Markdown
            ? AutoMarkdownAnalysisBudget : AutoStructuredAnalysisBudget;
        var whole = fullDocument || (!_largeMode && snapshot.Length <= autoBudget);
        var start = whole ? 0 : _largeMode ? _pageStart : 0;
        var length = whole ? snapshot.Length : Math.Min(PageSize, _largeMode ? _pageLength : snapshot.Length);
        StatusText.Text = whole ? "Analyzing…" : "Analyzing partial text…";

        _ = AnalyzeLaterAsync(snapshot, policy, start, length, serial, cts.Token,
            whole, editMark);
    }

    private async Task AnalyzeLaterAsync(TextSnapshot snapshot, IDocumentPolicy policy,
        int start, int length, long serial, CancellationToken token, bool fullDocument,
        TelemetryMark editMark)
    {
        try
        {
            await Task.Delay(180, token);
            var analysis = await Task.Run(() =>
            {
                using var parseSpan = MoteTelemetry.StartChild(TelemetryOperation.AnalysisParse,
                    editMark,
                    new TelemetryDimensions(Format: CurrentTelemetryFormat(policy.Kind),
                        DocumentBytes: length * sizeof(char), Version: snapshot.Version));
                var text = snapshot.GetText(start, length);
                return policy.Analyze(text, token);
            }, token);
            token.ThrowIfCancellationRequested();
            if (serial != _analysisSerial || snapshot.Version != _document.Snapshot.Version)
            {
                MoteTelemetry.RecordElapsed(TelemetryOperation.EditToPresentation, editMark,
                    new TelemetryDimensions(Version: snapshot.Version), TelemetryStatus.Cancelled);
                MoteTelemetry.Record(TelemetryEvent.AnalysisDiscarded);
                return;
            }
            _analysis = analysis;
            _analysisStart = start;
            _analysisIsFull = fullDocument;
            ShowAnalysis(analysis, start, fullDocument, editMark);
            MoteTelemetry.RecordElapsed(TelemetryOperation.EditToPresentation, editMark,
                CurrentDimensions());
        }
        catch (OperationCanceledException)
        {
            MoteTelemetry.RecordElapsed(TelemetryOperation.EditToPresentation, editMark,
                new TelemetryDimensions(Version: snapshot.Version), TelemetryStatus.Cancelled);
        }
        catch (Exception ex)
        {
            MoteTelemetry.RecordElapsed(TelemetryOperation.EditToPresentation, editMark,
                new TelemetryDimensions(Version: snapshot.Version), TelemetryStatus.Failure);
            if (serial == _analysisSerial) StatusText.Text = $"Analysis failed: {ex.Message}";
        }
    }

    private void ShowAnalysis(FormatAnalysis analysis, int sourceStart, bool fullDocument,
        TelemetryMark editMark)
    {
        using var presentationSpan = MoteTelemetry.StartChild(
            TelemetryOperation.AnalysisToPresentation, editMark, CurrentDimensions());
        _shownDiagnostics.Clear();
        var labels = new List<string>();
        foreach (var diagnostic in analysis.Diagnostics.Take(2500))
        {
            _shownDiagnostics.Add(diagnostic);
            var offset = sourceStart + diagnostic.Span.Start;
            var line = _document.Snapshot.GetLineIndexFromOffset(
                Math.Clamp(offset, 0, _document.Snapshot.Length)) + 1;
            labels.Add($"{diagnostic.Severity} · {diagnostic.Code} · Ln {line}: {diagnostic.Message}");
        }
        DiagnosticsList.ItemsSource = labels;
        ProblemCount.Text = !fullDocument
            ? $"{analysis.Diagnostics.Count} in sample · full file unchecked"
            : $"{analysis.Diagnostics.Count} problem{(analysis.Diagnostics.Count == 1 ? "" : "s")}";

        _shownStructure.Clear();
        var structure = new List<string>();
        AddStructure(analysis.Root, 0, structure);
        StructureList.ItemsSource = structure;
        _colorizer.SetTokens(analysis.Tokens, _pageStart - sourceStart, _pageLength);
        Editor.TextArea.TextView.Redraw();
        ShowRendered(analysis);
        MoteTelemetry.Record(TelemetryEvent.AnalysisPublished, dimensions: CurrentDimensions());
        StatusText.Text = !fullDocument
            ? "Partial analysis; full-file diagnostics not asserted"
            : labels.Count == 0 ? "No diagnostics" : $"{labels.Count} diagnostics";
    }

    private void AddStructure(SemanticNode node, int depth, List<string> labels)
    {
        if (depth > 0)
        {
            _shownStructure.Add(node);
            var name = node.Name ?? node.Value ?? node.Kind;
            labels.Add($"{new string(' ', Math.Min(depth - 1, 8) * 2)}{node.Kind}: {name}");
        }
        if (depth >= 8 || labels.Count >= 2500) return;
        foreach (var child in node.Children) AddStructure(child, depth + 1, labels);
    }

    /// <summary>
    /// Projects semantic nodes into native controls, avoiding an embedded browser or
    /// a runtime HTML interpreter. Markdown block types get typography; structured
    /// formats get an indented semantic tree. The policy's HTML remains available
    /// independently for safe export.
    /// </summary>
    private void ShowRendered(FormatAnalysis analysis)
    {
        RenderedPanel.Children.Clear();
        if (analysis.Root.Children.Count == 0)
        {
            RenderedPanel.Children.Add(new TextBlock { Text = "Nothing to render.",
                Foreground = ThemeBrush(Program.Theme.Palette.MutedForeground) });
            return;
        }
        var count = 0;
        foreach (var node in analysis.Root.Children)
        {
            AddRenderedNode(node, analysis.SourceText, 0, ref count);
            if (count >= 2500) break;
        }
        if (count >= 2500)
            RenderedPanel.Children.Add(new TextBlock { Text = "Preview truncated to 2,500 nodes.",
                Foreground = ThemeBrush(Program.Theme.Palette.MutedForeground) });
    }

    private void AddRenderedNode(SemanticNode node, string source, int depth, ref int count)
    {
        if (depth >= 12 || count >= 2500) return;
        count++;
        switch (node.Kind)
        {
            case "heading":
                var level = int.TryParse(node.Name, out var headingLevel) ? headingLevel : 1;
                RenderedPanel.Children.Add(new TextBlock
                {
                    Text = MarkdownText(node, source),
                    FontSize = Math.Max(15, 26 - (level - 1) * 2),
                    FontWeight = FontWeight.SemiBold,
                    Foreground = ThemeBrush(Program.Theme.Palette.PreviewForeground),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 9, 0, 2)
                });
                return;
            case "paragraph":
                RenderedPanel.Children.Add(new TextBlock
                {
                    Text = MarkdownText(node, source),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    Foreground = ThemeBrush(Program.Theme.Palette.PreviewForeground)
                });
                return;
            case "fenced-code":
            case "code-block":
                RenderedPanel.Children.Add(new Border
                {
                    Background = ThemeBrush(Program.Theme.Palette.EditorBackground),
                    Padding = new Thickness(10),
                    CornerRadius = new CornerRadius(5),
                    Child = new TextBlock
                    {
                        Text = node.Value ?? (node.Kind == "fenced-code"
                            ? StripFence(RawSource(node, source)) : RawSource(node, source)),
                        FontFamily = FontFamily.Parse("Cascadia Code,Consolas,Menlo,monospace"),
                        Foreground = ThemeBrush(Program.Theme.Palette.PreviewForeground),
                        TextWrapping = TextWrapping.Wrap
                    }
                });
                return;
            case "list":
                for (var i = 0; i < node.Children.Count && count < 2500; i++)
                {
                    count++;
                    var item = node.Children[i];
                    RenderedPanel.Children.Add(new TextBlock
                    {
                        Text = $"{(node.Name == "ordered" ? $"{i + 1}." : "•")} {MarkdownText(item, source)}",
                        Margin = new Thickness(14 + depth * 12, 0, 0, 0),
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = ThemeBrush(Program.Theme.Palette.PreviewForeground)
                    });
                }
                return;
            case "quote":
                RenderedPanel.Children.Add(new Border
                {
                    BorderBrush = ThemeBrush(Program.Theme.Palette.Accent),
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(10, 2),
                    Child = new TextBlock
                    {
                        Text = MarkdownText(node, source),
                        Foreground = ThemeBrush(Program.Theme.Palette.MutedForeground),
                        TextWrapping = TextWrapping.Wrap
                    }
                });
                return;
            case "thematic-break":
                RenderedPanel.Children.Add(new Border
                {
                    Height = 1,
                    Background = ThemeBrush(Program.Theme.Palette.Border),
                    Margin = new Thickness(0, 8)
                });
                return;
        }

        var label = node.Name is null ? node.Kind : $"{node.Name}  ·  {node.Kind}";
        var value = node.Value ?? RawSource(node, source);
        RenderedPanel.Children.Add(new TextBlock
        {
            Text = value is null ? label : $"{label}: {value}",
            Margin = new Thickness(depth * 14, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush(depth == 0
                ? Program.Theme.Palette.Accent : Program.Theme.Palette.PreviewForeground)
        });
        foreach (var child in node.Children) AddRenderedNode(child, source, depth + 1, ref count);
    }

    private static string MarkdownText(SemanticNode node, string source)
    {
        if (node.Kind == "heading") return node.Value ?? StripHeading(RawSource(node, source));
        if (node.Kind == "paragraph") return node.Value ?? RawSource(node, source);
        if (node.Kind is "text" or "code") return node.Value ?? RawSource(node, source);
        if (node.Kind == "line-break") return Environment.NewLine;
        if (node.Kind == "image") return "[image]";
        if (node.Kind == "quote")
            return string.Join(Environment.NewLine, RawSource(node, source)
                .Split('\n').Select(line => line.TrimStart().TrimStart('>').TrimStart()));
        if (node.Children.Count == 0) return node.Value ?? RawSource(node, source);
        return string.Concat(node.Children.Select(child => MarkdownText(child, source)));
    }

    private static string RawSource(SemanticNode node, string source)
    {
        if (node.Span.Start < 0 || node.Span.Length < 0 ||
            node.Span.Start > source.Length ||
            node.Span.Length > source.Length - node.Span.Start) return "";
        return source.Substring(node.Span.Start, node.Span.Length);
    }

    private static string StripHeading(string raw)
    {
        var text = raw.TrimStart();
        var markerLength = 0;
        while (markerLength < text.Length && markerLength < 6 && text[markerLength] == '#')
            markerLength++;
        if (markerLength == 0) return raw;
        text = text[markerLength..].Trim();
        var end = text.Length;
        while (end > 0 && text[end - 1] == '#') end--;
        return end < text.Length && (end == 0 || char.IsWhiteSpace(text[end - 1]))
            ? text[..end].TrimEnd() : text;
    }

    private static string StripFence(string source)
    {
        var firstNewline = source.IndexOf('\n');
        if (firstNewline < 0) return source;
        var body = source[(firstNewline + 1)..];
        var lastNewline = body.LastIndexOf('\n');
        if (lastNewline >= 0)
        {
            var tail = body[(lastNewline + 1)..].Trim();
            if (tail.StartsWith("```", StringComparison.Ordinal) ||
                tail.StartsWith("~~~", StringComparison.Ordinal))
                body = body[..lastNewline];
        }
        return body.TrimEnd('\r', '\n');
    }

    private static IBrush ThemeBrush(ThemeColor color) =>
        new SolidColorBrush(Color.Parse(color.ToHex()));

    private void DiagnosticSelected(object? sender, SelectionChangedEventArgs e)
    {
        var index = DiagnosticsList.SelectedIndex;
        if (index < 0 || index >= _shownDiagnostics.Count || _analysis is null) return;
        var offset = _shownDiagnostics[index].Span.Start;
        GoToOffset(offset);
    }

    private void StructureSelected(object? sender, SelectionChangedEventArgs e)
    {
        var index = StructureList.SelectedIndex;
        if (index < 0 || index >= _shownStructure.Count) return;
        GoToOffset(_shownStructure[index].Span.Start);
    }

    private void GoToOffset(int sourceOffset)
    {
        var globalOffset = _analysisStart + sourceOffset;
        if (globalOffset < _pageStart || globalOffset > _pageStart + _pageLength)
        {
            _pageHistory.Push(_pageStart);
            var lineStart = _document.Snapshot.GetLineStartOffset(
                _document.Snapshot.GetLineIndexFromOffset(globalOffset));
            _pageStart = globalOffset - lineStart > PageSize
                ? Math.Max(lineStart, globalOffset - PageSize / 2)
                : lineStart;
            ShowDocument(resetPage: false, caretGlobal: globalOffset);
        }
        Editor.CaretOffset = Math.Clamp(globalOffset - _pageStart, 0, Editor.Document.TextLength);
        Editor.Focus();
        Editor.ScrollToLine(Editor.Document.GetLineByOffset(Editor.CaretOffset).LineNumber);
    }

    private void HandleWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if ((e.KeyModifiers & command) != command) return;
        switch (e.Key)
        {
            case Key.O: OpenClicked(this, new RoutedEventArgs()); break;
            case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                SaveAsClicked(this, new RoutedEventArgs()); break;
            case Key.S: SaveClicked(this, new RoutedEventArgs()); break;
            case Key.Z when e.KeyModifiers.HasFlag(KeyModifiers.Shift): Redo(); break;
            case Key.Z: Undo(); break;
            case Key.Y: Redo(); break;
            default: return;
        }
        e.Handled = true;
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!HasUnsavedChanges) return true;
        var choice = await ShowChoiceAsync("Unsaved changes",
            "Save changes before replacing this file?", "Save", "Discard", "Cancel");
        if (choice == 0) return await SaveAsync(forcePicker: false);
        return choice == 1;
    }

    private async void WindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closingAfterConfirmation || !HasUnsavedChanges)
        {
            CancelAnalysis();
            _document.Dispose();
            return;
        }
        e.Cancel = true;
        if (!await ConfirmDiscardAsync()) return;
        _closingAfterConfirmation = true;
        Close();
    }

    private async Task ShowErrorAsync(string message) =>
        _ = await ShowChoiceAsync("mote", message, "OK");

    private async Task<int> ShowChoiceAsync(string title, string message, params string[] choices)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 430,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var result = choices.Length - 1;
        for (var i = 0; i < choices.Length; i++)
        {
            var index = i;
            var button = new Button { Content = choices[i], Margin = new Thickness(8, 0, 0, 0) };
            button.Click += (_, _) => { result = index; dialog.Close(); };
            buttons.Children.Add(button);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons }
        };
        await dialog.ShowDialog(this);
        return result;
    }

    private void CancelAnalysis()
    {
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        _analysisCancellation = null;
    }

    private TelemetryDimensions CurrentDimensions() =>
        new(Format: CurrentTelemetryFormat(_policy.Kind),
            DocumentBytes: _document.Snapshot.Length * sizeof(char),
            Version: _document.Snapshot.Version);

    private static TelemetryFormat CurrentTelemetryFormat(DocumentKind kind) => kind switch
    {
        DocumentKind.Markdown => TelemetryFormat.Markdown,
        DocumentKind.Toml => TelemetryFormat.Toml,
        DocumentKind.Json => TelemetryFormat.Json,
        DocumentKind.Yaml => TelemetryFormat.Yaml,
        DocumentKind.Csv => TelemetryFormat.Csv,
        _ => TelemetryFormat.PlainText
    };
}
