using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Mote.Themes;

namespace Mote.Native.Mac;

/// <summary>Creates an isolated, full-resident AppKit source surface, never a product shell.</summary>
[SupportedOSPlatform("macos")]
internal static class MacNativeSourceCapabilityProbe
{
    /// <summary>Creates the sole source painter on the main thread without activating the application.</summary>
    internal static INativeSourceDiagnosticHost Create(IThemePolicy theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return new Host(theme);
    }

    /// <summary>Owns one window, scroll view, text view and autorelease pool in main-thread order.</summary>
    private sealed class Host : INativeSourceDiagnosticHost
    {
        /// <summary>Retained native objects; document views are additionally retained by their containers.</summary>
        private nint _pool, _window, _scroll, _text;
        /// <summary>The main managed owner detects misuse before any Objective-C messaging.</summary>
        private readonly int _owner = Environment.CurrentManagedThreadId;
        /// <summary>The theme remains the default foreground when complete style spans are republished.</summary>
        private readonly ThemeColor _foreground;

        /// <summary>Constructs all objects transactionally; partial initialization is released on failure.</summary>
        internal Host(IThemePolicy theme)
        {
            if (MainThread() == 0) throw new InvalidOperationException("AppKit requires the main thread.");
            if (RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
                throw new PlatformNotSupportedException("The native probe supports macOS x64 and arm64 only.");
            _foreground = theme.Palette.EditorForeground;
            try
            {
                _pool = Required(ObjC.New("NSAutoreleasePool"));
                if (!ObjC.ApplicationLoad()) throw new PlatformNotSupportedException("AppKit initialization failed.");
                _ = Required(ObjC.Send(ObjC.Class("NSApplication"), ObjC.Sel("sharedApplication")));
                _window = Required(ObjC.Send(ObjC.Send(ObjC.Class("NSWindow"), ObjC.Sel("alloc")),
                    ObjC.Sel("initWithContentRect:styleMask:backing:defer:"),
                    new ObjC.Rect(120, 120, 960, 640), 15, 2, 0));
                Flag(_window, "setReleasedWhenClosed:", false);
                ObjC.Send(_window, ObjC.Sel("setTitle:"), ObjC.String("mote native source capability"));
                _scroll = NewView("NSScrollView");
                _text = NewView("NSTextView");
                Configure(theme);
                ObjC.Send(_scroll, ObjC.Sel("setDocumentView:"), _text);
                ObjC.Send(_window, ObjC.Sel("setContentView:"), _scroll);
                // orderFront: shows this window but neither activates nor makes it key.
                ObjC.Send(_window, ObjC.Sel("orderFront:"), (nint)0);
            }
            catch { Dispose(); throw; }
        }

        /// <summary>Reads actual native managers, checking TextKit 2 before the compatibility-triggering legacy accessor.</summary>
        public string LayoutBackend
        {
            get
            {
                Check();
                if (Responds(_text, "textLayoutManager") && ObjC.Send(_text, ObjC.Sel("textLayoutManager")) != 0)
                    return "textkit2";
                if (Responds(_text, "layoutManager") && ObjC.Send(_text, ObjC.Sel("layoutManager")) != 0)
                    return "textkit1";
                return "unknown";
            }
        }

        /// <summary>Imports plain characters; the caller separately certifies exact native readback.</summary>
        public void Install(string display)
        {
            Check();
            ArgumentNullException.ThrowIfNull(display);
            ObjC.Send(_text, ObjC.Sel("setString:"), ObjC.String(display));
        }

        /// <summary>Copies NSString by explicit UTF-16 length, including embedded NUL characters.</summary>
        public string ReadText()
        {
            Check();
            return ObjC.ManagedString(Required(ObjC.Send(_text, ObjC.Sel("string"))));
        }

        /// <summary>Sets a sorted global native range; anchor direction is deliberately not certified.</summary>
        public void SetSelection(string display, int anchor, int active)
        {
            Check();
            ValidateRange(display, Math.Min(anchor, active), Math.Abs((long)active - anchor));
            Exact(display);
            ObjC.Send(_text, ObjC.Sel("setSelectedRange:"),
                new ObjC.Range((nuint)Math.Min(anchor, active), (nuint)Math.Abs((long)active - anchor)));
        }

        /// <summary>Reads native global UTF-16 selection and rejects an out-of-date display replica.</summary>
        public NativeSourceDiagnosticRange ReadSelection(string display)
        {
            Check();
            Exact(display);
            var range = ObjC.SendRange(_text, ObjC.Sel("selectedRange"));
            var start = checked((int)range.Location);
            var length = checked((int)range.Length);
            ValidateRange(display, start, length);
            return new(start, length);
        }

        /// <summary>Replaces the currently selected native range once through NSTextInputClient.</summary>
        public void Insert(string text)
        {
            Check();
            ArgumentNullException.ThrowIfNull(text);
            var before = ReadText();
            var selected = ReadSelection(before);
            ObjC.Send(_text, ObjC.Sel("insertText:replacementRange:"), ObjC.String(text),
                new ObjC.Range((nuint)selected.Start, (nuint)selected.Length));
            Exact(string.Concat(before.AsSpan(0, selected.Start), text,
                before.AsSpan(selected.Start + selected.Length)));
        }

        /// <summary>Returns the owned clip view's current bounds origin, without selecting text.</summary>
        public NativeSourceDiagnosticViewport CaptureViewport()
        {
            Check();
            var origin = Rect(Clip(), "bounds").Origin;
            return new(origin.X, origin.Y);
        }

        /// <summary>Scrolls the final global character into view without changing the real selection.</summary>
        public void ScrollToEnd(string display)
        {
            Check();
            Exact(display);
            var selected = ObjC.SendRange(_text, ObjC.Sel("selectedRange"));
            ObjC.Send(_text, ObjC.Sel("scrollRangeToVisible:"), new ObjC.Range((nuint)display.Length, 0));
            if (ObjC.SendRange(_text, ObjC.Sel("selectedRange")) != selected)
                throw new InvalidOperationException("Native scrolling changed selection.");
        }

        /// <summary>Restores this view's scroll position through NSClipView, never selection commands.</summary>
        public void RestoreViewport(NativeSourceDiagnosticViewport viewport)
        {
            Check();
            if (!double.IsFinite(viewport.Horizontal) || !double.IsFinite(viewport.Vertical))
                throw new ArgumentOutOfRangeException(nameof(viewport));
            var clip = Clip();
            ObjC.Send(clip, ObjC.Sel("scrollToPoint:"), new ObjC.Point(viewport.Horizontal, viewport.Vertical));
            ObjC.Send(_scroll, ObjC.Sel("reflectScrolledClipView:"), clip);
        }

        /// <summary>Batches foreground-only storage mutations; preserves characters, selection and viewport.</summary>
        public void PublishStyles(string display, IReadOnlyList<NativeSourceDiagnosticStyle> styles)
        {
            Check();
            ArgumentNullException.ThrowIfNull(styles);
            Exact(display);
            var gaps = MacNativeForegroundPublication.UncoveredRanges(display.Length, styles);
            var selected = ObjC.SendRange(_text, ObjC.Sel("selectedRange"));
            var viewport = CaptureViewport();
            var storage = Required(ObjC.Send(_text, ObjC.Sel("textStorage")));
            var key = ObjC.String("NSColor");
            var colors = new Dictionary<ThemeColor, nint>();
            ObjC.Send(storage, ObjC.Sel("beginEditing"));
            try
            {
                // Reset only uncovered text. Every covered location receives its
                // original ordered overlay, so a whole reset only destroys runs.
                foreach (var gap in gaps)
                    ApplyForeground(storage, key, NativeColor(_foreground), display.Length, gap.Start, gap.Length);
                foreach (var style in styles)
                    ApplyForeground(storage, key, NativeColor(style.Foreground), display.Length, style.Start, style.Length);
            }
            finally { ObjC.Send(storage, ObjC.Sel("endEditing")); }
            foreach (var position in MacNativeForegroundPublication.SampleLocations(display.Length, styles, gaps))
            {
                var actual = AttributeAt(storage, ObjC.Sel("attribute:atIndex:effectiveRange:"), key,
                    (nuint)position, 0);
                var expected = MacNativeForegroundPublication.ExpectedColor(position, _foreground, styles);
                if (!SameColor(actual, NativeColor(expected)))
                    throw new InvalidOperationException("Native foreground readback differs from the complete style overlay.");
            }
            Exact(display);
            if (ObjC.SendRange(_text, ObjC.Sel("selectedRange")) != selected)
                throw new InvalidOperationException("Attribute publication changed selection.");
            RestoreViewport(viewport);

            // Native storage retains applied values. Borrowed factory values are
            // used only during this synchronous call and its existing owned pool.
            nint NativeColor(ThemeColor color)
            {
                if (!colors.TryGetValue(color, out var value)) colors.Add(color, value = Color(color));
                return value;
            }
        }

        /// <summary>Submits owned native layout and drawing; does not certify compositor presentation.</summary>
        public void FlushDraw()
        {
            Check();
            ObjC.Send(_scroll, ObjC.Sel("layoutSubtreeIfNeeded"));
            ObjC.Send(_window, ObjC.Sel("displayIfNeeded"));
        }

        /// <summary>Closes only the owned window and releases retained views before draining their pool.</summary>
        public void Dispose()
        {
            if (Environment.CurrentManagedThreadId != _owner || MainThread() == 0)
                throw new InvalidOperationException("Native host disposal requires its main-thread owner.");
            if (_window != 0) ObjC.Send(_window, ObjC.Sel("close"));
            Release(ref _window);
            Release(ref _scroll);
            Release(ref _text);
            Release(ref _pool);
        }

        /// <summary>Configures ordinary native editable text with engine-owned undo and no automatic substitutions.</summary>
        private void Configure(IThemePolicy theme)
        {
            Flag(_scroll, "setHasVerticalScroller:", true);
            Flag(_scroll, "setAutohidesScrollers:", true);
            Flag(_text, "setEditable:", true);
            Flag(_text, "setSelectable:", true);
            Flag(_text, "setRichText:", false);
            Flag(_text, "setAllowsUndo:", false);
            Flag(_text, "setVerticallyResizable:", true);
            Flag(_text, "setHorizontallyResizable:", false);
            ObjC.Send(_text, ObjC.Sel("setMinSize:"), new ObjC.Size(0, 640));
            ObjC.Send(_text, ObjC.Sel("setMaxSize:"), new ObjC.Size(1_000_000_000, 1_000_000_000));
            ObjC.Send(_text, ObjC.Sel("setAutoresizingMask:"), (nint)2);
            var container = Required(ObjC.Send(_text, ObjC.Sel("textContainer")));
            ObjC.Send(container, ObjC.Sel("setContainerSize:"), new ObjC.Size(960, 1_000_000_000));
            Flag(container, "setWidthTracksTextView:", true);
            foreach (var selector in new[] { "setAutomaticQuoteSubstitutionEnabled:",
                "setAutomaticDashSubstitutionEnabled:", "setAutomaticTextReplacementEnabled:",
                "setAutomaticSpellingCorrectionEnabled:", "setContinuousSpellCheckingEnabled:" })
                if (Responds(_text, selector)) Flag(_text, selector, false);
            ObjC.Send(_text, ObjC.Sel("setFont:"), Required(ObjC.Send(ObjC.Class("NSFont"),
                ObjC.Sel("monospacedSystemFontOfSize:weight:"), theme.Typography.EditorFontSize, 0d)));
            ObjC.Send(_text, ObjC.Sel("setTextColor:"), Color(_foreground));
            ObjC.Send(_text, ObjC.Sel("setBackgroundColor:"), Color(theme.Palette.EditorBackground));
        }

        /// <summary>Rejects use after release and cross-thread AppKit calls.</summary>
        private void Check()
        {
            if (Environment.CurrentManagedThreadId != _owner || MainThread() == 0)
                throw new InvalidOperationException("Native host requires its main-thread owner.");
            ObjectDisposedException.ThrowIf(_text == 0, this);
        }

        /// <summary>Verifies the exact replica, without newline normalization or guessed offset maps.</summary>
        private void Exact(string display)
        {
            ArgumentNullException.ThrowIfNull(display);
            if (!string.Equals(ReadText(), display, StringComparison.Ordinal))
                throw new InvalidOperationException("Native text differs from the exact display replica.");
        }

        /// <summary>Validates global UTF-16 spans before unsigned conversion.</summary>
        private static void ValidateRange(string display, int start, long length)
        {
            ArgumentNullException.ThrowIfNull(display);
            if (start < 0 || length < 0 || start > display.Length || length > display.Length - start)
                throw new ArgumentOutOfRangeException(nameof(start));
        }

        /// <summary>Returns this scroll view's borrowed clip view.</summary>
        private nint Clip() => Required(ObjC.Send(_scroll, ObjC.Sel("contentView")));
        /// <summary>Allocates one retained view in the fixed probe geometry.</summary>
        private static nint NewView(string name) => Required(ObjC.Send(
            ObjC.Send(ObjC.Class(name), ObjC.Sel("alloc")), ObjC.Sel("initWithFrame:"), new ObjC.Rect(0, 0, 960, 640)));
        /// <summary>Rejects missing native objects rather than silently accepting nil messages.</summary>
        private static nint Required(nint value) => value != 0 ? value : throw new InvalidOperationException("Native object unavailable.");
        /// <summary>Queries selector support before invoking optional platform APIs.</summary>
        private static bool Responds(nint obj, string selector) => SendNativeBool(obj, ObjC.Sel("respondsToSelector:"), ObjC.Sel(selector)) != 0;
        /// <summary>Sets an Objective-C BOOL using its exact one-byte ABI.</summary>
        private static void Flag(nint obj, string selector, bool value) => SendBool(obj, ObjC.Sel(selector), value ? (byte)1 : (byte)0);
        /// <summary>Creates an autoreleased sRGB foreground color in the owned pool.</summary>
        private static nint Color(ThemeColor color) => Required(ObjC.Send(ObjC.Class("NSColor"),
            ObjC.Sel("colorWithSRGBRed:green:blue:alpha:"), color.Red / 255d, color.Green / 255d, color.Blue / 255d, 1d));
        /// <summary>
        /// Uses actual native effective runs, not remembered managed colors, to
        /// skip identical foreground values. Mutation never targets another
        /// attribute, the selected range or characters. Short native runs are
        /// valid and every iteration must make strictly positive progress.
        /// </summary>
        private static unsafe void ApplyForeground(nint storage, nint key, nint color,
            int length, int start, int count)
        {
            var end = start + count;
            var selector = ObjC.Sel("attribute:atIndex:effectiveRange:");
            while (start < end)
            {
                ObjC.Range effective;
                var actual = AttributeAt(storage, selector, key, (nuint)start, (nint)(&effective));
                var next = MacNativeForegroundPublication.EffectiveEnd(length, start, end,
                    effective.Location, effective.Length);
                if (!SameColor(actual, color))
                    ObjC.Send(storage, ObjC.Sel("addAttribute:value:range:"), key, color,
                        new ObjC.Range((nuint)start, (nuint)(next - start)));
                start = next;
            }
        }
        /// <summary>Unknown/missing values differ; native NSColor value equality is not pointer equality.</summary>
        private static bool SameColor(nint actual, nint expected) => actual != 0 &&
            SendNativeBool(actual, ObjC.Sel("isKindOfClass:"), ObjC.Class("NSColor")) != 0 &&
            SendNativeBool(actual, ObjC.Sel("isEqual:"), expected) != 0;
        /// <summary>Releases one owned reference exactly once.</summary>
        private static void Release(ref nint obj)
        {
            if (obj != 0) ObjC.Send(obj, ObjC.Sel("release"));
            obj = 0;
        }
        /// <summary>Reads a 32-byte CGRect using x64 structure-return or arm64 ordinary return ABI.</summary>
        private static ObjC.Rect Rect(nint obj, string selector)
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64) return SendRect(obj, ObjC.Sel(selector));
            SendRectStret(out var rect, obj, ObjC.Sel(selector));
            return rect;
        }
    }

    /// <summary>Checks Darwin's real process main thread, not merely a managed thread identifier.</summary>
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "pthread_main_np")]
    private static extern int MainThread();
    /// <summary>BOOL argument bridge used only for void setters.</summary>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void SendBool(nint obj, nint selector, byte value);
    /// <summary>BOOL result bridge avoids interpreting unspecified upper return-register bits.</summary>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern byte SendNativeBool(nint obj, nint selector, nint value);
    /// <summary>Reads an object attribute using NSUInteger and an optional NSRange pointer on both 64-bit ABIs.</summary>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint AttributeAt(nint obj, nint selector, nint key, nuint position, nint effectiveRange);
    /// <summary>arm64 CGRect return bridge; never invoked on x64.</summary>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern ObjC.Rect SendRect(nint obj, nint selector);
    /// <summary>x64 CGRect structure-return bridge; never invoked on arm64.</summary>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend_stret")]
    private static extern void SendRectStret(out ObjC.Rect rect, nint obj, nint selector);
}
