using System.Text;
using Mote.Configuration;
using Mote.Engine;
using Mote.Formats;
using Mote.Telemetry;
using Mote.Themes;
using Mote.Native.Accessibility;
using Mote.Native.Viewport;

namespace Mote.Native;

/// <summary>
/// Composes the canonical document engine with one bounded native text viewport.
/// The platform shell never owns persistence state or a second document model.
/// </summary>
internal sealed partial class NativeEditorController : IDisposable, IAccessibleViewport, IAccessibleSelection
{
    internal const int PageSize = 64 * 1024;
    internal const int PageSlack = 8 * 1024;
    private const int FullAnalysisLimit = 2 * 1024 * 1024;
    private readonly INativeEditorShell _shell;
    private readonly INativeCanvasShell? _canvasShell;
    private readonly AccessibleDocument? _accessibleDocument;
    /// <summary>Null retains the historical diagnostic adapter behavior.</summary>
    private readonly EditorPresentationProfile? _productProfile;
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private MoteConfiguration _configuration;
    /// <summary>The bounded background lane; source state never enters its closure.</summary>
    private readonly NativeSettingsReload _settingsReload;
    /// <summary>Latest complete snapshot waiting for natural native composition settlement.</summary>
    private MoteConfiguration? _pendingSettings;
    /// <summary>Diagnostics for current requested values and their OS-base composition.</summary>
    private string? _settingsNotice;
    /// <summary>Last rejected reload, retained through unrelated appearance callbacks.</summary>
    private string? _reloadFailureNotice;
    /// <summary>Last failed layout installation, retained until a deliberate settings retry.</summary>
    private string? _previewFailureNotice;
    /// <summary>An empty initial notice needs no native mutation; published notices still require explicit clearing.</summary>
    private bool _statusNoticePublished;
    /// <summary>Requested paths/writers differ from the running process-lifetime values.</summary>
    private bool _settingsRestartRequired;
    /// <summary>The palette last committed to both controller and native shell.</summary>
    private IThemePolicy _theme;
    /// <summary>The latest coalesced target while native preedit or a transition is active.</summary>
    private IThemePolicy? _pendingTheme;
    /// <summary>Detects appearance callbacks reentrant inside native palette application.</summary>
    private long _appearanceSerial;
    private bool _themeApplying;
    /// <summary>Keeps a privacy-safe failure notice until a palette commits successfully.</summary>
    private bool _themeUnavailable;
    private bool _shown;
    private readonly string? _startupPath;
    private Document _document = new();
    private NativeNavigationModel _navigation = new();
    private CanvasInteraction? _canvas;
    private long _canvasGeneration = 1;
    private long _canvasBindingNonce;
    private long _canvasBoundGeneration = -1;
    private long _canvasBoundVersion = -1;
    private int _canvasBoundActive = -1;
    private int _canvasInputStart;
    private int _canvasInputEnd;
    private string? _findQuery;
    private CancellationTokenSource? _findCancellation;
    private long _findSerial;
    private long _clipboardSerial;
    private bool _projectingSelection;
    private bool _nativeProjectsGlobalSelection;
    private IDocumentPolicy _policy = DocumentPolicies.ForKind(DocumentKind.PlainText);
    private NativeFormatSessionDriver? _sessionDriver;
    private NativeIdleFullAnalysis? _idleFullAnalysis;
    /// <summary>One bounded visible presentation and its source facts; never retains a format tree.</summary>
    private VisibleSessionFrame? _visibleSessionAnalysis;

    /// <summary>Pairs source overlays with the exact viewport and document generation that produced them.</summary>
    private sealed record VisibleSessionFrame(NativeAnalysisView View,
        NativeCanvasSemantics Semantics, Mote.Formats.TextSpan Viewport);

    /// <summary>One proved current-version conflict survives page navigation without retaining source or a tree.</summary>
    private TomlKnownError? _tomlKnownError;

    /// <summary>Identity-bound, bounded witness; replaced documents and every source edit revoke it.</summary>
    private sealed record TomlKnownError(Diagnostic Diagnostic, NativeDocumentStamp Stamp,
        Document Document, NativeFormatSessionDriver Driver, IDocumentPolicy Policy);
    /// <summary>The exact analysis currently offered to the native preview.</summary>
    private NativeAnalysisView? _presentedPreview;
    /// <summary>Monotonic identity for same-version viewport, policy and theme maps.</summary>
    private long _presentationSequence;
    private NativeTextProjection? _projection;
    private CanvasFrame? _lastCanvasFrame;
    private CancellationTokenSource? _analysisCancellation;
    private long _analysisSerial;
    /// <summary>One edit waiting for semantic installation; later edits cancel rather than steal its endpoint.</summary>
    private TelemetryMark _presentationMark;
    private TelemetryDimensions _presentationDimensions;
    /// <summary>Starts after local tracing configuration; deliberately excludes process launch/configuration.</summary>
    private TelemetryMark _startupMark;
    private int _pageStart;
    private int _pageLength;
    private int? _requestedCaretSource;
    private int _openSerial;
    /// <summary>The current request's interval stays owned by the UI lifetime even while I/O runs.</summary>
    private TelemetryMark _openMark;
    private int _openTraceRequest;
    private long _formatSerial;
    private string _operationStatus = "";
    /// <summary>Export is not Save; retain this document-bound warning through later analysis and operations.</summary>
    private bool _recoveryExported;
    private bool _saving;
    private volatile bool _accessibilityUnavailable;
    private bool _disposed;

    /// <summary>Wires platform events to engine transactions and static format policies.</summary>
    public NativeEditorController(INativeEditorShell shell, MoteConfiguration configuration,
        IThemePolicy theme, string? startupPath,
        EditorPresentationProfile? productProfile = null, Func<MoteConfiguration>? settingsLoader = null,
        TelemetryMark startupMark = default)
    {
        _shell = shell;
        _startupMark = startupMark;
        _canvasShell = shell is INativeCanvasShell { CanvasEnabled: true } canvas
            ? canvas : null;
        bool? expectedCanvas = productProfile switch
        {
            EditorPresentationProfile.Continuous => true,
            EditorPresentationProfile.LegacyPage or EditorPresentationProfile.NativeSource => false,
            null => null,
            _ => throw new ArgumentOutOfRangeException(nameof(productProfile))
        };
        if (expectedCanvas is { } expected && expected != (_canvasShell is not null))
            throw new ArgumentException("The product profile and native shell disagree.",
                nameof(productProfile));
        _sourceShell = shell is INativeSourceShell { NativeSourceEnabled: true } source ? source : null;
        if ((productProfile == EditorPresentationProfile.NativeSource) != (_sourceShell is not null) ||
            _sourceShell is not null && _canvasShell is not null)
            throw new ArgumentException("The product profile and source capability disagree.", nameof(productProfile));
        _productProfile = productProfile;
        _configuration = configuration;
        var startupComposition = ThemeComposer.Compose(theme, configuration.ThemeOverrides);
        _theme = ThemeEffectiveValues.Capture(startupComposition.Theme) == ThemeEffectiveValues.Capture(theme)
            ? theme : startupComposition.Theme;
        _settingsNotice = SettingsNotice(configuration, startupComposition.Issues);
        _settingsReload = new NativeSettingsReload(settingsLoader ?? (() => MoteConfigLoader.Load(
            new MoteConfigLoadOptions { MoteHomeOverride = configuration.HomeDirectory,
                UseEnvironmentOverride = false })), action => _shell.Post(action), SettingsLoaded);
        _startupPath = startupPath;
        if (_canvasShell is not null)
        {
            _canvas = NewCanvas(_document.Snapshot);
            _accessibleDocument = new AccessibleDocument(new AccessibleCanvasState(
                _canvasGeneration, _document.Snapshot, _canvas.Frame()));
        }
        _sessionDriver = CreateSessionDriver(_policy);
        _idleFullAnalysis = CreateIdleFullAnalysis(_sessionDriver, _document, _policy);
        _document.ChangedRange += DocumentChanged;
        if (_sourceShell is not null)
        {
            _sourceShell.SourceCandidate += SourceEdited;
            _sourceShell.SourceViewChanged += SourceViewChanged;
            _sourceShell.SourceRecoveryRequested += RecoverSource;
        }
        shell.TextChanged += Edited;
        shell.SelectionChanged += SelectionChanged;
        shell.PreviewActivated += PreviewActivated;
        shell.GridIntentRequested += GridIntentRequested;
        shell.GridWindowRequested += GridWindowRequested;
        shell.GridGestureBeginning += BeginGridGesture;
        shell.GridGestureRequested += GridGestureRequested;
        shell.GridGeometryChanged += GridGeometryChanged;
        shell.NewRequested += New;
        shell.OpenRequested += Open;
        if (shell is INativeExternalOpenShell externalOpenShell)
            externalOpenShell.ExternalOpenRequested += OpenExternal;
        if (shell is INativeOpenEncodingShell encodingShell)
            encodingShell.OpenWithEncodingRequested += OpenWithEncoding;
        shell.SaveRequested += StartSave;
        shell.UndoRequested += Undo;
        shell.RedoRequested += Redo;
        shell.FormatRequested += Format;
        shell.ReloadSettingsRequested += RequestSettingsReload;
        shell.PagePreviousRequested += PreviousPage;
        shell.PageNextRequested += NextPage;
        shell.FindRequested += Find;
        shell.FindNextRequested += FindNext;
        shell.GoToLineRequested += GoToLine;
        shell.SelectAllRequested += SelectAll;
        shell.CopyRequested += Copy;
        shell.CutRequested += Cut;
        shell.ClosingRequested += Closing;
        shell.Shown += Shown;
        shell.AppearanceChanged += AppearanceChanged;
        shell.CompositionSettled += CompositionSettled;
        if (_canvasShell is { } canvasShell)
        {
            canvasShell.CanvasEditCommitted += CanvasEdited;
            canvasShell.CanvasScrollRequested += CanvasScrolled;
            canvasShell.CanvasHorizontalAnchorRequested += CanvasHorizontalAnchorRequested;
            canvasShell.CanvasViewportResized += CanvasResized;
            canvasShell.CanvasSelectionRequested += CanvasSelected;
            canvasShell.CanvasAccessibilityFailed += CanvasAccessibilityFailed;
        }
    }

