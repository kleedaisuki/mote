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
        var buffer = ArrayPool<char>.Shared.Rent(8192);
        var builder = new StringBuilder();
        var summaries = new List<TomlStatement>();
        var boundary = new TomlStatementBoundary();
        int offset = start, statementStart = start, lines = 0, scanned = 0, parsed = 0, visits = 0;
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
                    offset++;
                    visits++;
                    if (builder.Length > MaxLength) return Result(false);
                    if (ch != '\n') continue;
                    if (++lines > MaxLines) return Result(false);
                    bool continues = boundary.Continues(builder, scanned);
                    scanned = builder.Length;
                    if (continues) continue;
                    if (!Append()) return Result(false);
                    if (stopAtSeam?.Invoke(offset) == true) return Result(true);
                    builder.Clear();
                    boundary = default;
                    scanned = lines = 0;
                    statementStart = offset;
                }
            }
            if (builder.Length != 0)
            {
                if (boundary.Continues(builder, scanned) || !Append()) return Result(false);
            }
            return Result(true);
        }
        finally { ArrayPool<char>.Shared.Return(buffer); }

        // The list and counters are staged, never committed by this reader.
        bool Append()
        {
            ct.ThrowIfCancellationRequested();
            if (summaries.Count == MaxStatements) return false;
            parsed += builder.Length;
            var summary = TomlStatementSummary.Parse(builder.ToString());
            if (summary is null) return false;
            var statement = new TomlStatement(statementStart, summary);
            if (!accept(statement)) return false;
            summaries.Add(statement);
            return true;
        }

        TomlStatementScan Result(bool valid) => new(valid, summaries, offset, parsed, visits);
    }
}

/// <summary>Staged validated syntax summaries and actual parser/scanner work for one read.</summary>
/// <param name="Valid">All delivered syntax units and caller-owned checks were accepted.</param>
/// <param name="Statements">Owned staged summaries; no syntax trees or statement source strings.</param>
/// <param name="End">Absolute stop position at EOF, a certified seam or a refused unit.</param>
/// <param name="ParsedCharacters">Actual standalone parser input units.</param>
/// <param name="ScannedCharacters">Actual logical-boundary visits; not a whole-analysis cost metric.</param>
internal sealed record TomlStatementScan(bool Valid, IReadOnlyList<TomlStatement> Statements,
    int End, int ParsedCharacters, int ScannedCharacters);
