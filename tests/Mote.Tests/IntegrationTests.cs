using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Exercises the one-file editor workflow across engine and format boundaries.</summary>
public sealed class IntegrationTests
{
    /// <summary>Opening, analyzing, editing, and saving one JSON file stays version-coherent.</summary>
    [Fact]
    public async Task Open_analyze_edit_save_reopen_uses_current_text()
    {
        using var temp = new RepoTemp();
        var path = temp.File("document.json");
        await File.WriteAllTextAsync(path, "{\"answer\":1}\r\n");
        using var document = await Document.OpenAsync(path);
        var policy = DocumentPolicies.ForPath(document.FilePath!);
        Assert.Equal(DocumentKind.Json, policy.Kind);

        var firstVersion = document.Snapshot.Version;
        var before = document.GetOrCompute(policy, snapshot => policy.Analyze(snapshot.GetText()));
        Assert.DoesNotContain(before.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(document.Snapshot.GetText(), before.SourceText);

        var valueOffset = document.Snapshot.GetText().IndexOf('1');
        document.Apply(new TextChange(valueOffset, 1, "[1,2]"));
        Assert.True(document.Snapshot.Version > firstVersion);
        var after = document.GetOrCompute(policy, snapshot => policy.Analyze(snapshot.GetText()));
        Assert.NotSame(before, after);
        Assert.Equal("{\"answer\":[1,2]}\r\n", after.SourceText);
        Assert.Equal("{\"answer\":1}\r\n", before.SourceText);
        Assert.DoesNotContain(after.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        await document.SaveAsync();
        Assert.False(document.IsModified);
        using var reopened = await Document.OpenAsync(path);
        Assert.Equal(after.SourceText, reopened.Snapshot.GetText());
    }

    /// <summary>Malformed content remains editable and gains a clean analysis after correction.</summary>
    [Fact]
    public void Diagnostics_follow_corrected_version_without_stale_cache()
    {
        using var document = new Document("{\"x\":");
        var policy = DocumentPolicies.ForKind(DocumentKind.Json);
        var invalid = document.GetOrCompute(policy, snapshot => policy.Analyze(snapshot.GetText()));
        Assert.Contains(invalid.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        document.Apply(new TextChange(document.Snapshot.Length, 0, "1}"));
        var valid = document.GetOrCompute(policy, snapshot => policy.Analyze(snapshot.GetText()));
        Assert.DoesNotContain(valid.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("{\"x\":1}", valid.SourceText);
    }
}
