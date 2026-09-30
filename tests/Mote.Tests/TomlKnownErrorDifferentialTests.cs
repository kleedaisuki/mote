using System.Text;
using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Black-box counterexample and refusal tests for the bounded large-TOML contract.</summary>
public sealed class TomlKnownErrorDifferentialTests
{
    private static readonly string Padding = string.Concat(Enumerable.Repeat("#" + new string('x', 4094) + "\n", 1025));

    /// <summary>Expected positions are literal source offsets, not ownership-index output.</summary>
    [Theory]
    [InlineData("duplicate", "a=1\n", "a=2\n", 1)]
    [InlineData("escaped", "a=1\n", "\"\\u0061\"=2\n", 8)]
    [InlineData("scalar", "a=1\n", "a.b=2\n", 3)]
    [InlineData("inline", "a={b=1}\n", "a.c=2\n", 3)]
    [InlineData("table", "[a]\n", "[a]\n", 1)]
    [InlineData("dotted", "a.b=1\n", "[a]\n", 1)]
    [InlineData("multiline", "a=\"\"\"hello\nworld\"\"\"\n", "a=2\n", 1)]
    [InlineData("eof", "a=1\n", "a=2", 1)]
    public void Certified_conflict_has_one_exact_current_version_witness(string name, string prefix, string suffix, int keyLength)
    {
        var source = prefix + Padding + suffix;
        Export(name, source);
        using var document = new Document(source);
        using var session = new TomlPolicy().CreateSession();
        var visible = new TextSpan(source.Length - suffix.Length, suffix.Length);
        var actual = session.Analyze(document.Snapshot, [], new(visible, AnalysisScope.Full));
        AssertProvisional(actual, document.Snapshot);
        var error = Assert.Single(actual.Diagnostics);
        Assert.Equal("TOML_OWNERSHIP", error.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(new TextSpan(visible.Start + (suffix.StartsWith('[') ? 1 : 0), keyLength), error.Span);
        Assert.NotEmpty(actual.Tokens);
        var lexical = session.Analyze(document.Snapshot, [], new(visible, AnalysisScope.Visible));
        Assert.Empty(lexical.Diagnostics);
        Assert.Equal(lexical.Coverage, actual.Coverage);
        Assert.Equal(lexical.Tokens, actual.Tokens);
    }

    /// <summary>Unknown prefix and same-transition certificate loss must suppress later tempting conflicts.</summary>
    [Theory]
    [InlineData("unknown-before", "[a.x]\n[a]\nx.z=1\n", "x.z=2\n")]
    [InlineData("unknown-during", "[a.x]\nz=1\n[a]\n", "x.z=2\n")]
    [InlineData("syntax-before", "a=???\n", "a=1\na=2\n")]
    [InlineData("unclosed-eof", "a=1\n", "b=[1,\na=2")]
    public void Uncertified_prefix_does_not_publish_an_ownership_witness(string name, string prefix, string suffix)
    {
        var source = prefix + Padding + suffix;
        Export(name, source);
        AssertUnknownWithoutWitness(source);
    }

    /// <summary>Implicit parents may be defined once without manufacturing a conflict.</summary>
    [Fact]
    public void Valid_implicit_parent_control_is_complete()
    {
        var source = "[a.x.y]\n[a]\n" + Padding + "x.z=3\n";
        Export("valid-implicit", source);
        using var document = new Document(source);
        using var session = new TomlPolicy().CreateSession();
        var actual = Full(session, document.Snapshot);
        Assert.Equal(AnalysisCompleteness.Complete, actual.Completeness);
        Assert.Equal(0, actual.TotalDiagnosticCount);
        Assert.Empty(actual.Diagnostics);
    }

    /// <summary>Length, physical-line and statement-count limits remain inclusive at their documented boundary.</summary>
    [Theory]
    [InlineData("length", false)]
    [InlineData("length", true)]
    [InlineData("lines", false)]
    [InlineData("lines", true)]
    [InlineData("statements", false)]
    [InlineData("statements", true)]
    public void Resource_boundary_cannot_certify_a_conflict_beyond_the_cap(string cap, bool beyond)
    {
        string prefix = "a=1\n";
        string ballast = cap switch
        {
            "length" => "#" + new string('x', 256 * 1024 - 2 + (beyond ? 1 : 0)) + "\n",
            "lines" => "b=[\n" + string.Concat(Enumerable.Repeat("1,\n", 62 + (beyond ? 1 : 0))) + "]\n",
            "statements" => string.Concat(Enumerable.Repeat("#\n", 120_000 - 1025 - 2 + (beyond ? 1 : 0))),
            _ => throw new ArgumentOutOfRangeException(nameof(cap))
        };
        var source = prefix + Padding + ballast + "a=2\n";
        Export(cap + (beyond ? "-beyond" : "-boundary"), source);
        using var document = new Document(source);
        using var session = new TomlPolicy().CreateSession();
        var actual = Full(session, document.Snapshot);
        AssertProvisional(actual, document.Snapshot);
        if (beyond) Assert.Empty(actual.Diagnostics);
        else Assert.Equal(new TextSpan(source.Length - 4, 1), Assert.Single(actual.Diagnostics).Span);
    }

    /// <summary>One dotted statement can create several bindings; statement count is not a binding-cap oracle.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Binding_exhaustion_refuses_a_later_duplicate(bool beyond)
    {
        var prefix = new StringBuilder("root=1\nextra=1\n");
        for (int i = 0; i < 66_666 + (beyond ? 1 : 0); i++) prefix.Append("k").Append(i).Append(".b.c=1\n");
        var source = prefix + Padding + "root=2\n";
        Export(beyond ? "bindings-beyond" : "bindings-admitted", source);
        if (beyond) AssertUnknownWithoutWitness(source);
        else
        {
            using var document = new Document(source);
            using var session = new TomlPolicy().CreateSession();
            var actual = Full(session, document.Snapshot);
            AssertProvisional(actual, document.Snapshot);
            Assert.Equal(new TextSpan(source.Length - 7, 4), Assert.Single(actual.Diagnostics).Span);
        }
    }

    /// <summary>Snapshot authority survives absent or deliberately false edit chains and fresh Undo versions.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repair_and_undo_rebuild_from_snapshot_despite_bad_chain(bool wrongChain)
    {
        using var document = new Document("a=1\n" + Padding + "a=2\n");
        using var session = new TomlPolicy().CreateSession();
        var initial = document.Snapshot;
        Assert.Single(Full(session, initial).Diagnostics);
        document.Apply(new TextChange(initial.Length - 4, 1, "b"));
        IReadOnlyList<VersionedEdit> edits = wrongChain ? [new(999, 1000, new TextChange(0, 1, "z"))] : [];
        var repaired = session.Analyze(document.Snapshot, edits, new(new TextSpan(0, 1), AnalysisScope.Full));
        Assert.Equal(AnalysisCompleteness.Complete, repaired.Completeness);
        Assert.Empty(repaired.Diagnostics);
        Assert.True(document.Undo());
        var restored = Full(session, document.Snapshot);
        Assert.True(restored.Version > repaired.Version);
        Assert.Single(restored.Diagnostics);
        Assert.True(document.Redo());
        Assert.Empty(Full(session, document.Snapshot).Diagnostics);
        // Reanalyzing the immutable older snapshot must not inherit the newest snapshot's result.
        Assert.Single(Full(session, initial).Diagnostics);
    }

    /// <summary>A canceled call throws and cannot poison a subsequent authoritative analysis.</summary>
    [Fact]
    public void Cancellation_does_not_publish_or_poison_session()
    {
        using var document = new Document("a=1\n" + Padding + "a=2\n");
        using var session = new TomlPolicy().CreateSession();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.Analyze(document.Snapshot, [],
            new(new TextSpan(0, 1), AnalysisScope.Full), cancellation.Token));
        Assert.Single(Full(session, document.Snapshot).Diagnostics);
    }

