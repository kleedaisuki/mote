using System.Text;
using Mote.Engine;
using Mote.Formats;
using Mote.Native;

namespace Mote.Tests;

/// <summary>Independent whole-source binding contracts without native controls or global state.</summary>
public sealed class NativeSourceDiagnosticBindingTests
{
    private const string Mixed = "甲\r\n乙\n丙\r丁😀e\u0301\u202eאב\u202c尾";

    /// <summary>Full source and original selection remain independent of a visible page.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtentAndEverySourceBoundaryRoundTrip(bool crLf)
    {
        using var doc = new Document(Mixed);
        using var binding = New(doc, crLf ? NativeLineEndingMode.CrLf : NativeLineEndingMode.Preserve, 2, Mixed.Length);
        Assert.Same(doc.Snapshot, binding.Snapshot);
        Assert.Equal(new NativeDocumentStamp(7, 0), binding.Stamp);
        Assert.Equal(31, binding.InstallationNonce);
        Assert.Equal(0, binding.SourceStart);
        Assert.Equal(Mixed.Length, binding.SourceLength);
        Assert.Equal(2, binding.Anchor);
        Assert.Equal(Mixed.Length, binding.Active);
        Assert.Equal(Mixed, binding.Projection.Source);
        for (var i = 0; i <= Mixed.Length; i++)
            Assert.Equal(i, binding.Projection.ToSourceBoundary(binding.Projection.ToDisplay(i)));
    }

    /// <summary>Installation mismatch is permanent, even if a later readback matches.</summary>
    [Fact]
    public void FailedCertificateCannotBeRetried()
    {
        using var doc = new Document("abc");
        using var binding = New(doc);
        Assert.False(binding.CertifyInstalled("ab"));
        Assert.False(binding.CertifyInstalled("abc"));
        Assert.Equal(NativeSourceDiagnosticEditOutcome.Unavailable, Reconcile(binding, doc, "xyz").Outcome);
        Assert.Equal("abc", doc.Snapshot.GetText());
    }

    /// <summary>No installed readback means no permission to edit.</summary>
    [Fact]
    public void UncertifiedReadbackCannotMutate()
    {
        using var doc = new Document("abc");
        using var binding = New(doc);
        Assert.Equal(NativeSourceDiagnosticEditOutcome.Unavailable, Reconcile(binding, doc, "xyz").Outcome);
        Assert.False(doc.CanUndo);
    }

    /// <summary>Selection-only echo consumes the callback binding without creating history.</summary>
    [Fact]
    public void NoChangeAdvancesSelectionAndRetiresOldBinding()
    {
        using var doc = new Document("abcdef");
        using var binding = Certified(doc);
        var result = Reconcile(binding, doc, "abcdef", new(2, 3));
        Assert.Equal(NativeSourceDiagnosticEditOutcome.NoChange, result.Outcome);
        Assert.Null(result.Change);
        using var next = Assert.IsType<NativeSourceDiagnosticBinding>(result.Binding);
        Assert.Same(binding.Snapshot, next.Snapshot);
        Assert.Equal(2, next.Anchor);
        Assert.Equal(5, next.Active);
        Assert.Equal(31, next.InstallationNonce);
        Assert.False(doc.CanUndo);
        Assert.Equal(NativeSourceDiagnosticEditOutcome.Stale, Reconcile(binding, doc, "abXdef").Outcome);
        Assert.Equal(NativeSourceDiagnosticEditOutcome.Applied, Reconcile(next, doc, "abXdef").Outcome);
    }

