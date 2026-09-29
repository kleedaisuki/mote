using System.Reflection;
using System.Text;
using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Adversarial key-index checks independent of the session's randomized hash seed.</summary>
public sealed class JsonKeyIndexTests
{
    /// <summary>Equal 64-bit hashes never make distinct decoded keys look like duplicates.</summary>
    [Fact]
    public void Forced_hash_collision_still_compares_decoded_key_text()
    {
        const string source = "\"alpha\" \"beta\" \"alpha\"";
        using var document = new Document(source);
        var table = NewKeyTable(document.Snapshot);
        var add = AddKeyMethod(table);
        const ulong forcedHash = 0x123456789abcdef0;
        Assert.Equal(true, (bool?)add.Invoke(table, [forcedHash, new TextSpan(0, 7)]));
        Assert.Equal(true, (bool?)add.Invoke(table, [forcedHash, new TextSpan(8, 6)]));
        Assert.Equal(false, (bool?)add.Invoke(table, [forcedHash, new TextSpan(15, 7)]));
    }

    /// <summary>A deliberately degenerate bucket chain downgrades rather than doing unbounded work.</summary>
    [Fact]
    public void Forced_collision_chain_respects_safe_budget()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 130; i++) builder.Append('"').Append('k').Append(i.ToString("D3")).Append("\" ");
        using var document = new Document(builder.ToString());
        var table = NewKeyTable(document.Snapshot);
        var add = AddKeyMethod(table);
        for (var i = 0; i < 129; i++)
            Assert.Equal(true, (bool?)add.Invoke(table, [0UL, new TextSpan(i * 7, 6)]));
        Assert.Null((bool?)add.Invoke(table, [0UL, new TextSpan(129 * 7, 6)]));
    }

    /// <summary>A 16 MiB repeated-key document has an exact global count without per-key allocation.</summary>
    [Fact]
    public void Large_repeated_keys_have_exact_count_and_bounded_analyzer_allocations()
    {
        const int keys = 2_796_202; // About 16 MiB of short properties, beyond one full-tree projection.
        var builder = new StringBuilder(keys * 6 + 2);
        builder.Append('{');
        for (var i = 0; i < keys; i++)
        {
            if (i != 0) builder.Append(',');
            builder.Append("\"x\":1");
        }
        builder.Append('}');
        using var document = new Document(builder.ToString());
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Json);
        using var session = policy.CreateSession();
        var request = new AnalysisRequest(new TextSpan(0, 1), AnalysisScope.Full);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = session.Analyze(document.Snapshot, [], request);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(AnalysisCompleteness.Complete, result.Completeness);
        Assert.Equal(keys - 1, result.TotalDiagnosticCount);
        Assert.Empty(result.Diagnostics); // Every duplicate is outside the one-character viewport.
        Assert.True(allocated < 64L * 1024 * 1024,
            $"Repeated-key JSON analysis allocated {allocated} bytes on this thread.");
    }

    /// <summary>Creates the private exact-index implementation without relying on random collisions.</summary>
    private static object NewKeyTable(TextSnapshot snapshot)
    {
        var type = typeof(DocumentPolicies).Assembly.GetType(
            "Mote.Formats.JsonIncrementalSession+Parser+KeyTable", throwOnError: true)!;
        var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, [typeof(TextSnapshot), typeof(CancellationToken)], modifiers: null)!;
        return constructor.Invoke([snapshot, CancellationToken.None]);
    }

    /// <summary>Uses the two-argument test seam that accepts a caller-controlled hash.</summary>
    private static MethodInfo AddKeyMethod(object table) => table.GetType().GetMethod(
        "Add", BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
        [typeof(ulong), typeof(TextSpan)], modifiers: null)!;
}
