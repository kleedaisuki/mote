using System.Text;
using System.Text.Json;
using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>All published TOML 1.1 syntax/ownership fixtures, with exact upstream bytes.</summary>
public sealed class TomlConformanceTests
{
    /// <summary>Loads the pinned manifest without depending on checkout or process working directory.</summary>
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(TomlConformanceTests).Assembly.GetManifestResourceStream("Mote.Tests.TomlConformance")!;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            using var record = JsonDocument.Parse(line);
            var root = record.RootElement;
            yield return [root.GetProperty("name").GetString()!, root.GetProperty("valid").GetBoolean(),
                root.GetProperty("bytes").GetString()!];
        }
    }

    /// <summary>Complete means valid; invalid byte encoding is a separately tested input boundary.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Published_source_has_normative_validity(string name, bool valid, string encoded)
    {
        var bytes = Convert.FromBase64String(encoded);
        string source;
        try { source = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException)
        {
            Assert.False(valid, name);
            return;
        }
        using var document = new Document(source);
        var analysis = TomlIncrementalSession.AnalyzeLarge(document.Snapshot,
            new TextSpan(0, source.Length), CancellationToken.None);
        Assert.Equal(valid, analysis.Completeness == AnalysisCompleteness.Complete);
        Assert.Equal(valid ? 0 : (int?)null, analysis.TotalDiagnosticCount);
        Assert.Equal(document.Snapshot.Version, analysis.Version);
    }

    /// <summary>Non-TOML whitespace and bare CR must not bypass validation on the actual large path.</summary>
    [Theory]
    [InlineData("\f")]
    [InlineData("\v")]
    [InlineData("\r")]
    [InlineData("# invalid control \r")]
    [InlineData("# invalid control \u007f\n")]
    [InlineData("\u00a0")]
    public void Large_dispatcher_does_not_certify_invalid_trivia(string bad)
    {
        var ballast = string.Concat(Enumerable.Repeat("#" + new string('x', 4094) + "\n", 1025));
        using var document = new Document(ballast + bad);
        using var session = new TomlPolicy().CreateSession();
        var analysis = session.Analyze(document.Snapshot, [], new(new TextSpan(0, 20), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Provisional, analysis.Completeness);
        Assert.Null(analysis.TotalDiagnosticCount);
        Assert.Empty(analysis.Diagnostics);
    }
}
