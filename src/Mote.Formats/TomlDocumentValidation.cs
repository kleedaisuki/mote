namespace Mote.Formats;

/// <summary>Uniform whole-source ownership using validated local syntax, without large-session budgets.</summary>
internal static class TomlDocumentValidation
{
    /// <summary>
    /// Validates grammar/inline semantics statement-by-statement and normative table ownership.
    /// Preserves independent recoverable failures. Malformed multiline units may prevent
    /// suffix recovery; whole-parser grammar diagnostics remain available to the caller.
    /// </summary>
    internal static IReadOnlyList<Diagnostic> Validate(string source, CancellationToken ct)
    {
        var ownership = new TomlOwnershipIndex(int.MaxValue, recover: true);
        var diagnostics = new List<Diagnostic>();
        bool Accept(TomlStatement statement)
        {
            ct.ThrowIfCancellationRequested();
            var conflict = statement.Summary.Apply(ownership, statement.Start);
            if (conflict is not null) diagnostics.Add(conflict with { Code = "TOML_PARSE" });
            return true;
        }
        void Invalid(bool header) { if (header) ownership.InvalidateCurrentScope(); }
        var scan = TomlStatementReader.Validate(source, Accept, Invalid, ct);
        ct.ThrowIfCancellationRequested();
        diagnostics.AddRange(scan.Diagnostics);
        if (diagnostics.Count != 0) return diagnostics;
        if (scan.Valid) return Array.Empty<Diagnostic>();
        // Grammar diagnostics normally explain an invalid unit. Never translate an
        // unexplained parser refusal into a false valid public-policy result.
        return [new(DiagnosticSeverity.Error, "TOML_PARSE", "Invalid TOML statement.",
            new TextSpan(Math.Min(scan.End, source.Length), 0))];
    }
}
