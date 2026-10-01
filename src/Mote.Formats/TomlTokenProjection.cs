using Mote.Engine;
using Tomlyn.Parsing;

namespace Mote.Formats;

/// <summary>Projects known key/scalar roles from validated IR before lexing bounded remaining gaps.</summary>
internal static class TomlTokenProjection
{
    /// <summary>
    /// Clipped validated scalars cannot acquire a false lexical starting state, including
    /// windows wholly inside multiline strings. Remaining collection/comment gaps are bounded
    /// lexical hints; this does not assert exact context for every nested collection window.
    /// </summary>
    internal static IReadOnlyList<SemanticToken> Project(TextSnapshot snapshot,
        IReadOnlyList<TomlStatement> statements, TextSpan window, CancellationToken ct)
    {
        var certified = new List<SemanticToken>();
        foreach (var statement in statements)
        {
            ct.ThrowIfCancellationRequested();
            var summary = statement.Summary;
            if (summary.Action == TomlStatementAction.Trivia) continue;
            Add("key", summary.KeySpan, statement.Start);
            if (summary.ValueKind is "string" or "number" or "boolean" or "datetime")
                Add(summary.ValueKind, summary.ValueSpan, statement.Start);
        }
        var tokens = new List<SemanticToken>();
        int position = window.Start;
        foreach (var token in certified)
        {
            AppendGap(position, token.Span.Start - position);
            tokens.Add(token);
            position = token.Span.End;
        }
        AppendGap(position, window.End - position);
        return tokens;

        void Add(string kind, TextSpan local, int start)
        {
            int left = Math.Max(window.Start, start + local.Start);
            int right = Math.Min(window.End, start + local.End);
            if (right > left) certified.Add(new(kind, new(left, right - left)));
        }

        void AppendGap(int start, int length)
        {
            if (length <= 0) return;
            var source = snapshot.GetText(start, length);
            var lexer = TomlLexer.Create(source);
            while (lexer.MoveNext())
            {
                ct.ThrowIfCancellationRequested();
                var kind = TomlPolicy.TokenKindName(lexer.Current.Kind);
                if (kind is null) continue;
                int offset = Math.Clamp(lexer.CurrentSpan.Offset, 0, source.Length);
                int count = Math.Clamp(lexer.CurrentSpan.Length, 0, source.Length - offset);
                tokens.Add(new(kind, new(start + offset, count)));
            }
        }
    }
}