    /// <summary>Requests a tiny viewport while requiring whole-file ownership scanning.</summary>
    private static DocumentAnalysis Full(IFormatSession session, TextSnapshot snapshot) =>
        session.Analyze(snapshot, [], new(new TextSpan(snapshot.Length - 1, 1), AnalysisScope.Full));

    /// <summary>Asserts only the externally promised uncertainty, not private parser structure.</summary>
    private static void AssertProvisional(DocumentAnalysis analysis, TextSnapshot snapshot)
    {
        Assert.True(snapshot.Length > 4 * 1024 * 1024);
        Assert.Equal(snapshot.Version, analysis.Version);
        Assert.Equal(AnalysisCompleteness.Provisional, analysis.Completeness);
        Assert.Null(analysis.TotalDiagnosticCount);
    }

    /// <summary>Checks refusal after independently constructed unknown or exhausted prefixes.</summary>
    private static void AssertUnknownWithoutWitness(string source)
    {
        using var document = new Document(source);
        using var session = new TomlPolicy().CreateSession();
        var actual = Full(session, document.Snapshot);
        AssertProvisional(actual, document.Snapshot);
        Assert.Empty(actual.Diagnostics);
    }

    /// <summary>Retains exact UTF-8 fixtures inside the repository for an independent tomllib run.</summary>
    private static void Export(string name, string source)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Repository root was not found.");
        var directory = Path.Combine(root.FullName, ".temp", "toml-known-error-differential", "fixtures");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".toml"), source, new UTF8Encoding(false));
    }
}