    /// <summary>Unchanged mixed delimiters survive one localized replacement and one undo step.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedUnicodeEditHasOneAtomicHistoryEntry(bool crLf)
    {
        using var doc = new Document(Mixed);
        using var binding = Certified(doc, crLf ? NativeLineEndingMode.CrLf : NativeLineEndingMode.Preserve);
        var notifications = 0;
        doc.Changed += (_, _) => notifications++;
        var display = binding.Projection.Display.Replace("丁", "修改", StringComparison.Ordinal);
        var result = Reconcile(binding, doc, display, new(display.Length, 0));
        Assert.Equal(NativeSourceDiagnosticEditOutcome.Applied, result.Outcome);
        var expected = Mixed.Replace("丁", "修改", StringComparison.Ordinal);
        Assert.Equal(expected, doc.Snapshot.GetText());
        Assert.Equal(1, doc.Snapshot.Version);
        Assert.Equal(1, notifications);
        using var next = Assert.IsType<NativeSourceDiagnosticBinding>(result.Binding);
        Assert.Equal(31, next.InstallationNonce);
        Assert.Equal(expected.Length, next.Active);
        Assert.Equal(display, next.Projection.Display);
        Assert.True(doc.Undo());
        Assert.Equal(Mixed, doc.Snapshot.GetText());
        Assert.False(doc.Undo());
        Assert.True(doc.Redo());
        Assert.Equal(expected, doc.Snapshot.GetText());
        Assert.False(doc.Redo());
    }

    /// <summary>Shared surrogate halves are not valid independent replacement boundaries.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmojiReplacementSharingHighSurrogateIsAtomic(bool crLf)
    {
        using var doc = new Document("甲😀尾");
        using var binding = Certified(doc, crLf ? NativeLineEndingMode.CrLf : NativeLineEndingMode.Preserve);
        var result = Reconcile(binding, doc, "甲😁尾", new(3, 0));
        Assert.Equal(NativeSourceDiagnosticEditOutcome.Applied, result.Outcome);
        Assert.Equal("甲😁尾", doc.Snapshot.GetText());
        Assert.True(doc.Undo());
        Assert.Equal("甲😀尾", doc.Snapshot.GetText());
    }

    /// <summary>Shared low surrogate context must also be included in the atomic replacement.</summary>
    [Fact]
    public void ReplacementSharingLowSurrogateHasCompleteScalarRange()
    {
        using var doc = new Document("甲\U0001F600尾");
        using var binding = Certified(doc);
        var result = Reconcile(binding, doc, "甲\U0001FA00尾", new(3, 0));
        Assert.Equal(NativeSourceDiagnosticEditOutcome.Applied, result.Outcome);
        Assert.Equal(new TextChange(1, 2, "\U0001FA00"), result.Change);
        Assert.Equal("甲\U0001FA00尾", doc.Snapshot.GetText());
    }

    /// <summary>Cancellation after atomic mutation cannot turn a committed edit into apparent cancellation.</summary>
    [Fact]
    public void CancellationFromChangedDoesNotDisguiseCommit()
    {
        using var doc = new Document("abc");
        using var binding = Certified(doc);
        using var cancellation = new CancellationTokenSource();
        doc.Changed += (_, _) => cancellation.Cancel();
        var result = binding.Reconcile(doc, binding.Stamp, binding.InstallationNonce,
            "xyz", new(0, 0), cancellation.Token);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(NativeSourceDiagnosticEditOutcome.Applied, result.Outcome);
        Assert.Equal("xyz", doc.Snapshot.GetText());
        Assert.True(doc.CanUndo);
    }

    /// <summary>Invalid installation identities and out-of-source selections fail construction.</summary>
    [Theory]
    [InlineData(0, 1, 0, 0)]
    [InlineData(1, 0, 0, 0)]
    [InlineData(1, 1, -1, 0)]
    [InlineData(1, 1, 0, 4)]
    public void InvalidConstructionFails(long generation, long nonce, int anchor, int active)
    {
        using var doc = new Document("abc");
        Assert.Throws<ArgumentOutOfRangeException>(() => new NativeSourceDiagnosticBinding(
            doc.Snapshot, generation, nonce, NativeLineEndingMode.Preserve, anchor, active));
    }
    /// <summary>Generation, installation, mutation and snapshot identity independently fence admission.</summary>
    [Theory]
    [InlineData("generation")]
    [InlineData("nonce")]
    [InlineData("version")]
    [InlineData("other-document")]
    public void StaleIdentityCannotMutate(string reason)
    {
        using var doc = new Document("abc");
        using var other = new Document("abc");
        using var binding = Certified(doc);
        var stamp = binding.Stamp;
        var nonce = binding.InstallationNonce;
        if (reason == "generation") stamp = new(8, stamp.Version);
        if (reason == "nonce") nonce++;
        if (reason == "version") doc.Apply(new(0, 0, "!"));
        var target = reason == "other-document" ? other : doc;
        var original = target.Snapshot;
        var result = binding.Reconcile(target, stamp, nonce, "xyz", new(0, 0));
        Assert.Equal(NativeSourceDiagnosticEditOutcome.Stale, result.Outcome);
        Assert.Same(original, target.Snapshot);
        Assert.Null(result.Binding);
        Assert.Null(result.Change);
    }