    /// <summary>Runs one native UI event loop on the calling thread.</summary>
    public void Run() => _shell.Run();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _activeSaveTrace?.EndOnce(TelemetryStatus.Cancelled, TelemetryReason.LifetimeEnded);
        RetireGridNavigation();
        CancelGridCopy();
        _shell.CancelSourceDrawTrace();
        FinishOpen(_openTraceRequest, TelemetryStatus.Cancelled);
        FinishEditPresentation(TelemetryStatus.Cancelled);
        MoteTelemetry.RecordElapsed(TelemetryOperation.StartupToEditable,
            _startupMark, status: TelemetryStatus.Cancelled);
        _startupMark = default;
        _settingsReload.Dispose();
        if (_shell is INativeOpenEncodingShell encodingShell)
            encodingShell.OpenWithEncodingRequested -= OpenWithEncoding;
        if (_sourceShell is not null)
        {
            _sourceShell.SourceCandidate -= SourceEdited;
            _sourceShell.SourceViewChanged -= SourceViewChanged;
            _sourceShell.SourceRecoveryRequested -= RecoverSource;
        }
        _shell.ReloadSettingsRequested -= RequestSettingsReload;
        _shell.AppearanceChanged -= AppearanceChanged;
        _shell.CompositionSettled -= CompositionSettled;
        _shell.PreviewActivated -= PreviewActivated;
        _shell.GridIntentRequested -= GridIntentRequested;
        _shell.GridWindowRequested -= GridWindowRequested;
        _shell.GridGestureBeginning -= BeginGridGesture;
        _shell.GridGestureRequested -= GridGestureRequested;
        _shell.GridGeometryChanged -= GridGeometryChanged;
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        _idleFullAnalysis?.Dispose();
        _analysisDispatcher?.Dispose();
        _visibleSessionAnalysis = null;
        _tomlKnownError = null;
        _document.ChangedRange -= DocumentChanged;
        _sessionDriver?.Dispose();
        _findCancellation?.Cancel();
        _findCancellation?.Dispose();
        _accessibleDocument?.Invalidate();
        _document.Dispose();
    }

    private void Shown()
    {
        try { _shell.SetTheme(_theme); }
        catch (NativeThemeDeferredException) { _pendingTheme = _theme; }
        catch (Exception) { ReportThemeFailure(); }
        _shown = true;
        ApplyPendingTheme();
        if (_themeUnavailable || _settingsNotice is not null) UpdateStatusNotice();
        ShowDocument();
        MoteTelemetry.RecordElapsed(TelemetryOperation.StartupToEditable,
            _startupMark, Dimensions(_document.Snapshot));
        _startupMark = default;
        if (_canvasShell is not null)
        {
            try { _canvasShell.SetCanvasAccessibility(_accessibleDocument!, this); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // AX is not allowed to make the opt-in editor itself unusable.
                // Keep the failure visible, but do not disclose paths or text.
                _accessibilityUnavailable = true;
                UpdateStatusNotice();
                ShowDocument();
            }
        }
        ScheduleAnalysis();
        if (_startupPath is not null) StartOpen(_startupPath);
    }

    /// <summary>
    /// Re-resolves every configured ID against the current OS preference.
    /// Explicit IDs naturally stay unchanged; system and unknown-ID fallback
    /// follow the OS without a separate theme-selection branch.
    /// </summary>
    private void AppearanceChanged()
    {
        if (_disposed) return;
        if (Environment.CurrentManagedThreadId != _uiThreadId)
        {
            try { TryPost(AppearanceChanged); }
            catch (Exception) { /* Never unwind through an OS appearance callback. */ }
            return;
        }
        try
        {
            ++_appearanceSerial;
            ResolveRequestedTheme();
            ApplyPendingTheme();
        }
        catch (Exception)
        {
            _pendingTheme = null;
            ReportThemeFailure();
        }
    }

    /// <summary>Retries the coalesced appearance only after native preedit has settled.</summary>
    private void CompositionSettled()
    {
        if (_disposed) return;
        if (Environment.CurrentManagedThreadId != _uiThreadId)
        {
            try { TryPost(CompositionSettled); }
            catch (Exception) { /* Never unwind through an OS composition callback. */ }
            return;
        }
        try
        {
            ApplyPendingSettings();
            ApplyPendingTheme();
            UpdateStatusNotice();
        }
        catch (Exception) { ReportThemeFailure(); }
    }

    /// <summary>
    /// Applies the palette as one UI-thread transition without editing source,
    /// rebuilding a document projection, or interrupting marked text.
    /// </summary>
    private void ApplyPendingTheme()
    {
        if (!_shown || _themeApplying || _pendingTheme is null || _shell.IsTextComposing)
            return;
        var next = _pendingTheme;
        _pendingTheme = null;
        var appearanceSerial = _appearanceSerial;
        _themeApplying = true;
        try
        {
            _shell.SetTheme(next);
            if (_presentedPreview is { } presented) PresentAnalysis(presented);
            _theme = next;
            _themeUnavailable = false;
            UpdateStatusNotice();
        }
        catch (NativeThemeDeferredException)
        {
            // The native adapter promises it has not changed any paint role.
            // Keep the last requested policy for the final IME callback.
            _pendingTheme = next;
        }
        catch (Exception)
        {
            // A platform adapter may have applied some paint roles before an
            // OS resource failed. Best-effort rollback leaves source untouched.
            try
            {
                _shell.SetTheme(_theme);
                if (_presentedPreview is { } previousView) PresentAnalysis(previousView);
            }
            catch (Exception) { /* Keep the canonical model and report a recoverable UI fault. */ }
            ReportThemeFailure();
        }
        finally { _themeApplying = false; }
        if (_pendingSettings is not null)
        {
            try { TryPost(ApplyPendingSettings); }
            catch (Exception) { /* A composition-settled event can retry the latest settings. */ }
        }
        if (appearanceSerial != _appearanceSerial)
        {
            // An appearance callback may run inside SetTheme while _theme still
            // denotes the old policy. Resolve again against the committed theme.
            try
            {
                ResolveRequestedTheme();
            }
            catch (Exception) { ReportThemeFailure(); }
        }
        if (_pendingTheme is not null)
        {
            try { TryPost(ApplyPendingTheme); }
            catch (Exception) { /* The next OS event can retry a pending palette. */ }
        }
    }

    /// <summary>Retains a nonmodal warning without interrupting native composition.</summary>
    private void ReportThemeFailure()
    {
        _themeUnavailable = true;
        try
        {
            TryPost(() => UpdateStatusNotice());
        }
        catch (Exception) { /* A failed status refresh must not escape an OS callback. */ }
    }

    /// <summary>
    /// Composes current UI-owned notices independently of cached document/analysis status.
    /// Native analysis replay cannot erase newer operation state or revive a cleared warning.
    /// Idle paint can skip an empty initial notice; explicit settings/health events still publish.
    /// </summary>
    private void UpdateStatusNotice(bool skipInitialEmpty = false)
    {
        if (_disposed || !_shown || _shell.IsTextComposing) return;
        try
        {
            var health = MoteTelemetry.Health;
            var notice = string.Join(" ", new[]
            {
                _accessibilityUnavailable ? (_productProfile == EditorPresentationProfile.Continuous
                    ? "AX unavailable: save, restart --legacy-page"
                    : "Accessibility provider unavailable") : null,
                _themeUnavailable ? "Theme update unavailable; editing remains available." : null,
                _reloadFailureNotice, _previewFailureNotice, _settingsNotice,
                _configuration.Diagnostics.Count == 0 ? null :
                    $"Config warnings: {_configuration.Diagnostics.Count}",
                health.SinkFaulted || health.DroppedRecords > 0 ? "Trace degraded" : null,
                _recoveryExported
                    ? "Recovery exported. Current buffer is not saved; Save again when ready." : null,
                _operationStatus
            }.Where(item => !string.IsNullOrEmpty(item)));
            if (skipInitialEmpty && notice.Length == 0 && !_statusNoticePublished) return;
            _shell.SetStatusNotice(notice.Length == 0 ? null : notice);
            _statusNoticePublished = true;
        }
        catch (Exception) { /* A status failure must not disable editing. */ }
    }

    /// <summary>Explicit reload never commits composition, reads source, or writes a file.</summary>
    internal void RequestSettingsReload()
    {
        if (_disposed) return;
        _pendingSettings = null;
        _settingsReload.Request();
    }

    /// <summary>Publishes only the newest completed read, then defers visual changes until preedit settles.</summary>
    private void SettingsLoaded(MoteConfiguration? config, Exception? error)
    {
        if (_disposed) return;
        if (error is not null || config is null || config.ReadDisposition == ConfigReadDisposition.Rejected)
        {
            _reloadFailureNotice = config is null
                ? "Settings reload failed; previous settings retained."
                : "Settings reload rejected; previous settings retained. " + SettingsNotice(config, []);
            UpdateStatusNotice();
            return;
        }
        _pendingSettings = Program.ValidateThemeId(config, out _);
        ApplyPendingSettings();
    }

    /// <summary>Applies independent live groups while keeping writer destinations at their startup lifetime.</summary>
    private void ApplyPendingSettings()
    {
        if (_pendingSettings is null || _shell.IsTextComposing || _themeApplying) return;
        var requested = _pendingSettings;
        _pendingSettings = null;
        var composition = ThemeComposer.Compose(
            ThemePolicies.Resolve(requested.ThemeId, _shell.PrefersDark), requested.ThemeOverrides);
        var themeAccepted = requested.ThemeOverridesAccepted && composition.Issues.Count == 0;
        _settingsRestartRequired = requested.CacheDirectory != _configuration.CacheDirectory ||
            requested.DataDirectory != _configuration.DataDirectory ||
            requested.TraceDirectory != _configuration.TraceDirectory ||
            requested.TraceEnabled != _configuration.TraceEnabled;
        var previousLayout = _configuration.PreviewLayout;
        _previewFailureNotice = null;
        _configuration = requested with
        {
            CacheDirectory = _configuration.CacheDirectory,
            DataDirectory = _configuration.DataDirectory,
            TraceDirectory = _configuration.TraceDirectory,
            TraceEnabled = _configuration.TraceEnabled,
            ThemeId = themeAccepted ? requested.ThemeId : _configuration.ThemeId,
            ThemeOverrides = themeAccepted ? requested.ThemeOverrides : _configuration.ThemeOverrides
        };
        _settingsNotice = SettingsNotice(requested, composition.Issues);
        _reloadFailureNotice = themeAccepted ? null :
            "Theme overrides rejected; previous theme retained. " + SettingsNotice(requested, composition.Issues);
        if (_settingsRestartRequired) _settingsNotice += " Writer/path changes apply on next launch.";
        if (themeAccepted) ResolveRequestedTheme();
        ApplyPendingTheme();
        if (previousLayout != _configuration.PreviewLayout && _presentedPreview is { } view)
        {
            try { PresentAnalysis(view); }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                _configuration = _configuration with { PreviewLayout = previousLayout };
                _previewFailureNotice = "Preview layout update unavailable; previous preference retained.";
                try { PresentAnalysis(view); }
                catch (Exception rollback) when (rollback is not OutOfMemoryException)
                { _previewFailureNotice += " Native preview rollback unavailable."; }
            }
        }
        UpdateStatusNotice();
    }

    /// <summary>OS transitions always recompose requested data; invalid contrast uses the newly selected base.</summary>
    private void ResolveRequestedTheme()
    {
        var baseTheme = ThemePolicies.Resolve(_configuration.ThemeId, _shell.PrefersDark);
        var composed = ThemeComposer.Compose(baseTheme, _configuration.ThemeOverrides);
        var resolved = ThemeEffectiveValues.Capture(composed.Theme) == ThemeEffectiveValues.Capture(baseTheme)
            ? baseTheme : composed.Theme;
        _settingsNotice = SettingsNotice(_configuration, composed.Issues);
        if (_settingsRestartRequired) _settingsNotice += " Writer/path changes apply on next launch.";
        _pendingTheme = ThemeEffectiveValues.Capture(resolved) == ThemeEffectiveValues.Capture(_theme)
            ? null : resolved;
        UpdateStatusNotice();
    }

    /// <summary>Bounded persistent, nonmodal settings details; no content or palette text enters telemetry.</summary>
    private static string? SettingsNotice(MoteConfiguration config, IReadOnlyList<ThemeOverrideIssue> issues)
    {
        if (config.Diagnostics.Count == 0 && issues.Count == 0) return null;
        var details = config.Diagnostics.Select(item => item.Code + ": " + item.Message)
            .Concat(issues.Select(item => item.Code + " " + item.Role + ": " + item.Message +
                (item.Ratio is { } ratio ? $" ({ratio:F2}:1; requires {item.Minimum:F1}:1)" : "")))
            .Take(32).Select(item => item.Length > 256 ? item[..256] + "…" : item);
        var omitted = config.Diagnostics.Count + issues.Count - 32;
        return $"Settings ({config.ConfigPath}): " + string.Join(" · ", details) +
            (omitted > 0 ? $" · {omitted} additional settings details omitted." : "");
    }

    private void New()
    {
        if (!CanReplace()) return;
        ReplaceDocument(new Document());
    }

    private void Open()
    {
        if (!CanReplace()) return;
        var path = _shell.PickOpenFile();
        if (path is not null) StartOpen(path);
    }

    /// <summary>Admits OS-delivered single-document paths without bypassing pending text or discard consent.</summary>
    private bool OpenExternal(string path)
    {
        if (_disposed || string.IsNullOrWhiteSpace(path)) return false;
        if (!CanReplace(out var previous, out var version, out var serial) ||
            _disposed || serial != _openSerial ||
            !ReferenceEquals(previous, _document) || version != _document.Snapshot.Version) return false;
        StartOpen(path);
        return true;
    }

    /// <summary>Explicit choice also admits BOM-less files that happen to be valid under another codec.</summary>
    private void OpenWithEncoding()
    {
        if (_shell is not INativeOpenEncodingShell encodingShell || !CanReplace()) return;
        var previous = _document;
        var version = previous.Snapshot.Version;
        var serial = _openSerial;
        var path = _shell.PickOpenFile();
        if (path is null || _disposed || serial != _openSerial) return;
        var selected = ChooseOpenEncoding(encodingShell);
        if (selected is null || _disposed || serial != _openSerial) return;
        StartOpenCore(path, selected, previous, version);
    }

    /// <summary>Contains chooser failure without consuming a pending open or changing document state.</summary>
    private DocumentTextEncoding? ChooseOpenEncoding(INativeOpenEncodingShell shell)
    {
        try { return shell.ChooseOpenEncoding(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _shell.ShowError($"Cannot choose a source encoding: {error.Message}");
            return null;
        }
    }

    private bool CanReplace() => CanReplace(out _, out _, out _);

    /// <summary>Captures the version after input settlement but before potentially reentrant discard dialogs.</summary>
    private bool CanReplace(out Document previous, out long version, out long serial)
    {
        previous = _document;
        version = previous.Snapshot.Version;
        serial = _openSerial;
        if (!_shell.CommitPendingText()) return false;
        previous = _document;
        version = previous.Snapshot.Version;
        serial = _openSerial;
        if (_saving)
        {
            _shell.ShowError("Wait for the current save to finish before replacing this document.");
            return false;
        }
        WarnRetainedRecovery();
        return !_document.IsModified || _shell.ConfirmDiscard();
    }

    /// <summary>Retains the existing single-argument default-open entry and its diagnostic callers.</summary>
    private void StartOpen(string path) => StartOpenCore(path);

    /// <summary>Retains the originally approved document/version across a deliberate codec retry.</summary>
    private void StartOpenCore(string path, DocumentTextEncoding? selected = null,
        Document? expectedDocument = null, long? expectedVersion = null)
    {
        FinishOpen(_openTraceRequest, TelemetryStatus.Cancelled);
        var request = ++_openSerial;
        var previous = expectedDocument ?? _document;
        var version = expectedVersion ?? previous.Snapshot.Version;
        var openMark = MoteTelemetry.Mark();
        _openMark = openMark;
        _openTraceRequest = request;
        _ = Task.Run(async () =>
        {
            Document? opened = null;
            Exception? error = null;
            string? failureMessage = null;
            try
            {
                using var io = MoteTelemetry.StartChild(TelemetryOperation.DocumentOpen, openMark);
                io?.SetStatus(TelemetryStatus.Failure);
                opened = selected is { } encoding
                    ? await Document.OpenWithEncodingAsync(path, encoding).ConfigureAwait(false)
                    : await Document.OpenAsync(path).ConfigureAwait(false);
                io?.SetStatus(TelemetryStatus.Success);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                error = ex;
                failureMessage = OpenFailureMessage(path, ex);
            }
            Post(() =>
            {
                if (_disposed || request != _openSerial)
                {
                    opened?.Dispose();
                    FinishOpen(request, TelemetryStatus.Cancelled);
                    return;
                }
                if (error is not null)
                {
                    FinishOpen(request, TelemetryStatus.Failure);
                    if (selected is null && error is DecoderFallbackException &&
                        _shell is INativeOpenEncodingShell encodingShell)
                    {
                        var choice = ChooseOpenEncoding(encodingShell);
                        if (choice is not null && !_disposed && request == _openSerial)
                            StartOpenCore(path, choice, previous, version);
                        return;
                    }
                    _shell.ShowError(failureMessage!);
                    return;
                }
                if (opened is null) return;
                if (!SettleInputBeforeAsyncResult())
                {
                    opened.Dispose();
                    FinishOpen(request, TelemetryStatus.Cancelled);
                    return;
                }
                if (_saving)
                {
                    opened.Dispose();
                    FinishOpen(request, TelemetryStatus.Cancelled);
                    _shell.ShowError("Wait for the current save to finish before opening another file.");
                    return;
                }
                if (!ReferenceEquals(previous, _document) || version != _document.Snapshot.Version)
                {
                    if (!CanReplace())
                    {
                        opened.Dispose();
                        FinishOpen(request, TelemetryStatus.Cancelled);
                        return;
                    }
                }
                ReplaceDocument(opened, MoteTelemetry.Fork(openMark), request);
                FinishOpen(request, TelemetryStatus.Success, Dimensions(opened.Snapshot));
            });
        });
    }

    /// <summary>Discovers a restart sidecar only on Open failure; never adopts, reads or removes its bytes.</summary>
    private static string OpenFailureMessage(string path, Exception error)
    {
        var message = $"Cannot open file: {error.Message}";
        try
        {
            var recovery = Document.GetSaveRecoveryPath(path);
            if (File.Exists(recovery))
                message += $"\nAn unowned, unverified recovery sidecar was found for this path:\n{recovery}\n" +
                    "Inspect or copy it manually. It has not been opened, adopted, or deleted by mote.";
        }
        catch (Exception hintError) when (hintError is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            // Optional discovery cannot replace the actual Open failure.
        }
        return message;
    }

    private void ReplaceDocument(Document replacement, TelemetryMark drawMark = default, int openRequest = 0)
    {
        ResetGridInterest();
        _activeSaveTrace?.EndOnce(TelemetryStatus.Cancelled, TelemetryReason.StaleDocument);
        if (openRequest == 0) FinishOpen(_openTraceRequest, TelemetryStatus.Cancelled);
        ++_openSerial;
        CancelAnalysis();
        _idleFullAnalysis?.Dispose();
        _visibleSessionAnalysis = null;
        _tomlKnownError = null;
        _document.ChangedRange -= DocumentChanged;
        _sessionDriver?.Dispose();
        _document.Dispose();
        _document = replacement;
        _document.ChangedRange += DocumentChanged;
        _navigation = new NativeNavigationModel();
        _sourceBinding = null;
        _sourceUnavailable = false;
        _sourceFailure = NativeSourceFailure.CanonicalRetained;
        _sourceViewportSequence = 0;
        ++_canvasGeneration;
        _canvasBoundGeneration = -1;
        _canvasBoundVersion = -1;
        _canvasBoundActive = -1;
        if (_canvasShell is not null)
            _canvas = NewCanvas(replacement.Snapshot);
        _nativeProjectsGlobalSelection = false;
        _findCancellation?.Cancel();
        _findQuery = null;
        ++_findSerial;
        ++_clipboardSerial;
        ++_formatSerial;
        _operationStatus = "";
        _recoveryExported = false;
        _policy = replacement.FilePath is { } path
            ? DocumentPolicies.ForPath(path)
            : DocumentPolicies.ForKind(DocumentKind.PlainText);
        _sessionDriver = CreateSessionDriver(_policy);
        _idleFullAnalysis = CreateIdleFullAnalysis(_sessionDriver, _document, _policy);
        _pageStart = 0;
        _pageLength = 0;
        _requestedCaretSource = 0;
        TraceSourceDraw(drawMark, TelemetryOperation.OpenToDrawSubmission);
        ShowDocument(drawMark);
        ScheduleAnalysis();
    }

    /// <summary>Reports observed bytes without treating dirty state or an error code as preservation proof.</summary>
    private static string SaveFailureMessage(Exception error)
    {
        var info = SaveFailureInfo.FromException(error);
        if (info is null) return $"Save failed. Disk outcome was not verified. {error.Message}";
        var outcome = info.TargetOutcome switch
        {
            SaveFileOutcome.OriginalSnapshot => "The target matched the original snapshot when checked.",
            SaveFileOutcome.SavedSnapshot => "The target matched the attempted saved snapshot when checked; Save bookkeeping did not complete.",
            SaveFileOutcome.Missing => "The target was missing when checked.",
            SaveFileOutcome.OtherContent => "The target contained different bytes when checked; it was not overwritten again.",
            _ => "The target outcome could not be verified."
        };
        var stage = info.RecoveryOutcome == SaveFileOutcome.Missing
            ? "No recovery sidecar was found."
            : $"Inspect the recovery sidecar: {info.RecoveryPath}\n" +
                "Its bytes may belong to an earlier attempt; they are not automatically overwritten or deleted.";
        return $"Save failed. {outcome}\n{stage}\n{error.Message}";
    }

    /// <summary>Uses an explicit new-path export; exporting an old staged version does not save later edits.</summary>
    private void StartRecoveryExport(Document document, SaveRecovery recovery)
    {
        if (!recovery.IsCompleteSnapshot)
        {
            _shell.ShowError($"Incomplete Save recovery remains at:\n{recovery.Path}\n" +
                "These bytes are not a verified complete snapshot. Inspect or copy the sidecar manually. " +
                "Close and reopen after explicitly resolving the sidecar; your current buffer is still not saved.");
            return;
        }
        _shell.ShowError($"Save recovery is pending at:\n{recovery.Path}\n" +
            $"It contains attempted snapshot v{recovery.SnapshotVersion}; later edits are not included. " +
            "Choose a NEW path to export it, then Save again to save the current buffer. " +
            "Cancel keeps recovery; existing files will not be overwritten.");
        var path = _shell.PickSaveFile(null);
        if (path is null) return;
        _saving = true;
        _ = Task.Run(async () =>
        {
            Exception? error = null;
            try { await document.ExportSaveRecoveryAsync(path).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { error = ex; }
            Post(() =>
            {
                _saving = false;
                if (_disposed) return;
                if (error is not null)
                {
                    _shell.ShowError($"Recovery export failed; inspect the pending sidecar at {recovery.Path}. " +
                        $"Current buffer was not saved. {error.Message}");
                    return;
                }
                _recoveryExported = true;
                ShowDocument();
            });
        });
    }

    /// <summary>Closing/replacing may discard the buffer, but never silently discards staged recovery.</summary>
    private void WarnRetainedRecovery()
    {
        if (_document.PendingSaveRecovery is { } recovery)
            _shell.ShowError($"The retained Save recovery will remain after closing this document:\n{recovery.Path}\n" +
                (recovery.IsCompleteSnapshot
                    ? $"It is attempted snapshot v{recovery.SnapshotVersion}, not necessarily your latest edits. "
                    : "It is incomplete or unverified staged data, not a guaranteed full snapshot. ") +
                "Inspect, copy, or remove it explicitly before saving this target again.");
    }

    private void Edited(string editedDisplay)
    {
        if (_canvasShell is not null || _sourceShell is not null) return;
        if (_projection is null) return;
        if (_projection.Difference(editedDisplay) is not { } change) return;
        var mark = MoteTelemetry.Mark();
        try
        {
            var globalSelection = _nativeProjectsGlobalSelection &&
                _navigation.SelectionLength > 0 &&
                (_navigation.SelectionStart < _pageStart ||
                 _navigation.SelectionStart + _navigation.SelectionLength > _pageStart + _pageLength);
            var applied = globalSelection
                ? new TextChange(_navigation.SelectionStart, _navigation.SelectionLength,
                    change.InsertText)
                : new TextChange(_pageStart + change.Start, change.DeleteLength,
                    change.InsertText);
            ApplyTraced(applied, mark);
            TraceSourceDraw(MoteTelemetry.Fork(mark));
            var newCaret = applied.Start + applied.InsertText.Length;
            _navigation.MoveCaret(_document.Snapshot, newCaret);
            _nativeProjectsGlobalSelection = false;
            InvalidateFind();
            if (globalSelection)
            {
                _pageStart = Math.Max(0, applied.Start - 1024);
                _pageLength = 0;
                _requestedCaretSource = newCaret;
                ShowDocument(mark);
                ScheduleAnalysis(mark);
                return;
            }
            var desiredLength = _pageLength + change.InsertText.Length - change.DeleteLength;
            if (desiredLength > PageSize + PageSlack)
            {
                _pageStart = Math.Max(0, newCaret - PageSize + 2048);
                _pageLength = 0;
                _requestedCaretSource = newCaret;
            }
            else
            {
                _pageLength = Math.Max(0, desiredLength);
                if (_pageLength < PageSize / 2 &&
                    _pageStart + _pageLength < _document.Snapshot.Length)
                {
                    _pageLength = 0;
                    _requestedCaretSource = newCaret;
                }
            }
            MoteTelemetry.Record(TelemetryEvent.EditCommitted,
                dimensions: Dimensions(_document.Snapshot));
            ShowDocument(mark);
            ScheduleAnalysis(mark);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _shell.ShowError($"This edit cannot be represented safely: {ex.Message}");
            ShowDocument();
        }
    }

    private void Undo()
    {
        if (!_shell.CommitPendingText()) return;
        var mark = MoteTelemetry.Mark();
        if (_document.Undo())
        {
            TraceSourceDraw(MoteTelemetry.Fork(mark));
            _pageLength = 0;
            ShowDocument(mark);
            RevealSelection();
            ProjectSelection();
            ScheduleAnalysis(mark);
        }
    }

    private void Redo()
    {
        if (!_shell.CommitPendingText()) return;
        var mark = MoteTelemetry.Mark();
        if (_document.Redo())
        {
            TraceSourceDraw(MoteTelemetry.Fork(mark));
            _pageLength = 0;
            ShowDocument(mark);
            RevealSelection();
            ProjectSelection();
            ScheduleAnalysis(mark);
        }
    }

    private void Format()
    {
        if (!_shell.CommitPendingText()) return;
        var snapshot = _document.Snapshot;
        if (snapshot.Length > FullAnalysisLimit)
        {
            _shell.ShowError("Formatting this large document requires a bounded, trivia-preserving formatter; it is not available yet.");
            return;
        }
        var document = _document;
        var policy = _policy;
        var serial = ++_formatSerial;
        _operationStatus = "Formatting in background…";
        ShowDocument();
        _ = Task.Run(() =>
        {
            string? formatted = null;
            var changed = false;
            Exception? error = null;
            try
            {
                var original = snapshot.GetText();
                formatted = policy.Format(original);
                changed = formatted != original;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { error = ex; }
            Post(() =>
            {
                if (_disposed || serial != _formatSerial) return;
                if (!ReferenceEquals(document, _document) ||
                    _document.Snapshot.Version != snapshot.Version)
                {
                    _operationStatus = "Format result discarded after a newer edit.";
                    ShowDocument();
                    return;
                }
                if (!SettleInputBeforeAsyncResult())
                {
                    _operationStatus = "Formatting cancelled during text composition.";
                    ShowDocument();
                    return;
                }
                _operationStatus = "";
                if (error is not null)
                {
                    _shell.ShowError($"Formatting failed without changing the file: {error.Message}");
                    ShowDocument();
                    return;
                }
                if (!ReferenceEquals(document, _document) ||
                    _document.Snapshot.Version != snapshot.Version)
                {
                    _operationStatus = "Format result discarded after a newer edit.";
                    ShowDocument();
                    return;
                }
                var mark = MoteTelemetry.Mark();
                if (changed && formatted is not null)
                {
                    ApplyTraced(new TextChange(0, snapshot.Length, formatted), mark);
                    TraceSourceDraw(MoteTelemetry.Fork(mark));
                    _navigation.MoveCaret(_document.Snapshot, 0);
                    _pageStart = 0;
                    _pageLength = 0;
                    _requestedCaretSource = 0;
                }
                ShowDocument(mark);
                if (changed) ScheduleAnalysis(mark);
            });
        });
    }

    private void PreviousPage()
    {
        if (_sourceShell is not null) return;
        if (!_shell.CommitPendingText()) return;
        if (_canvas is not null)
        {
            CanvasScrolled(-_canvas.ViewportHeight);
            return;
        }
        if (_pageStart == 0) return;
        InvalidateFind();
        _pageStart = Math.Max(0, _pageStart - PageSize);
        _pageLength = 0;
        _requestedCaretSource = _pageStart;
        ShowDocument();
        ProjectSelectionOrMoveToPage();
        ScheduleAnalysis();
    }

    private void NextPage()
    {
        if (_sourceShell is not null) return;
        if (!_shell.CommitPendingText()) return;
        if (_canvas is not null)
        {
            CanvasScrolled(_canvas.ViewportHeight);
            return;
        }
        var length = _document.Snapshot.Length;
        if (_pageStart + _pageLength >= length) return;
        InvalidateFind();
        _pageStart += _pageLength;
        _pageLength = 0;
        _requestedCaretSource = _pageStart;
        ShowDocument();
        ProjectSelectionOrMoveToPage();
        ScheduleAnalysis();
    }

    private void SelectionChanged(int displayAnchor, int displayActive)
    {
        if (_sourceShell is not null) return;
        if (_canvasShell is not null) return;
        if (_projectingSelection || _projection is null) return;
        if ((uint)displayAnchor > (uint)_projection.Display.Length ||
            (uint)displayActive > (uint)_projection.Display.Length) return;
        var forward = displayActive >= displayAnchor;
        var anchor = _projection.ToSourceBoundary(displayAnchor, towardEnd: !forward);
        var active = _projection.ToSourceBoundary(displayActive, towardEnd: forward &&
            displayActive != displayAnchor);
        var nextAnchor = _pageStart + anchor;
        var nextActive = _pageStart + active;
        if (nextAnchor != _navigation.Anchor || nextActive != _navigation.Active)
        {
            var showedSearch = _operationStatus == "Searching document…";
            InvalidateFind();
            _navigation.SetSelection(_document.Snapshot, nextAnchor, nextActive);
            if (showedSearch) ShowDocument();
        }
        _nativeProjectsGlobalSelection = _navigation.SelectionLength > 0;
    }

    /// <summary>
    /// Accepts exactly one final, version- and binding-tagged native edit. The
    /// source document changes synchronously before the input callback returns,
    /// so its next keystroke uses the new binding nonce and engine version.
    /// </summary>
    private void CanvasEdited(CanvasCommittedEdit edit)
    {
        if (_canvasShell is null || _disposed) return;
        var snapshot = _document.Snapshot;
        if (edit.DocumentGeneration != _canvasGeneration ||
            edit.BaseVersion != snapshot.Version ||
            edit.BindingNonce != _canvasBindingNonce) return;
        // The OS adapter must report the exact global transaction, including
        // any selection outside its bounded host. Never substitute a possibly
        // stale controller selection for a later native selection change.
        var change = edit.Change;
        if (change.InsertText is null || change.Start < 0 || change.DeleteLength < 0 ||
            change.Start > snapshot.Length - change.DeleteLength ||
            edit.ActiveSourceOffset != (long)change.Start + change.InsertText.Length ||
            (_navigation.SelectionLength > 0 &&
             (change.Start != _navigation.SelectionStart ||
              change.DeleteLength != _navigation.SelectionLength)) ||
            (_navigation.SelectionLength == 0 &&
             (change.Start < _canvasInputStart ||
              (long)change.Start + change.DeleteLength > _canvasInputEnd)))
        {
            _shell.ShowError("The native input could not be mapped to the document safely.");
            _canvasBoundVersion = -1;
            ShowDocument();
            return;
        }
        try
        {
            var mark = MoteTelemetry.Mark();
            ApplyTraced(change, mark);
            TraceSourceDraw(MoteTelemetry.Fork(mark));
            var newCaret = change.Start + change.InsertText.Length;
            _navigation.MoveCaret(_document.Snapshot, newCaret);
            _canvas!.Reveal(newCaret);
            InvalidateFind();
            MoteTelemetry.Record(TelemetryEvent.EditCommitted,
                dimensions: Dimensions(_document.Snapshot));
            ShowDocument(mark);
            EnsureCanvasCaretVisible(newCaret);
            ScheduleAnalysis(mark);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _shell.ShowError("The native input could not be committed safely.");
            _canvasBoundVersion = -1;
            ShowDocument();
        }
    }

    /// <summary>Advances the same source-backed viewport, never a native text page.</summary>
    private void CanvasScrolled(double pixels)
    {
        if (_canvas is null || !double.IsFinite(pixels) || pixels == 0) return;
        _canvas.ScrollBy(pixels);
        ShowDocument();
        ScheduleAnalysis();
    }

    /// <summary>
    /// Applies one versioned, platform-shaped horizontal edge without changing
    /// document contents or the global selection. An IME-owned host is never
    /// repositioned under an active candidate.
    /// </summary>
    private void CanvasHorizontalAnchorRequested(CanvasHorizontalAnchorRequest request)
    {
        if (_canvas is null || _canvasShell is null || _disposed ||
            _canvasShell.IsCanvasComposing ||
            request.DocumentGeneration != _canvasGeneration ||
            request.BaseVersion != _document.Snapshot.Version) return;
        try
        {
            _canvas.SetHorizontalAnchor(request.SourceBoundary, request.Affinity,
                request.IntraClusterPixels);
        }
        catch (ArgumentException)
        {
            // A stale or non-grapheme platform edge must never alter source state.
            return;
        }
        ShowDocument();
        ScheduleAnalysis();
    }

    /// <summary>Recomputes only visible geometry after a native view resize.</summary>
    private void CanvasResized(double height)
    {
        if (_canvas is null || !double.IsFinite(height) || height < 0) return;
        _canvas.Resize(height);
        ShowDocument();
        ScheduleAnalysis();
    }

    /// <summary>Stores OS hit-test endpoints as one global UTF-16 selection.</summary>
    private void CanvasSelected(int anchor, int active)
    {
        if (_canvas is null || _disposed) return;
        var snapshot = _document.Snapshot;
        if ((uint)anchor > (uint)snapshot.Length || (uint)active > (uint)snapshot.Length)
            return;
        if (SafeBoundary(snapshot, anchor, backwards: true) != anchor ||
            SafeBoundary(snapshot, active, backwards: true) != active) return;
        if (anchor == _navigation.Anchor && active == _navigation.Active) return;
        InvalidateFind();
        _navigation.SetSelection(snapshot, anchor, active);
        ShowDocument();
    }

    /// <summary>
    /// Resolves a native preview gesture through the exact rendered semantic
    /// span and current engine version; it never commits marked text or edits.
    /// </summary>
    private void PreviewActivated(NativePreviewActivation activation)
    {
        if (_disposed || _shell.IsTextComposing || _canvasShell?.IsCanvasComposing == true)
            return;
        var snapshot = _document.Snapshot;
        var stamp = new NativeDocumentStamp(_canvasGeneration, snapshot.Version);
        if (activation.Stamp != stamp || _presentedPreview is not { } presented ||
            presented.Stamp != stamp || activation.Identity != presented.Identity ||
            !presented.ShowPreview) return;
        var source = NativePreviewNavigation.SourceStart(presented,
            activation.PreviewOffset, snapshot.Length);
        if (source is not { } offset ||
            SafeBoundary(snapshot, offset, backwards: true) != offset) return;
        InvalidateFind();
        _navigation.MoveCaret(snapshot, offset);
        _nativeProjectsGlobalSelection = false;
        RevealSelection();
        ProjectSelection();
        _shell.FocusSource();
    }

    /// <summary>
    /// Keeps a detached provider failure visible across subsequent Open/New
    /// without allowing an accessibility callback to unwind through the OS.
    /// </summary>
    private void CanvasAccessibilityFailed()
    {
        if (_disposed) return;
        _accessibilityUnavailable = true;
        Post(() =>
        {
            if (_disposed) return;
            UpdateStatusNotice();
            ShowDocument();
        });
    }

    /// <summary>
    /// Reveals one version-bound source interval for an OS accessibility
    /// provider without ever settling or moving an active IME composition.
    /// A successful result includes a synchronous matching frame publication.
    /// </summary>
    public AccessibleRevealResult TryReveal(AccessibleRange range, bool alignToTop)
    {
        if (Environment.CurrentManagedThreadId != _uiThreadId)
            return AccessibleRevealResult.WrongThread;
        if (_disposed || _canvas is null || _canvasShell is null)
            return AccessibleRevealResult.StaleRange;
        var snapshot = _document.Snapshot;
        if (range.Generation != _canvasGeneration || range.Version != snapshot.Version ||
            range.Start < 0 || range.End < range.Start || range.End > snapshot.Length)
            return AccessibleRevealResult.StaleRange;
        // Neither half of CRLF nor a surrogate interior is a painted caret
        // boundary; fail before moving the viewport at all.
        if (SafeBoundary(snapshot, range.Start, backwards: true) != range.Start)
            return AccessibleRevealResult.NotVisible;
        if (_canvasShell.IsCanvasComposing)
            return AccessibleRevealResult.CompositionBlocked;

        _canvas.Reveal(range.Start);
        if (!alignToTop)
        {
            var rowHeight = Math.Max(12, _theme.Typography.EditorFontSize *
                _theme.Typography.LineHeightMultiplier);
            _canvas.ScrollBy(-Math.Max(0, _canvas.ViewportHeight - rowHeight),
                preserveSourceFocus: true);
        }
        ShowDocument();
        if (!EnsureCanvasCaretVisible(range.Start))
            return AccessibleRevealResult.NotVisible;
        ScheduleAnalysis();
        return AccessibleRevealResult.Revealed;
    }

    /// <summary>
    /// Selects one current source interval through canonical navigation without
    /// editing, acquiring focus, or settling native composition. Publication is
    /// synchronous so an accessibility client observes the selected range on return.
    /// </summary>
    public AccessibleSelectionResult TrySelect(AccessibleRange range)
    {
        if (Environment.CurrentManagedThreadId != _uiThreadId)
            return AccessibleSelectionResult.WrongThread;
        if (_disposed || _canvas is null || _canvasShell is null)
            return AccessibleSelectionResult.StaleRange;
        var snapshot = _document.Snapshot;
        if (range.Generation != _canvasGeneration || range.Version != snapshot.Version ||
            range.Start < 0 || range.End < range.Start || range.End > snapshot.Length)
            return AccessibleSelectionResult.StaleRange;
        if (SafeBoundary(snapshot, range.Start, backwards: true) != range.Start ||
            SafeBoundary(snapshot, range.End, backwards: true) != range.End)
            return AccessibleSelectionResult.InvalidBoundary;
        if (_shell.IsTextComposing || _canvasShell.IsCanvasComposing)
            return AccessibleSelectionResult.CompositionBlocked;

        InvalidateFind();
        _navigation.SetSelection(snapshot, range.Start, range.End);
        RevealSelection();
        return AccessibleSelectionResult.Selected;
    }

    /// <summary>
    /// A bounded source slice is necessary for a caret, but not proof that
    /// its glyph falls inside the physical viewport.
    /// </summary>
    private static bool ContainsShapedBoundary(CanvasFrame frame, int sourceOffset)
    {
        foreach (var slice in frame.Slices)
        {
            var end = slice.SourceStart + slice.SourceLength;
            if (sourceOffset >= slice.SourceStart &&
                (sourceOffset < end || sourceOffset == end && !slice.HasHiddenSuffix))
                return true;
        }
        return false;
    }

    private void Find()
    {
        if (!_shell.CommitPendingText()) return;
        var query = _shell.PromptFind();
        if (string.IsNullOrEmpty(query)) return;
        _findQuery = query;
        StartFind(query);
    }

    private void FindNext()
    {
        if (!_shell.CommitPendingText()) return;
        if (_findQuery is null) { Find(); return; }
        StartFind(_findQuery);
    }

    private void StartFind(string query)
    {
        InvalidateFind();
        var cancellation = new CancellationTokenSource();
        _findCancellation = cancellation;
        var serial = _findSerial;
        var document = _document;
        var snapshot = document.Snapshot;
        var startAnchor = _navigation.Anchor;
        var startActive = _navigation.Active;
        _operationStatus = "Searching document…";
        ShowDocument();
        _ = Task.Run(() =>
        {
            try
            {
                var result = new NativeNavigationModel();
                result.SetSelection(snapshot, startAnchor, startActive);
                var found = result.FindNext(snapshot, query, wrap: true, cancellation.Token);
                Post(() =>
                {
                    if (_disposed || cancellation.IsCancellationRequested ||
                        serial != _findSerial || !ReferenceEquals(document, _document) ||
                        snapshot.Version != _document.Snapshot.Version) return;
                    if (!SettleInputBeforeAsyncResult())
                    {
                        _operationStatus = "Search cancelled during text composition.";
                        ShowDocument();
                        return;
                    }
                    if (_disposed || cancellation.IsCancellationRequested || serial != _findSerial ||
                        !ReferenceEquals(document, _document) ||
                        snapshot.Version != _document.Snapshot.Version) return;
                    _operationStatus = found ? $"Found at {result.SelectionStart:N0}" :
                        "No match in document";
                    if (found)
                    {
                        _navigation.SetSelection(snapshot, result.Anchor, result.Active);
                        RevealSelection();
                        ProjectSelection();
                    }
                    else ShowDocument();
                });
            }
            catch (OperationCanceledException) { }
        });
    }

    private void GoToLine()
    {
        if (!_shell.CommitPendingText()) return;
        var line = _shell.PromptGoToLine();
        if (line is null) return;
        try
        {
            InvalidateFind();
            _navigation.GoToLine(_document.Snapshot, line.Value);
            RevealSelection();
            ProjectSelection();
        }
        catch (ArgumentOutOfRangeException)
        {
            _shell.ShowError($"Line must be between 1 and {_document.Snapshot.LineCount:N0}.");
        }
    }

    private void SelectAll()
    {
        if (!_shell.CommitPendingText()) return;
        InvalidateFind();
        _navigation.SelectAll(_document.Snapshot);
        ProjectSelection();
    }

    private void Copy() => CopyOrCut(cut: false);

    private void Cut() => CopyOrCut(cut: true);

    private void CopyOrCut(bool cut)
    {
        if (!_shell.CommitPendingText()) return;
        var length = _navigation.SelectionLength;
        if (length == 0) return;
        var serial = ++_clipboardSerial;
        var start = _navigation.SelectionStart;
        var anchor = _navigation.Anchor;
        var active = _navigation.Active;
        var document = _document;
        var snapshot = document.Snapshot;
        _operationStatus = cut ? "Preparing cut…" : "Preparing copy…";
        ShowDocument();
        _ = Task.Run(() =>
        {
            string? selected = null;
            Exception? error = null;
            try { selected = snapshot.GetText(start, length); }
            catch (Exception ex) { error = ex; }
            Post(() =>
            {
                if (_disposed || serial != _clipboardSerial) return;
                if (cut && (!ReferenceEquals(document, _document) ||
                    snapshot.Version != _document.Snapshot.Version ||
                    anchor != _navigation.Anchor || active != _navigation.Active))
                {
                    _operationStatus = "Cut cancelled after a newer edit or selection change.";
                    ShowDocument();
                    return;
                }
                if (cut && !SettleInputBeforeAsyncResult())
                {
                    _operationStatus = "Cut cancelled during text composition.";
                    ShowDocument();
                    return;
                }
                _operationStatus = "";
                if (error is not null || selected is null)
                {
                    _shell.ShowError("The selection could not be copied; the document was not changed.");
                    ShowDocument();
                    return;
                }
                if (cut && (!ReferenceEquals(document, _document) ||
                    snapshot.Version != _document.Snapshot.Version ||
                    anchor != _navigation.Anchor || active != _navigation.Active))
                {
                    _operationStatus = "Cut cancelled after a newer edit or selection change.";
                    ShowDocument();
                    return;
                }
                if (selected.Contains('\0'))
                {
                    _shell.ShowError("Copy refused: the selection contains U+0000; the clipboard and document were not changed.");
                    ShowDocument();
                    return;
                }
                try { _shell.SetClipboardText(selected); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _shell.ShowError("The system clipboard rejected the selection; the document was not changed.");
                    ShowDocument();
                    return;
                }
                if (cut)
                {
                    InvalidateFind();
                    var mark = MoteTelemetry.Mark();
                    try { ApplyTraced(new TextChange(start, length, ""), mark); }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                    {
                        _shell.ShowError($"Cut could not safely change the selection: {ex.Message}");
                        ShowDocument();
                        return;
                    }
                    _navigation.MoveCaret(_document.Snapshot, start);
                    TraceSourceDraw(MoteTelemetry.Fork(mark));
                    _pageStart = Math.Max(0, start - 1024);
                    _pageLength = 0;
                    _requestedCaretSource = start;
                    ShowDocument(mark);
                    ProjectSelection();
                    ScheduleAnalysis(mark);
                }
                else ShowDocument();
            });
        });
    }

    private void RevealSelection()
    {
        if (_sourceShell is not null) { ProjectSourceSelection(reveal: true); return; }
        if (_canvas is { } canvas)
        {
            var frame = canvas.Frame();
            var activeSource = _navigation.Active;
            // Visible rows may contain unpainted gaps inside an exceptionally
            // long line. The first/last slice envelope is not a caret oracle.
            if (!ContainsShapedBoundary(frame, activeSource)) canvas.Reveal(activeSource);
            ShowDocument();
            EnsureCanvasCaretVisible(activeSource);
            ScheduleAnalysis();
            return;
        }
        var caret = _navigation.Active;
        if (caret >= _pageStart && caret <= _pageStart + _pageLength &&
            _navigation.SelectionStart >= _pageStart) return;
        _pageStart = Math.Max(0, _navigation.SelectionStart - 1024);
        _pageLength = 0;
        _requestedCaretSource = caret;
        ShowDocument();
        ScheduleAnalysis();
    }

    /// <summary>
    /// Requires exact-version OS glyph geometry before claiming a caret is
    /// visible. If it is shaped but horizontally clipped, one source-anchored
    /// rebase is allowed; unknown geometry never becomes false success.
    /// </summary>
    private bool EnsureCanvasCaretVisible(int sourceOffset)
    {
        if (_canvas is null || _canvasShell is null ||
            _lastCanvasFrame is not { } frame) return false;
        var proof = _canvasShell.GetCanvasCaretGeometry(frame, sourceOffset);
        if (proof is { IsVisible: true }) return true;
        if (proof is null || _canvasShell.IsCanvasComposing) return false;
        _canvas.AnchorHorizontalAtSource(sourceOffset);
        ShowDocument();
        return _lastCanvasFrame is { } rebased &&
            _canvasShell.GetCanvasCaretGeometry(rebased, sourceOffset) is { IsVisible: true };
    }

    private void ProjectSelectionOrMoveToPage()
    {
        if (_sourceShell is not null) { ProjectSourceSelection(reveal: false); return; }
        if (_canvas is not null) { ShowDocument(); return; }
        if (_projection is null) return;
        if (_navigation.Project(_pageStart, _projection) is null)
        {
            // An off-page selection remains global; only the native caret is parked
            // at this page. Copy/Find continue to use canonical engine coordinates.
            _projectingSelection = true;
            try { _shell.SetSelection(0, 0); }
            finally { _projectingSelection = false; }
            _nativeProjectsGlobalSelection = false;
        }
        else ProjectSelection();
    }

    private void ProjectSelection()
    {
        if (_sourceShell is not null) { ProjectSourceSelection(reveal: false); return; }
        if (_canvas is not null) { ShowDocument(); return; }
        if (_projection is null) return;
        if (_navigation.Project(_pageStart, _projection) is not { } selection) return;
        _projectingSelection = true;
        try { _shell.SetSelection(selection.Anchor, selection.Active); }
        finally { _projectingSelection = false; }
        _nativeProjectsGlobalSelection = _navigation.SelectionLength > 0;
    }

    private void ShowDocument(TelemetryMark traceParent = default)
    {
        using var layout = MoteTelemetry.StartChild(TelemetryOperation.ViewLayout,
            traceParent, Dimensions(_document.Snapshot));
        layout?.SetStatus(TelemetryStatus.Failure);
        var snapshot = _document.Snapshot;
        var file = _document.FilePath is { } path ? Path.GetFileName(path) : "Untitled";
        var title = $"{file}{(_document.IsModified ? " •" : "")} — mote";
        UpdateStatusNotice(skipInitialEmpty: true);
        if (_sourceShell is not null)
        {
            ShowSourceDocument(snapshot, title);
            layout?.SetStatus(_sourceUnavailable ? TelemetryStatus.Failure : TelemetryStatus.Success);
            return;
        }
        if (_canvasShell is not null)
        {
            ShowCanvasDocument(snapshot, title);
            layout?.SetStatus(TelemetryStatus.Success);
            return;
        }
        _pageStart = Math.Min(_pageStart, snapshot.Length);
        _pageStart = SafeBoundary(snapshot, _pageStart, backwards: true);
        var targetLength = _pageLength > 0 ? _pageLength : PageSize;
        var end = SafeBoundary(snapshot, Math.Min(snapshot.Length, _pageStart + targetLength), backwards: false);
        _pageLength = end - _pageStart;
        _projection = new NativeTextProjection(snapshot.GetText(_pageStart, _pageLength), _shell.LineEndingMode);
        int? focus = _requestedCaretSource is { } caret && caret >= _pageStart &&
            caret <= _pageStart + _pageLength
            ? _projection.ToDisplay(caret - _pageStart) : null;
        _requestedCaretSource = null;
        var pageStatus = snapshot.Length <= PageSize ? "" :
            $"Page {_pageStart:N0}–{_pageStart + _pageLength:N0} / {snapshot.Length:N0}; page navigation is discrete";
        _shell.SetDocument(new NativeDocumentView(title, _projection.Display, _pageStart,
            snapshot.Length, _document.IsModified, pageStatus,
            new NativeDocumentStamp(_canvasGeneration, snapshot.Version), focus));
        layout?.SetStatus(TelemetryStatus.Success);
    }

    /// <summary>
    /// Rebinds only when source version or caret-local input window changes.
    /// Scrolling and status updates paint a new source-backed frame without
    /// replacing the native input host's text or disturbing composition.
    /// </summary>
    private void ShowCanvasDocument(TextSnapshot snapshot, string title)
    {
        var canvas = _canvas!;
        var shell = _canvasShell!;
        var frame = canvas.Frame();
        _lastCanvasFrame = frame;
        _accessibleDocument!.Publish(new AccessibleCanvasState(
            _canvasGeneration, snapshot, frame));
        var firstVisible = frame.Slices.Count == 0
            ? Math.Clamp(frame.TopAnchor.SourceOffset, 0, snapshot.Length)
            : frame.Slices[0].SourceStart;
        _pageStart = SafeBoundary(snapshot, firstVisible, backwards: true);
        var visibleEnd = frame.Slices.Count == 0 ? _pageStart :
            frame.Slices[^1].SourceStart + frame.Slices[^1].SourceLength;
        // Two visible slices can be separated by a 50 MiB logical line. The
        // analysis/preview bridge is still contiguous, so bound that bridge
        // independently of the source-backed canvas and never mirror the gap.
        var bridgeEnd = SafeBoundary(snapshot,
            (int)Math.Min(visibleEnd, (long)_pageStart + PageSize), backwards: true);
        _pageLength = Math.Max(0, bridgeEnd - _pageStart);
        _projection = new NativeTextProjection(snapshot.GetText(_pageStart, _pageLength),
            _shell.LineEndingMode);
        _requestedCaretSource = null;
        var line = snapshot.GetLineIndexFromOffset(frame.TopAnchor.SourceOffset) + 1;
        var presentation = _productProfile == EditorPresentationProfile.Continuous
            ? "continuous canvas" : "continuous canvas (experimental)";
        var status = $"Line {line:N0} / {snapshot.LineCount:N0} · {presentation}";
        var needsBinding = _canvasBoundGeneration != _canvasGeneration ||
            _canvasBoundVersion != snapshot.Version ||
            (_navigation.Active != _canvasBoundActive &&
             (_navigation.Active < _canvasInputStart || _navigation.Active > _canvasInputEnd));
        if (needsBinding)
        {
            CanvasInputWindow input;
            try
            {
                input = CanvasInputWindowSelector.Select(snapshot, _navigation.Active,
                    shell.MaxCanvasInputLength);
            }
            catch (CanvasInputWindowBoundaryException)
            {
                // Do not offer a half-grapheme context to the OS input method.
                // The canvas remains readable; moving the caret can retry.
                _canvasInputStart = _canvasInputEnd = _navigation.Active;
                _canvasBoundActive = _navigation.Active;
                _canvasBoundGeneration = _canvasGeneration;
                _canvasBoundVersion = snapshot.Version;
                ++_canvasBindingNonce;
                shell.SetCanvasInputUnavailable(snapshot, frame,
                    "No safe bounded text-input window at this caret.");
                shell.SetCanvasSemantics(new NativeCanvasSemantics(snapshot.Version,
                    AnalysisCompleteness.Provisional,
                    new Mote.Formats.TextSpan(_pageStart, _pageLength), [], []));
                shell.SetCanvasChrome(title, status + " · Input unavailable at this caret",
                    _document.IsModified);
                return;
            }
            _canvasInputStart = input.SourceStart;
            _canvasInputEnd = input.SourceEnd;
            _canvasBoundActive = _navigation.Active;
            _canvasBoundGeneration = _canvasGeneration;
            _canvasBoundVersion = snapshot.Version;
            var nonce = ++_canvasBindingNonce;
            shell.SetCanvasBinding(new NativeCanvasBinding(_canvasGeneration,
                snapshot.Version, nonce, snapshot, frame, input.SourceStart,
                input.SourceText, _navigation.Anchor, _navigation.Active,
                title, status, _document.IsModified));
            shell.SetCanvasSemantics(new NativeCanvasSemantics(snapshot.Version,
                AnalysisCompleteness.Provisional,
                new Mote.Formats.TextSpan(_pageStart, _pageLength), [], []));
        }
        else shell.SetCanvasFrame(frame);
        shell.SetCanvasChrome(title, status, _document.IsModified);
    }

    private static int SafeBoundary(TextSnapshot snapshot, int offset, bool backwards)
    {
        if (offset <= 0 || offset >= snapshot.Length) return offset;
        var pair = snapshot.GetText(offset - 1, 2);
        if (char.IsHighSurrogate(pair[0]) && char.IsLowSurrogate(pair[1]))
            return backwards ? offset - 1 : offset + 1;
        if (pair[0] == '\r' && pair[1] == '\n')
            return backwards ? offset - 1 : offset + 1;
        return offset;
    }

    private void ScheduleAnalysis(TelemetryMark editMark = default, bool gridViewport = false)
    {
        PublishSourceSemantics(_document.Snapshot.Version, AnalysisCompleteness.Provisional,
            new Mote.Formats.TextSpan(0, 0), [], []);
        var hadPreview = _presentedPreview is not null || _policy.Kind == DocumentKind.Csv && _gridFrame is not null;
        CancelAnalysis();
        _presentationMark = editMark;
        _presentationDimensions = Dimensions(_document.Snapshot);
        if (_policy.Kind != DocumentKind.Csv) _idleFullAnalysis?.Cancel();
        _visibleSessionAnalysis = null;
        var cancellation = new CancellationTokenSource();
        _analysisCancellation = cancellation;
        var serial = ++_analysisSerial;
        var document = _document;
        var snapshot = document.Snapshot;
        var policy = _policy;
        var pageStart = _pageStart;
        var pageLength = _pageLength;
        if (!hadPreview)
        {
            PresentAnalysis(new NativeAnalysisView([], "Analysis pending; global diagnostics unknown.",
                "Preparing preview…", $"{policy.DisplayName} · analyzing",
                new NativeDocumentStamp(_canvasGeneration, snapshot.Version)));
            // Pane layout can synchronously resize the source canvas and enqueue
            // a more accurate viewport request. Do not launch this superseded one.
            if (serial != _analysisSerial || cancellation.IsCancellationRequested) return;
        }
        if (_sessionDriver is { } driver)
        {
            ScheduleSessionAnalysis(driver, document, snapshot, policy,
                pageStart, pageLength, cancellation, serial, editMark, gridViewport);
            return;
        }
        var full = snapshot.Length <= FullAnalysisLimit;
        if (!full && policy.Kind is not (DocumentKind.PlainText or DocumentKind.Markdown or DocumentKind.Csv))
        {
            PresentAnalysis(new NativeAnalysisView([], "Global diagnostics deferred for large files.",
                "Structure preview requires complete semantic analysis.",
                "Large file: editable viewport; semantic analysis deferred",
                new NativeDocumentStamp(_canvasGeneration, snapshot.Version)));
            FinishEditPresentation(TelemetryStatus.Skipped);
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(80, cancellation.Token).ConfigureAwait(false);
                var text = full ? snapshot.GetText() : snapshot.GetText(pageStart, pageLength);
                var analysis = AnalyzeTraced(policy, text, cancellation.Token, editMark, snapshot);
                cancellation.Token.ThrowIfCancellationRequested();
                PostAnalysis(serial, () =>
                {
                    if (_disposed || cancellation.IsCancellationRequested ||
                        serial != _analysisSerial || !ReferenceEquals(document, _document) ||
                        snapshot.Version != _document.Snapshot.Version || pageStart != _pageStart)
                    {
                        MoteTelemetry.Record(TelemetryEvent.AnalysisDiscarded);
                        return;
                    }
                    using var present = MoteTelemetry.StartChild(TelemetryOperation.AnalysisToPresentation,
                        editMark, Dimensions(snapshot));
                    present?.SetStatus(TelemetryStatus.Failure);
                    var visible = ProjectTokens(analysis.Tokens, full ? pageStart : 0,
                        pageLength, _projection!);
                    var diagnostics = full
                        ? DiagnosticSummary(analysis.Diagnostics)
                        : "Partial viewport analysis only; global diagnostics unavailable.";
                    var preview = NativePreviewBuilder.Build(analysis, policy.Kind,
                        full ? 0 : pageStart, full);
                    PresentAnalysis(new NativeAnalysisView(visible, diagnostics, preview.Text,
                        full ? $"{policy.DisplayName} semantic analysis · v{snapshot.Version}"
                             : $"{policy.DisplayName} sample · partial",
                        new NativeDocumentStamp(_canvasGeneration, snapshot.Version), preview.Spans));
                    PublishSourceSemantics(snapshot.Version,
                        full ? AnalysisCompleteness.Complete : AnalysisCompleteness.Provisional,
                        new Mote.Formats.TextSpan(full ? 0 : pageStart, full ? snapshot.Length : pageLength),
                        full ? analysis.Tokens : analysis.Tokens.Select(t => t with
                        { Span = new Mote.Formats.TextSpan(t.Span.Start + pageStart, t.Span.Length) }).ToArray(),
                        full ? analysis.Diagnostics : analysis.Diagnostics.Select(d => d with
                        { Span = new Mote.Formats.TextSpan(d.Span.Start + pageStart, d.Span.Length) }).ToArray());
                    MoteTelemetry.Record(TelemetryEvent.AnalysisPublished,
                        dimensions: Dimensions(snapshot));
                    if (serial == _analysisSerial) FinishEditPresentation(TelemetryStatus.Success);
                    present?.SetStatus(TelemetryStatus.Success);
                });
            }
            catch (OperationCanceledException)
            {
                Post(() => { if (serial == _analysisSerial) FinishEditPresentation(TelemetryStatus.Cancelled); });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Post(() =>
                {
                    if (!_disposed && serial == _analysisSerial)
                    {
                        FinishEditPresentation(TelemetryStatus.Failure);
                        PresentAnalysis(new NativeAnalysisView([], "Analysis failed.", "",
                            $"{policy.DisplayName}: {ex.Message}",
                            new NativeDocumentStamp(_canvasGeneration, snapshot.Version)));
                    }
                });
            }
        });
    }

    private void ScheduleSessionAnalysis(NativeFormatSessionDriver driver,
        Document document, TextSnapshot snapshot, IDocumentPolicy policy,
        int pageStart, int pageLength, CancellationTokenSource cancellation,
        long serial, TelemetryMark editMark, bool gridViewport)
    {
        var scope = snapshot.Length <= FullAnalysisLimit ? AnalysisScope.Full : AnalysisScope.Visible;
        var request = new AnalysisRequest(new Mote.Formats.TextSpan(pageStart, pageLength), scope);
        var gridRequest = policy.Kind == DocumentKind.Csv && PreviewVisible()
            ? CreateGridRequest(snapshot, request) : null;
        var csv = policy.Kind == DocumentKind.Csv;
        var content = csv && _csvContentVersion != snapshot.Version;
        if (content)
        {
            _csvContentVersion = snapshot.Version;
            _csvContentCancellation?.Cancel();
            _csvContentCancellation?.Dispose();
            _csvContentCancellation = new CancellationTokenSource();
        }
        var workToken = content ? _csvContentCancellation!.Token : cancellation.Token;
        var dispatcher = _analysisDispatcher;
        // Register mandatory content before returning to native dispatch. A
        // demand Full must not overtake an edit merely because Task.Run started later.
        var csvTurn = csv && dispatcher is not null ? dispatcher.AnalyzeAsync(snapshot, request,
            workToken, gridRequest, content, content ? 80 : 0, _gridVisibleRows) : null;
        _ = Task.Run(async () =>
        {
            try
            {
                // JSON Visible checks the whole source to certify global semantics.
                // A longer debounce on huge files coalesces typing rather than
                // repeatedly starting a multi-second scan after each keystroke.
                var delay = policy.Kind == DocumentKind.Json ? snapshot.Length switch
                {
                    > 32 * 1024 * 1024 => 500,
                    > 8 * 1024 * 1024 => 200,
                    _ => 80
                } : 80;
                NativeFormatPresentation presentation;
                if (csvTurn is not null)
                    presentation = await AnalyzeSessionTraced(driver, snapshot, request,
                        workToken, editMark, gridRequest, csvTurn).ConfigureAwait(false);
                else
                {
                    await Task.Delay(delay, cancellation.Token).ConfigureAwait(false);
                    presentation = await AnalyzeSessionTraced(driver, snapshot, request,
                        cancellation.Token, editMark, gridRequest).ConfigureAwait(false);
                }
                var result = presentation.Analysis;
                cancellation.Token.ThrowIfCancellationRequested();
                PostAnalysis(serial, () =>
                {
                    if (_disposed || cancellation.IsCancellationRequested ||
                        serial != _analysisSerial || !ReferenceEquals(document, _document) ||
                        !ReferenceEquals(driver, _sessionDriver) ||
                        result.Version != _document.Snapshot.Version || pageStart != _pageStart)
                    {
                        MoteTelemetry.Record(TelemetryEvent.AnalysisDiscarded);
                        return;
                    }
                    using var present = MoteTelemetry.StartChild(
                        TelemetryOperation.AnalysisToPresentation, editMark, Dimensions(snapshot));
                    present?.SetStatus(TelemetryStatus.Failure);
                    var tokens = ProjectTokens(result.Tokens, pageStart, pageLength,
                        _projection!);
                    var diagnostics = SessionDiagnosticSummary(result, pageStart, pageLength);
                    var preview = presentation.Preview;
                    if (presentation.Grid is { } targetGrid && ResolveGridTarget(targetGrid)) return;
                    _gridPending = false;
                    var view = new NativeAnalysisView(tokens, diagnostics,
                        preview.Text, $"{policy.DisplayName} · {result.Completeness} · v{result.Version}",
                        new NativeDocumentStamp(_canvasGeneration, result.Version), preview.Spans,
                        Flow: preview.Flow, Grid: presentation.Grid);
                    var semantics = new NativeCanvasSemantics(result.Version,
                        result.Completeness, result.Coverage,
                        VisibleSourceTokens(result.Tokens, pageStart, pageLength),
                        VisibleSourceDiagnostics(result.Diagnostics, pageStart, pageLength));
                    var frame = MergeCachedTomlKnownError(
                        new VisibleSessionFrame(view, semantics, request.VisibleRange), policy, snapshot);
                    _visibleSessionAnalysis = frame;
                    PresentAnalysis(frame.View);
                    if (serial != _analysisSerial || cancellation.IsCancellationRequested) return;
                    _canvasShell?.SetCanvasSemantics(frame.Semantics);
                    PublishSourceSemantics(result.Version, frame.Semantics.Completeness,
                        result.Coverage, result.Tokens, result.Diagnostics);
                    if (serial != _analysisSerial || cancellation.IsCancellationRequested ||
                        !ReferenceEquals(_visibleSessionAnalysis, frame)) return;
                    MoteTelemetry.Record(TelemetryEvent.AnalysisPublished,
                        dimensions: Dimensions(snapshot));
                    if (serial == _analysisSerial) FinishEditPresentation(TelemetryStatus.Success);
                    var idle = _idleFullAnalysis?.Offer(snapshot, result, request.VisibleRange);
                    if (idle is IdleFullOffer.MemoryLimited or IdleFullOffer.PolicyLimited)
                    {
                        var reason = idle == IdleFullOffer.MemoryLimited
                            ? "memory pressure"
                            : "format work limit";
                        PresentAnalysis(frame.View with
                        {
                            Status = $"{policy.DisplayName} · Full pass deferred: {reason}; " +
                                $"global diagnostics unknown · v{result.Version}"
                        });
                    }
                    present?.SetStatus(TelemetryStatus.Success);
                });
            }
            catch (OperationCanceledException)
            {
                Post(() => { if (serial == _analysisSerial) FinishEditPresentation(TelemetryStatus.Cancelled); });
            }
            catch (ObjectDisposedException)
            {
                Post(() => { if (serial == _analysisSerial) FinishEditPresentation(TelemetryStatus.Cancelled); });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Post(() =>
                {
                    if (!_disposed && serial == _analysisSerial &&
                        ReferenceEquals(driver, _sessionDriver))
                    {
                        FinishEditPresentation(TelemetryStatus.Failure);
                        if (csv && _gridFrame is not null)
                        {
                            GridDeliveryFailed();
                            return;
                        }
                        PresentAnalysis(new NativeAnalysisView([], "Analysis failed.", "",
                            $"{policy.DisplayName}: {ex.Message}",
                            new NativeDocumentStamp(_canvasGeneration, snapshot.Version)));
                    }
                });
            }
        });
    }

    /// <summary>Records actual parser outcome before disposing its causal child span.</summary>
    private static FormatAnalysis AnalyzeTraced(IDocumentPolicy policy, string text,
        CancellationToken token, TelemetryMark mark, TextSnapshot snapshot)
    {
        using var parse = MoteTelemetry.StartChild(TelemetryOperation.AnalysisParse, mark, Dimensions(snapshot));
        try
        {
            var result = policy.Analyze(text, token);
            token.ThrowIfCancellationRequested();
            MoteTelemetry.RecordElapsed(TelemetryOperation.EditToAnalysis, MoteTelemetry.Fork(mark), Dimensions(snapshot));
            return result;
        }
        catch (OperationCanceledException) { parse?.SetStatus(TelemetryStatus.Cancelled); throw; }
        catch { parse?.SetStatus(TelemetryStatus.Failure); throw; }
    }

    /// <summary>Measures the serialized format turn, including its bounded render projection.</summary>
    private static async Task<NativeFormatPresentation> AnalyzeSessionTraced(NativeFormatSessionDriver driver,
        TextSnapshot snapshot, AnalysisRequest request, CancellationToken token, TelemetryMark mark,
        CsvGridRequest? gridRequest = null, Task<NativeFormatPresentation>? admitted = null)
    {
        using var parse = MoteTelemetry.StartChild(TelemetryOperation.AnalysisParse, mark, Dimensions(snapshot));
        try
        {
            var result = await (admitted ?? driver.AnalyzePresentationAsync(snapshot, request, token, gridRequest)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            MoteTelemetry.RecordElapsed(TelemetryOperation.EditToAnalysis, MoteTelemetry.Fork(mark), Dimensions(snapshot));
            return result;
        }
        catch (OperationCanceledException) { parse?.SetStatus(TelemetryStatus.Cancelled); throw; }
        catch (ObjectDisposedException) { parse?.SetStatus(TelemetryStatus.Cancelled); throw; }
        catch { parse?.SetStatus(TelemetryStatus.Failure); throw; }
    }

    /// <summary>Retains truthful failure status when native semantic installation throws on the UI thread.</summary>
    private void PostAnalysis(long serial, Action action) => Post(() =>
    {
        try { action(); }
        catch
        {
            if (serial == _analysisSerial) FinishEditPresentation(TelemetryStatus.Failure);
            if (serial == _analysisSerial && _policy.Kind == DocumentKind.Csv && _gridFrame is not null)
            {
                GridDeliveryFailed();
                return;
            }
            throw;
        }
    });

    /// <summary>
    /// Separates a certified document-wide count from diagnostics actually visible
    /// on the current source page. Full projections may include off-page errors.
    /// </summary>
    private static string SessionDiagnosticSummary(DocumentAnalysis result,
        int pageStart, int pageLength)
    {
        var pageEnd = pageStart + pageLength;
        var shown = result.Diagnostics.Where(d => d.Span.Length == 0
            ? d.Span.Start >= pageStart &&
                (d.Span.Start < pageEnd ||
                 pageEnd == result.Root.Span.End && d.Span.Start == pageEnd)
            : d.Span.Start < pageEnd && d.Span.End > pageStart).ToArray();
        if (result.Completeness != AnalysisCompleteness.Complete)
            return $"{result.Completeness} in {result.Coverage.Start:N0}–" +
                $"{result.Coverage.End:N0}; global diagnostics unknown. " +
                (shown.Length == 0 ? "No diagnostics in displayed viewport." :
                    DiagnosticSummary(shown));
        var count = result.TotalDiagnosticCount ?? result.Diagnostics.Count;
        if (count == 0) return "No diagnostics.";
        if (shown.Length == 0)
            return $"{count:N0} document diagnostics; none in displayed viewport.";
        return $"{count:N0} document diagnostics; shown: {DiagnosticSummary(shown)}";
    }

    private void DocumentChanged(object? sender, DocumentChangedRangeEventArgs change)
    {
        ResetGridInterest();
        _tomlKnownError = null;
        _sessionDriver?.Record(change);
        _navigation.ApplyChange(change.Change, change.After);
        _canvas?.ApplyEdit(change.After, change.Change);
        SourceDocumentChanged(change);
    }

    /// <summary>Creates one source-backed viewport sharing the controller's navigation state.</summary>
    private CanvasInteraction NewCanvas(TextSnapshot snapshot) =>
        new(snapshot, Math.Max(12, _theme.Typography.EditorFontSize *
            _theme.Typography.LineHeightMultiplier), 640,
            maxSliceLength: CanvasInputWindowSelector.MaxLength,
            selection: _navigation);

    private static NativeFormatSessionDriver? CreateSessionDriver(IDocumentPolicy policy) =>
        policy is IIncrementalDocumentPolicy incremental
            ? new NativeFormatSessionDriver(incremental) : null;

    /// <summary>
    /// Binds the optional idle lane to one document/policy identity. Worker results
    /// cross the UI thread only after rechecking that identity and source version.
    /// </summary>
    private NativeIdleFullAnalysis? CreateIdleFullAnalysis(NativeFormatSessionDriver? driver,
        Document document, IDocumentPolicy policy)
    {
        _analysisDispatcher?.Dispose();
        _analysisDispatcher = null;
        if (driver is null) return null;
        _analysisDispatcher = policy.Kind == DocumentKind.Csv ? new NativeAnalysisDispatcher(driver) : null;
        return new NativeIdleFullAnalysis(driver, policy.Kind,
            (_, _, _) => { },
            (snapshot, error) => Post(() =>
            {
                if (!_disposed && ReferenceEquals(driver, _sessionDriver) &&
                    ReferenceEquals(document, _document) &&
                    snapshot.Version == _document.Snapshot.Version)
                {
                    MoteTelemetry.Record(TelemetryEvent.AnalysisDiscarded,
                        dimensions: Dimensions(snapshot), status: TelemetryStatus.Failure);
                    if (policy.Kind == DocumentKind.Csv)
                    {
                        SetGridIndexNotice(error is NativeFullAnalysisDeferredException
                            ? "CSV Full indexing deferred by memory pressure; Retry indexing explicitly."
                            : "CSV Full indexing failed; Retry indexing explicitly; source unchanged.");
                        return;
                    }
                    if (_visibleSessionAnalysis is { } visible)
                        PresentAnalysis(visible.View with
                        {
                            Status = $"{policy.DisplayName} · Full pass failed; " +
                                $"visible diagnostics retained · v{snapshot.Version}"
                        });
                }
            }), publishPresentation: (snapshot, presentation, visibleRange) => Post(() =>
                PublishIdleFullAnalysis(driver, document, policy, snapshot,
                    presentation.Analysis, visibleRange, presentation.Preview)), dispatcher: _analysisDispatcher);
    }

    /// <summary>
    /// Promotes only a certified whole-document result. A policy may correctly
    /// answer a Full request with Provisional coverage, which must not erase the
    /// more relevant visible-page analysis or claim a global problem count. The
    /// narrow TOML ownership witness augments only matching bounded source facts.
    /// </summary>
    private void PublishIdleFullAnalysis(NativeFormatSessionDriver driver, Document document,
        IDocumentPolicy policy, TextSnapshot snapshot, DocumentAnalysis result,
        Mote.Formats.TextSpan visibleRange, NativePreview preview)
    {
        if (_disposed || !ReferenceEquals(driver, _sessionDriver) ||
            !ReferenceEquals(document, _document) || !ReferenceEquals(policy, _policy) ||
            snapshot.Version != _document.Snapshot.Version || result.Version != snapshot.Version ||
            policy.Kind != DocumentKind.Csv &&
                (visibleRange.Start != _pageStart || visibleRange.Length != _pageLength))
        {
            MoteTelemetry.Record(TelemetryEvent.AnalysisDiscarded);
            return;
        }
        if (result.Completeness != AnalysisCompleteness.Complete)
        {
            if (policy.Kind == DocumentKind.Csv)
                SetGridIndexNotice($"CSV Full pass {result.Completeness}; file totals remain unknown; Retry indexing explicitly.");
            RememberTomlKnownError(driver, document, policy, snapshot, result);
            if (TryMergeTomlKnownError(policy, snapshot)) return;
            // The Full request did run, but the policy could not certify the
            // entire file. Keep visible facts and make the finite result clear.
            if (_visibleSessionAnalysis is { } visible)
                PresentAnalysis(visible.View with
                {
                    Status = $"{policy.DisplayName} · Full pass {result.Completeness}; " +
                        $"global diagnostics unknown · v{result.Version}"
                });
            return;
        }

        if (policy.Kind == DocumentKind.Csv && PreviewVisible())
        {
            _gridIndexNotice = null;
            // Idle indexing has committed the same session's coordinate facts.
            // Query the latest bounded interest rather than replacing Grid with Flow.
            ScheduleAnalysis(gridViewport: true);
            return;
        }

        using var present = MoteTelemetry.Start(TelemetryOperation.AnalysisToPresentation,
            Dimensions(snapshot));
        // Scope disposal precedes the outer callback's error handling. Only a
        // fully installed publication may be classified as successful.
        present?.SetStatus(TelemetryStatus.Failure);
        var pageStart = _pageStart;
        var pageLength = _pageLength;
        var projection = _projection;
        var serial = _analysisSerial;
        var identity = new NativePresentationId(new NativeDocumentStamp(_canvasGeneration, result.Version),
            checked(_presentationSequence + 1));
        var tokens = ProjectTokens(result.Tokens, _pageStart, _pageLength, _projection!);
        PresentAnalysis(new NativeAnalysisView(tokens,
            SessionDiagnosticSummary(result, _pageStart, _pageLength),
            preview.Text, $"{policy.DisplayName} · Complete · v{result.Version}",
            new NativeDocumentStamp(_canvasGeneration, result.Version), preview.Spans,
            Flow: preview.Flow));
        if (!StillCurrent())
        {
            Discard();
            return;
        }
        _canvasShell?.SetCanvasSemantics(new NativeCanvasSemantics(result.Version,
            result.Completeness, result.Coverage,
            VisibleSourceTokens(result.Tokens, pageStart, pageLength),
            VisibleSourceDiagnostics(result.Diagnostics, pageStart, pageLength)));
        PublishSourceSemantics(result.Version, result.Completeness, result.Coverage,
            result.Tokens, result.Diagnostics);
        if (!StillCurrent())
        {
            Discard();
            return;
        }
        MoteTelemetry.Record(TelemetryEvent.AnalysisPublished,
            dimensions: Dimensions(snapshot));
        present?.SetStatus(TelemetryStatus.Success);

        /// <summary>Native callbacks may replace even a same-version presentation synchronously.</summary>
        bool StillCurrent() => !_disposed && ReferenceEquals(driver, _sessionDriver) &&
            ReferenceEquals(document, _document) && ReferenceEquals(policy, _policy) &&
            snapshot.Version == _document.Snapshot.Version && _canvasGeneration == identity.Document.Generation &&
            pageStart == _pageStart && pageLength == _pageLength && ReferenceEquals(projection, _projection) &&
            serial == _analysisSerial && _presentedPreview?.Identity == identity;

        /// <summary>Supersession is cancellation, not a successful installation or a thrown failure.</summary>
        void Discard()
        {
            present?.SetStatus(TelemetryStatus.Cancelled);
            MoteTelemetry.Record(TelemetryEvent.AnalysisDiscarded);
        }
    }

    /// <summary>
    /// Adds only the first proved TOML ownership conflict to an exact visible frame.
    /// A partial Full pass supplies no replacement rendering or exact global count.
    /// </summary>
    private bool TryMergeTomlKnownError(IDocumentPolicy policy, TextSnapshot snapshot)
    {
        if (_visibleSessionAnalysis is not { } frame) return false;
        var merged = MergeCachedTomlKnownError(frame, policy, snapshot);
        if (ReferenceEquals(merged, frame)) return false;
        _visibleSessionAnalysis = merged;
        PresentAnalysis(merged.View);
        // Native pane installation can synchronously edit or change the viewport.
        if (!ReferenceEquals(_visibleSessionAnalysis, merged) || _disposed ||
            snapshot.Version != _document.Snapshot.Version) return true;
        _canvasShell?.SetCanvasSemantics(merged.Semantics);
        PublishSourceSemantics(merged.Semantics.Version, merged.Semantics.Completeness,
            merged.Semantics.Coverage, merged.Semantics.Tokens, merged.Semantics.Diagnostics);
        MoteTelemetry.Record(TelemetryEvent.AnalysisPublished, dimensions: Dimensions(snapshot));
        return true;
    }

    /// <summary>Captures only a proved, identity-guarded Full witness, including one outside the current viewport.</summary>
    private void RememberTomlKnownError(NativeFormatSessionDriver driver, Document document,
        IDocumentPolicy policy, TextSnapshot snapshot, DocumentAnalysis result)
    {
        if (policy.Kind != DocumentKind.Toml || result.Completeness != AnalysisCompleteness.Provisional ||
            _visibleSessionAnalysis?.View.Stamp != new NativeDocumentStamp(_canvasGeneration, snapshot.Version)) return;
        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Code == "TOML_OWNERSHIP" &&
            d.Severity == DiagnosticSeverity.Error && d.Span.Start >= 0 && d.Span.Length > 0 &&
            (long)d.Span.Start + d.Span.Length <= snapshot.Length);
        if (diagnostic is not null)
            _tomlKnownError = new TomlKnownError(diagnostic,
                new NativeDocumentStamp(_canvasGeneration, snapshot.Version), document, driver, policy);
    }

    /// <summary>Reprojects one exact-version witness into bounded visible facts without replacing rendering.</summary>
    private VisibleSessionFrame MergeCachedTomlKnownError(VisibleSessionFrame frame,
        IDocumentPolicy policy, TextSnapshot snapshot)
    {
        if (_tomlKnownError is not { } known || policy.Kind != DocumentKind.Toml ||
            !ReferenceEquals(known.Document, _document) || !ReferenceEquals(known.Driver, _sessionDriver) ||
            !ReferenceEquals(known.Policy, policy) || !ReferenceEquals(policy, _policy) ||
            known.Stamp != new NativeDocumentStamp(_canvasGeneration, snapshot.Version) ||
            frame.View.Stamp != known.Stamp || frame.Semantics.Version != snapshot.Version ||
            frame.Viewport != new Mote.Formats.TextSpan(_pageStart, _pageLength)) return frame;
        var witness = known.Diagnostic;
        if (witness.Span.Start >= frame.Viewport.End || witness.Span.End <= frame.Viewport.Start) return frame;
        var diagnostics = frame.Semantics.Diagnostics.ToList();
        if (!diagnostics.Any(d => d.Code == witness.Code && d.Span == witness.Span))
        {
            // Preserve existing bounded visible facts rather than evicting one to make room.
            if (diagnostics.Count == 4096) return frame;
            diagnostics.Add(witness);
            diagnostics.Sort(static (left, right) => left.Span.Start.CompareTo(right.Span.Start));
        }
        var semantics = frame.Semantics with
        {
            Completeness = AnalysisCompleteness.Provisional,
            Diagnostics = diagnostics
        };
        var view = frame.View with
        {
            DiagnosticsSummary = $"Provisional in {semantics.Coverage.Start:N0}–" +
                $"{semantics.Coverage.End:N0}; global diagnostics unknown. {DiagnosticSummary(diagnostics)}",
            Status = $"{policy.DisplayName} · Full pass Provisional; global diagnostics unknown · v{snapshot.Version}"
        };
        return new VisibleSessionFrame(view, semantics, frame.Viewport);
    }

    private void SelectPolicy(IDocumentPolicy policy)
    {
        if (ReferenceEquals(policy, _policy)) return;
        ResetGridInterest();
        ++_formatSerial;
        CancelAnalysis();
        _idleFullAnalysis?.Dispose();
        _visibleSessionAnalysis = null;
        _tomlKnownError = null;
        _sessionDriver?.Dispose();
        _policy = policy;
        _sessionDriver = CreateSessionDriver(policy);
        _idleFullAnalysis = CreateIdleFullAnalysis(_sessionDriver, _document, _policy);
        // Save As can change the format policy without changing source version.
        // Clear old-language facts before the new analyzer publishes, rather
        // than trying to encode policy identity into the input binding stamp.
        var stamp = new NativeDocumentStamp(_canvasGeneration, _document.Snapshot.Version);
        PresentAnalysis(new NativeAnalysisView([], "Format analysis pending; global diagnostics unknown.",
            "", $"{policy.DisplayName} · analyzing", stamp));
        _canvasShell?.SetCanvasSemantics(new NativeCanvasSemantics(stamp.Version,
            AnalysisCompleteness.Provisional, new Mote.Formats.TextSpan(0, 0), [], []));
        PublishSourceSemantics(stamp.Version, AnalysisCompleteness.Provisional,
            new Mote.Formats.TextSpan(0, 0), [], []);
    }

    private static IReadOnlyList<SemanticToken> ProjectTokens(IReadOnlyList<SemanticToken> tokens,
        int analysisStart, int pageLength, NativeTextProjection projection)
    {
        var projected = new List<SemanticToken>(Math.Min(tokens.Count, 4096));
        var pageEnd = analysisStart + pageLength;
        foreach (var token in tokens)
        {
            if (projected.Count >= 4096) break;
            var start = Math.Max(token.Span.Start, analysisStart);
            var end = Math.Min(token.Span.End, pageEnd);
            if (start >= end) continue;
            var displayStart = projection.ToDisplay(start - analysisStart);
            var displayEnd = projection.ToDisplay(end - analysisStart);
            if (displayEnd > displayStart)
                projected.Add(new SemanticToken(token.Kind,
                    new Mote.Formats.TextSpan(displayStart, displayEnd - displayStart)));
        }
        return projected;
    }

    /// <summary>Keeps only bounded absolute semantic spans needed by the canvas.</summary>
    private static IReadOnlyList<SemanticToken> VisibleSourceTokens(
        IReadOnlyList<SemanticToken> tokens, int start, int length)
    {
        var end = start + length;
        var visible = new List<SemanticToken>(Math.Min(tokens.Count, 4096));
        foreach (var token in tokens)
        {
            if (visible.Count == 4096) break;
            var clippedStart = Math.Max(start, token.Span.Start);
            var clippedEnd = Math.Min(end, token.Span.End);
            if (clippedEnd > clippedStart)
                visible.Add(new SemanticToken(token.Kind,
                    new Mote.Formats.TextSpan(clippedStart, clippedEnd - clippedStart)));
        }
        visible.Sort(static (left, right) => left.Span.Start.CompareTo(right.Span.Start));
        return visible;
    }

    /// <summary>Keeps at most one visible-page batch of absolute diagnostic spans.</summary>
    private static IReadOnlyList<Diagnostic> VisibleSourceDiagnostics(
        IReadOnlyList<Diagnostic> diagnostics, int start, int length)
    {
        var end = start + length;
        var visible = new List<Diagnostic>(Math.Min(diagnostics.Count, 4096));
        foreach (var diagnostic in diagnostics)
        {
            if (visible.Count == 4096) break;
            var span = diagnostic.Span;
            if (span.Length == 0 ? span.Start >= start && span.Start <= end :
                span.Start < end && span.End > start)
                visible.Add(diagnostic);
        }
        visible.Sort(static (left, right) => left.Span.Start.CompareTo(right.Span.Start));
        return visible;
    }

    private static string DiagnosticSummary(IReadOnlyList<Diagnostic> diagnostics)
    {
        if (diagnostics.Count == 0) return "No diagnostics.";
        var errors = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
        var warnings = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
        var first = diagnostics[0];
        return $"{errors} errors · {warnings} warnings · {first.Code}: {first.Message}";
    }

    private void CancelAnalysis(bool preservePreview = false)
    {
        FinishEditPresentation(TelemetryStatus.Cancelled);
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        _analysisCancellation = null;
        // A still-painted old preview may remain visible until the next result,
        // but it must not navigate after its source map has been invalidated.
        if (!preservePreview) _presentedPreview = null;
    }

    /// <summary>Arms drawing only for an accepted engine revision, before native installation can draw it.</summary>
    private void TraceSourceDraw(TelemetryMark mark,
        TelemetryOperation operation = TelemetryOperation.EditToDrawSubmission) =>
        _shell.TraceSourceDraw(new NativeDocumentStamp(_canvasGeneration, _document.Snapshot.Version),
            mark, Dimensions(_document.Snapshot), operation);

    /// <summary>Separates synchronous canonical engine mutation from later layout/analysis/drawing.</summary>
    private void ApplyTraced(TextChange change, TelemetryMark mark)
    {
        using var edit = MoteTelemetry.StartChild(TelemetryOperation.DocumentEdit, mark,
            Dimensions(_document.Snapshot));
        edit?.SetStatus(TelemetryStatus.Failure);
        _document.Apply(change);
        edit?.SetStatus(TelemetryStatus.Success);
    }

    /// <summary>Completes each cross-callback semantic parent exactly once, including replacement/close.</summary>
    private void FinishEditPresentation(TelemetryStatus status)
    {
        var mark = _presentationMark;
        _presentationMark = default;
        MoteTelemetry.RecordElapsed(TelemetryOperation.EditToPresentation, mark,
            _presentationDimensions, status);
    }

    /// <summary>Terminates a request-owned open interval once; late I/O cannot finish a newer request.</summary>
    private void FinishOpen(int request, TelemetryStatus status, TelemetryDimensions dimensions = default)
    {
        if (request == 0 || request != _openTraceRequest) return;
        var mark = _openMark;
        _openMark = default;
        _openTraceRequest = 0;
        MoteTelemetry.RecordElapsed(TelemetryOperation.OpenToEditable, mark, dimensions, status);
    }

    /// <summary>Publishes one analysis and retains its exact navigation map.</summary>
    private void PresentAnalysis(NativeAnalysisView view)
    {
        view = view with { PresentationSequence = checked(++_presentationSequence),
            ShowPreview = PreviewVisible() };
        var previous = _presentedPreview;
        _presentedPreview = view;
        if (view.ShowPreview && view.Grid is { } grid)
        {
            _gridFrame = MakeGridFrame(view.Identity, grid);
            view = view with { GridNavigation = _gridFrame };
            _presentedPreview = view;
        }
        // Notices live in the separate persistent channel, not a cached analysis
        // candidate: native palette/composition replay must not restore old notices.
        UpdateStatusNotice(skipInitialEmpty: true);
        try { _shell.SetAnalysis(view); }
        catch
        {
            // Reentrant layout callbacks may already have published another map.
            if (ReferenceEquals(_presentedPreview, view)) _presentedPreview = previous;
            throw;
        }
        if (ReferenceEquals(_presentedPreview, view) && view.GridNavigation is { Ready: not null } frame)
        {
            _gridDeliveredFrame = frame;
            _gridDeliveredView = view;
            _gridDeliveredAnchor = _gridAnchor;
        }
    }

    /// <summary>Applies policy convention after preserving explicit and legacy layout choices.</summary>
    private bool PreviewVisible() => _configuration.PreviewLayout switch
    {
        PreviewLayoutPreference.Split => true,
        PreviewLayoutPreference.SourceOnly => false,
        PreviewLayoutPreference.Auto when _productProfile is null or EditorPresentationProfile.LegacyPage => true,
        PreviewLayoutPreference.Auto => DocumentPresentation.ForPolicy(_policy) ==
            DocumentPresentationDefault.SourceAndPreview,
        _ => throw new InvalidOperationException("Unknown preview layout preference.")
    };

    private void InvalidateFind()
    {
        _findCancellation?.Cancel();
        _findCancellation?.Dispose();
        _findCancellation = null;
        ++_findSerial;
        if (_operationStatus == "Searching document…") _operationStatus = "";
    }

    private void Closing(object? sender, NativeClosingEventArgs e)
    {
        if (!_shell.CommitPendingText()) { e.Cancel = true; return; }
        if (_saving) { e.Cancel = true; return; }
        WarnRetainedRecovery();
        if (_document.IsModified && !_shell.ConfirmDiscard()) e.Cancel = true;
    }

    private void Post(Action action) => TryPost(action);

    private bool TryPost(Action action)
    {
        try { _shell.Post(action); return true; }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException) { return false; }
    }

    /// <summary>
    /// Every asynchronous UI completion that can mutate the engine or current projection
    /// first settles native IME preedit against the still-current document. Callers then
    /// recheck their captured document identity and version before applying the result.
    /// </summary>
    private bool SettleInputBeforeAsyncResult() => !_disposed && _shell.CommitPendingText();

    private static TelemetryDimensions Dimensions(TextSnapshot snapshot) =>
        new(DocumentBytes: snapshot.Length * sizeof(char), Version: snapshot.Version);
}
