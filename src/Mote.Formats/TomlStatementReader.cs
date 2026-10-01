using System.Buffers;
using System.Text;
using Mote.Engine;

namespace Mote.Formats;

/// <summary>Streams and validates logical statements from a previously certified source seam.</summary>
internal static class TomlStatementReader
{
    /// <summary>Current explicit resource refusals, not a definition of the TOML language.</summary>
    internal const int MaxLength = 256 * 1024;
    /// <summary>Current logical-statement line budget; each new segment is scanned once.</summary>
    internal const int MaxLines = 64;
    /// <summary>Current whole-document summary-count budget.</summary>
    internal const int MaxStatements = 120_000;

    /// <summary>
    /// Reads until EOF or a certified stop seam. Every accepted unit has independently valid
    /// syntax/inline ownership; a caller may reject a unit on global ownership/resource grounds.
    /// </summary>
    internal static TomlStatementScan Read(TextSnapshot snapshot, int start,
        Func<TomlStatement, bool> accept, Func<int, bool>? stopAtSeam, CancellationToken ct)
    {
        using var reader = new SnapshotTextReader(snapshot, start, snapshot.Length - start, ct);
        return ReadCore(reader, start, true, accept, stopAtSeam, null, ct);
    }

    /// <summary>
    /// Validates a legacy string without large-session budgets or a second document/rope copy.
    /// Statements are released immediately. Recoverable units keep their independent
    /// diagnostics; invalid headers can explicitly quarantine subsequent assignment scope.
    /// </summary>
    internal static TomlStatementScan Validate(string source, Func<TomlStatement, bool> accept,
        Action<bool> invalidUnit, CancellationToken ct)
    {
        using var reader = new StringReader(source);
        return ReadCore(reader, 0, false, accept, null, invalidUnit, ct);
    }

    /// <summary>Shares one scanner/parser mechanism between budgeted caching and unbounded validation.</summary>
    private static TomlStatementScan ReadCore(TextReader reader, int start, bool bounded,
        Func<TomlStatement, bool> accept, Func<int, bool>? stopAtSeam,
        Action<bool>? invalidUnit, CancellationToken ct)
    {
        var buffer = ArrayPool<char>.Shared.Rent(8192);
        var builder = new StringBuilder();
        var summaries = new List<TomlStatement>();
        List<Diagnostic>? diagnostics = null;
        var boundary = new TomlStatementBoundary();
        int offset = start, statementStart = start, lines = 0, scanned = 0, parsed = 0, visits = 0;
        bool header = false, classified = false;
        try
        {
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                for (int i = 0; i < read; i++)
                {
                    char ch = buffer[i];
                    builder.Append(ch);
                    if (!classified && !char.IsWhiteSpace(ch) && !char.IsControl(ch))
                    { header = ch == '['; classified = true; }
                    offset++;
                    visits++;
                    if (bounded && builder.Length > MaxLength) return Result(false);
                    if (ch != '\n') continue;
                    if (++lines > MaxLines && bounded) return Result(false);
                    bool continues = boundary.Continues(builder, scanned);
                    scanned = builder.Length;
                    // Headers cannot span lines. Public recovery may therefore discard
                    // an invalid header at its physical boundary without guessing the
                    // continuation state of a valid multiline assignment value.
                    if (continues && (bounded || !header)) continue;
                    if (!Append()) return Result(false);
                    if (stopAtSeam?.Invoke(offset) == true) return Result(true);
                    builder.Clear();
                    boundary = default;
                    scanned = lines = 0;
                    header = classified = false;
                    statementStart = offset;
                }
            }
            if (builder.Length != 0)
            {
                if (boundary.Continues(builder, scanned) && bounded || !Append()) return Result(false);
            }
            return Result(true);
        }
        finally { ArrayPool<char>.Shared.Return(buffer); }

        // The list and counters are staged, never committed by this reader.
        bool Append()
        {
            ct.ThrowIfCancellationRequested();
            if (bounded && summaries.Count == MaxStatements) return false;
            parsed += builder.Length;
            var source = builder.ToString();
            var summary = TomlStatementSummary.Parse(source, out var localDiagnostics, out bool headerUnit);
            if (localDiagnostics.Count != 0)
            {
                diagnostics ??= new List<Diagnostic>();
                foreach (var error in localDiagnostics)
                    diagnostics.Add(error with { Span = new TextSpan(statementStart + error.Span.Start, error.Span.Length) });
            }
            if (summary is null)
            {
                if (bounded) return false;
                if (localDiagnostics.Count == 0)
                    (diagnostics ??= new List<Diagnostic>()).Add(new(DiagnosticSeverity.Error, "TOML_PARSE",
                        "Invalid TOML statement.", new TextSpan(statementStart, source.Length)));
                invalidUnit?.Invoke(headerUnit);
                return true;
            }
            var statement = new TomlStatement(statementStart, summary);
            if (!accept(statement)) return false;
            if (bounded) summaries.Add(statement);
            return true;
        }

        TomlStatementScan Result(bool valid) => new(valid && diagnostics is not { Count: > 0 }, summaries,
            offset, parsed, visits, diagnostics ?? (IReadOnlyList<Diagnostic>)Array.Empty<Diagnostic>());
    }
}

/// <summary>Staged validated syntax summaries and actual parser/scanner work for one read.</summary>
/// <param name="Valid">All delivered syntax units and caller-owned checks were accepted.</param>
/// <param name="Statements">Owned staged summaries; no syntax trees or statement source strings.</param>
/// <param name="End">Absolute stop position at EOF, a certified seam or a refused unit.</param>
/// <param name="ParsedCharacters">Actual standalone parser input units.</param>
/// <param name="ScannedCharacters">Actual logical-boundary visits; not a whole-analysis cost metric.</param>
/// <param name="Diagnostics">Absolute local syntax/inline diagnostics; unbounded mode recovers across certified seams.</param>
internal sealed record TomlStatementScan(bool Valid, IReadOnlyList<TomlStatement> Statements,
    int End, int ParsedCharacters, int ScannedCharacters, IReadOnlyList<Diagnostic> Diagnostics);
