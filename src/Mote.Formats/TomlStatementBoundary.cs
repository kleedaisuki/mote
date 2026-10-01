using System.Text;

namespace Mote.Formats;

/// <summary>Tracks logical TOML statement seams without copying accumulated source.</summary>
/// <remarks>
/// Feed each newly appended complete physical line, or the final EOF segment, exactly once.
/// The builder must preserve its scanned prefix. Reset this struct to default after a seam.
/// This is not a syntax validator: only the statement parser can certify the resulting unit.
/// Quote lookahead may cross builder chunks, but never a physical-line invocation boundary.
/// </remarks>
internal struct TomlStatementBoundary
{
    /// <summary>The active basic or literal string delimiter, or zero outside strings.</summary>
    private char _quote;
    /// <summary>Whether the active delimiter opened a multiline string.</summary>
    private bool _multiline;
    /// <summary>Whether the next basic-string character is escaped.</summary>
    private bool _escaped;
    /// <summary>Whether the current physical line is inside a comment.</summary>
    private bool _comment;
    /// <summary>Net collection nesting; mismatched delimiters are left for syntax validation.</summary>
    private int _depth;

    /// <summary>Scans only [start, statement.Length), returning whether the unit needs more input.</summary>
    /// <remarks>
    /// Plain strings cannot span physical lines. A malformed plain string therefore does not
    /// independently request continuation; its completed unit is still rejected by the parser.
    /// </remarks>
    internal bool Continues(StringBuilder statement, int start)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start, statement.Length);
        for (int i = start; i < statement.Length; i++)
        {
            char ch = statement[i];
            if (_comment) { if (ch is '\r' or '\n') _comment = false; continue; }
            if (_quote != '\0') { ScanString(statement, ref i, ch); continue; }
            if (ch == '#') { _comment = true; continue; }
            if (ch is '"' or '\'') { OpenString(statement, ref i, ch); continue; }
            if (ch is '[' or '{') _depth++;
            if (ch is ']' or '}') _depth--;
        }
        return _multiline || _depth > 0;
    }

    /// <summary>Consumes string content, treating three-to-five closing quotes as one delimiter.</summary>
    private void ScanString(StringBuilder statement, ref int i, char ch)
    {
        if (!_multiline && ch is '\r' or '\n') { CloseString(); return; }
        if (_escaped) { _escaped = false; return; }
        if (_quote == '"' && ch == '\\') { _escaped = true; return; }
        if (ch != _quote) return;
        if (!_multiline) { CloseString(); return; }
        int count = QuoteRun(statement, i, ch);
        i += count - 1;
        if (count >= 3) CloseString();
    }

    /// <summary>Consumes exactly three opening quotes, leaving optional leading quote content.</summary>
    private void OpenString(StringBuilder statement, ref int i, char ch)
    {
        _quote = ch;
        _multiline = QuoteRun(statement, i, ch) >= 3;
        if (_multiline) i += 2;
    }

    /// <summary>Counts a contiguous delimiter run without allocating a source substring.</summary>
    private static int QuoteRun(StringBuilder statement, int start, char quote)
    {
        int end = start + 1;
        while (end < statement.Length && statement[end] == quote) end++;
        return end - start;
    }

    /// <summary>Clears all active string state at a closing delimiter or invalid plain newline.</summary>
    private void CloseString()
    {
        _quote = '\0';
        _multiline = false;
        _escaped = false;
    }
}
