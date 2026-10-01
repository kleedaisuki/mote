using System.Runtime.Versioning;
using Mote.Engine;
using Mote.Themes;

namespace Mote.Native.Mac;

/// <summary>In-memory legacy AppKit source audit for embedded NUL and retained edit tails.</summary>
/// <remarks>
/// Uses a fresh shell, real NSTextView insertion and the production text projection
/// plus an unsaved Engine document. Does not run the controller's Save/reopen path,
/// touch files or pasteboard, request TCC, change input sources, or send external input.
/// </remarks>
[SupportedOSPlatform("macos")]
internal static class MacSourceNulProbe
{
    /// <summary>Runs one isolated legacy source window; target execution is required.</summary>
    internal static int Run()
    {
        try
        {
            var shell = new MacEditorShell(experimentalCanvas: false);
            var passed = false;
            shell.Shown += () => shell.Post(() =>
            {
                try { Check(shell); passed = true; }
                catch (Exception error) when (error is not OutOfMemoryException)
                { Console.Error.WriteLine($"Mac source NUL check failed: {error.Message}"); }
                finally { shell.Close(); }
            });
            shell.Run();
            return passed ? 0 : 1;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Mac source NUL initialization failed: {error.GetType().Name}.");
            return 1;
        }
    }

    /// <summary>Checks exact import, native notification, Engine difference and reversible insertion.</summary>
    private static void Check(MacEditorShell shell)
    {
        var fixtures = new[]
        {
            "a\0b",
            "\"a\0b\",tail\r\n",
            "\0␀\0",
            "␀\0",
            "\0",
            "a\0b😀\r\n␀\0"
        };
        shell.SetTheme(ThemePolicies.Resolve(ThemePolicies.DarkId, true));
        var observed = new List<string>();
        shell.TextChanged += observed.Add;
        for (var index = 0; index < fixtures.Length; index++)
        {
            var source = fixtures[index];
            var nativeString = ObjC.String(source);
            Require(ObjC.Send(nativeString, ObjC.Sel("length")) == source.Length,
                index, "NSString import length");
            Require(string.Equals(ObjC.ManagedString(nativeString), source, StringComparison.Ordinal),
                index, "length-aware NSString bridge roundtrip");
            using var document = new Document(source);
            var projection = new NativeTextProjection(source, shell.LineEndingMode);
            var stamp = new NativeDocumentStamp(index + 1, document.Snapshot.Version);
            shell.SetDocument(new NativeDocumentView("NUL source audit", projection.Display, 0,
                source.Length, false, "", stamp));
            Require(string.Equals(shell.ProbeNativeText, source, StringComparison.Ordinal),
                index, "NSTextView source import/readback");
            Require(ObjC.Send(ObjC.Send(shell.ProbeEditorView, ObjC.Sel("string")),
                ObjC.Sel("length")) == source.Length, index, "NSTextView embedded-NUL length");
            Require(projection.Difference(shell.ProbeNativeText) is null,
                index, "unchanged native text is not an Engine edit");
            observed.Clear();
            shell.ProbeInsertAtStart("x");
            var expected = "x" + source;
            Require(string.Equals(shell.ProbeNativeText, expected, StringComparison.Ordinal),
                index, "ordinary native insertion preserves full original tail");
            Require(observed.Count > 0 && observed.All(value =>
                string.Equals(value, expected, StringComparison.Ordinal)),
                index, "native TextChanged payload retains NUL and full tail");
            var difference = projection.Difference(observed[^1]);
            Require(difference is { Start: 0, DeleteLength: 0, InsertText: "x" },
                index, "production difference is insertion only, not tail deletion");
            document.Apply(difference!.Value);
            Require(string.Equals(document.Snapshot.GetText(), expected, StringComparison.Ordinal),
                index, "unsaved Engine document preserves exact native edit");
            Require(document.Undo() && string.Equals(document.Snapshot.GetText(), source, StringComparison.Ordinal),
                index, "Engine Undo restores exact embedded-NUL source");
            Require(document.Redo() && string.Equals(document.Snapshot.GetText(), expected, StringComparison.Ordinal),
                index, "Engine Redo restores exact embedded-NUL edit");
        }
        Console.WriteLine($"mote-native-mac-source-nul-cases={fixtures.Length}; mode=legacy; coverage=bridge-native-insert-projection-engine-undo-redo; save-reopen=not-tested");
    }

    private static void Require(bool condition, int fixture, string contract)
    {
        if (!condition) throw new InvalidOperationException($"Fixture {fixture}: {contract}.");
    }
}
