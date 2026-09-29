using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Runs process-wide heap measurements without other xUnit collections in flight.</summary>
[CollectionDefinition("YamlResourceIsolation", DisableParallelization = true)]
public sealed class YamlResourceIsolationCollection;

/// <summary>
/// Measures YAML's bounded anchor preflight in an isolated xUnit collection.
/// GC.GetTotalMemory covers the entire process, so concurrent test classes
/// would otherwise contaminate the retained-heap difference.
/// </summary>
[Collection("YamlResourceIsolation")]
public sealed class YamlResourceIsolationTests
{
    /// <summary>Dense anchors remain bounded and never invent an undefined alias.</summary>
    [Fact]
    public void Yaml_many_unique_anchors_preflight_bounded_and_no_false_alias()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var baselineHeap = GC.GetTotalMemory(forceFullCollection: true);
        var source = string.Concat(Enumerable.Range(0, 120_000)
            .Select(index => $"- &a{index:D6} x\n")) + "- *a000000\n";
        Assert.True(source.Length > 1024 * 1024);
        using var document = new Document(source);
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Yaml)).CreateSession();
        var actual = session.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 32), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Provisional, actual.Completeness);
        Assert.Null(actual.TotalDiagnosticCount);
        Assert.Contains(actual.Diagnostics, diagnostic => diagnostic.Code == "yaml.streaming-limit");
        Assert.DoesNotContain(actual.Diagnostics, diagnostic => diagnostic.Code == "yaml.undefined-alias");
        Assert.True(actual.Root.Children.Count < 2049);
        var retainedHeap = GC.GetTotalMemory(forceFullCollection: true);
        Assert.True(retainedHeap - baselineHeap < 64L * 1024 * 1024,
            $"Unexpected retained managed heap after bounded YAML analysis: {retainedHeap - baselineHeap} bytes.");
    }
}