    /// <summary>Invalid native ranges and embedded NUL refuse mutation rather than truncate input.</summary>
    [Theory]
    [InlineData(-1, 0, "abc")]
    [InlineData(0, -1, "abc")]
    [InlineData(4, 0, "abc")]
    [InlineData(2, int.MaxValue, "abc")]
    [InlineData(0, 0, "a\0bc")]
    public void InvalidReadbackCannotMutate(int start, int length, string final)
    {
        using var doc = new Document("abc");
        using var binding = Certified(doc);
        Assert.Equal(NativeSourceDiagnosticEditOutcome.Unavailable,
            Reconcile(binding, doc, final, new(start, length)).Outcome);
        Assert.Equal("abc", doc.Snapshot.GetText());
        Assert.False(doc.CanUndo);
    }

    /// <summary>A native caret inside a synthesized CRLF cannot claim an exact canonical boundary.</summary>
    [Fact]
    public void SynthesizedDelimiterInteriorRefusesSelection()
    {
        using var doc = new Document("a\nb");
        using var binding = Certified(doc, NativeLineEndingMode.CrLf);
        Assert.Equal("a\r\nb", binding.Projection.Display);
        Assert.Equal(NativeSourceDiagnosticEditOutcome.Unavailable,
            Reconcile(binding, doc, "a\r\nb", new(2, 0)).Outcome);
        Assert.False(doc.CanUndo);
    }

    /// <summary>Disposal revokes the native certificate but not the engine-owned document.</summary>
    [Fact]
    public void DisposalDoesNotDisposeDocument()
    {
        using var doc = new Document("abc");
        var binding = Certified(doc);
        binding.Dispose();
        binding.Dispose();
        Assert.False(binding.CertifyInstalled("abc"));
        Assert.Equal(NativeSourceDiagnosticEditOutcome.Unavailable, Reconcile(binding, doc, "xyz").Outcome);
        doc.Apply(new(0, 0, "!"));
        Assert.Equal("!abc", doc.Snapshot.GetText());
    }

    /// <summary>Already cancelled reconciliation does no work and leaves the certificate usable.</summary>
    [Fact]
    public void CancellationBeforeWorkLeavesNoCommit()
    {
        using var doc = new Document("abc");
        using var binding = Certified(doc);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => binding.Reconcile(doc, binding.Stamp,
            binding.InstallationNonce, "xyz", new(0, 0), cancelled.Token));
        Assert.Equal("abc", doc.Snapshot.GetText());
        Assert.False(doc.CanUndo);
        Assert.Equal(NativeSourceDiagnosticEditOutcome.Applied, Reconcile(binding, doc, "xyz").Outcome);
    }

    /// <summary>Unsupported NUL sources are explicit, not silently shortened.</summary>
    [Fact]
    public void EmbeddedNulSourceIsUnsupported()
    {
        using var doc = new Document("a\0b");
        Assert.Throws<NotSupportedException>(() => New(doc));
    }

    /// <summary>Canonical edits survive engine save and a fresh open with exact UTF-8 bytes.</summary>
    [Fact]
    public async Task EditUndoRedoSaveFreshOpenPreservesExactBytes()
    {
        var root = FindRepository();
        var folder = Path.Combine(root, ".temp", "native-source-binding", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "mixed.txt");
        var utf8 = new UTF8Encoding(false, true);
        await File.WriteAllBytesAsync(path, utf8.GetBytes(Mixed));
        using var doc = await Document.OpenAsync(path);
        using var binding = Certified(doc, NativeLineEndingMode.CrLf);
        var display = binding.Projection.Display.Replace("乙", "正文", StringComparison.Ordinal);
        Assert.Equal(NativeSourceDiagnosticEditOutcome.Applied, Reconcile(binding, doc, display).Outcome);
        Assert.True(doc.Undo());
        Assert.True(doc.Redo());
        await doc.SaveAsync();
        var expected = Mixed.Replace("乙", "正文", StringComparison.Ordinal);
        Assert.Equal(utf8.GetBytes(expected), await File.ReadAllBytesAsync(path));
        using var reopened = await Document.OpenAsync(path);
        Assert.Equal(expected, reopened.Snapshot.GetText());
        Assert.False(reopened.IsModified);
    }

    /// <summary>Fixture byte targets are explicit and distinct from canonical UTF-16 extents.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 3711959)]
    [InlineData(2, 524288)]
    public void OrdinaryFixturesHaveExactByteAndScalarContracts(int index, int targetBytes)
    {
        var fixture = NativeSourceDiagnosticFixtures.Create(index);
        var strictUtf8 = new UTF8Encoding(false, true);
        var bytes = strictUtf8.GetBytes(fixture.Text);
        if (targetBytes != 0) Assert.Equal(targetBytes, bytes.Length);
        Assert.True(bytes.Length > fixture.Text.Length);
        Assert.Equal(fixture.Text, strictUtf8.GetString(bytes));
        Assert.DoesNotContain('\0', fixture.Text);
        Assert.InRange(fixture.EditOffset, 0, fixture.Text.Length - "EDIT_TARGET".Length);
        Assert.Equal("EDIT_TARGET", fixture.Text.Substring(fixture.EditOffset, "EDIT_TARGET".Length));
        if (index == 0)
        {
            Assert.Contains("\r\n", fixture.Text);
            var withoutCrLf = fixture.Text.Replace("\r\n", "", StringComparison.Ordinal);
            Assert.Contains("\n", withoutCrLf);
            Assert.Contains("\r", withoutCrLf);
            Assert.Contains("🧪", fixture.Text);
            Assert.Contains("e\u0301", fixture.Text);
        }
    }

    /// <summary>Dense fixture and controlled insertion are valid JSON by an independent standard parser.</summary>
    [Fact]
    public void DenseJsonRemainsSemanticJsonAfterInsertion()
    {
        var fixture = NativeSourceDiagnosticFixtures.Create(2);
        using var parsed = System.Text.Json.JsonDocument.Parse(fixture.Text);
        Assert.Equal("EDIT_TARGET", parsed.RootElement.GetProperty("edit_target").GetString());
        Assert.True(parsed.RootElement.GetProperty("rows").GetArrayLength() > 1000);
        var edited = fixture.Text.Insert(fixture.EditOffset, NativeSourceDiagnosticFixtures.Insertion);
        using var reparsed = System.Text.Json.JsonDocument.Parse(edited);
        Assert.Equal("新🧪EDIT_TARGET", reparsed.RootElement.GetProperty("edit_target").GetString());
        foreach (var text in new[] { fixture.Text, edited })
        {
            var analysis = fixture.Policy.Analyze(text);
            Assert.DoesNotContain(analysis.Diagnostics, item => item.Severity == DiagnosticSeverity.Error);
            Assert.True(analysis.Tokens.Count > 1000);
        }
    }
    /// <summary>Creates one explicit whole-document installation with fixed test identities.</summary>
    private static NativeSourceDiagnosticBinding New(Document doc,
        NativeLineEndingMode mode = NativeLineEndingMode.Preserve, int anchor = 0, int active = 0)
        => new(doc.Snapshot, 7, 31, mode, anchor, active);

    /// <summary>Obtains permission only through an exact readback of the installed replica.</summary>
    private static NativeSourceDiagnosticBinding Certified(Document doc,
        NativeLineEndingMode mode = NativeLineEndingMode.Preserve)
    {
        var binding = New(doc, mode);
        Assert.True(binding.CertifyInstalled(binding.Projection.Display));
        return binding;
    }

    /// <summary>Supplies the captured identity unchanged unless a test explicitly overrides it.</summary>
    private static NativeSourceDiagnosticEdit Reconcile(NativeSourceDiagnosticBinding binding,
        Document doc, string display, NativeSourceDiagnosticRange selection = default)
        => binding.Reconcile(doc, binding.Stamp, binding.InstallationNonce, display, selection);

    /// <summary>Finds the repository so persistence fixtures never use system temporary storage.</summary>
    private static string FindRepository()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, ".git")))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

