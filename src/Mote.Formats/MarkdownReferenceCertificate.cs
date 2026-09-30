using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Mote.Formats;

/// <summary>Closed bounded presence certificate for one independently isolated source owner.</summary>
internal sealed record MarkdownReferenceCertificate(string[] Keys, MarkdownReferenceCertificate.Atom[] Atoms, Dictionary<string, int> Multiplicities)
{
    /// <summary>The exact pipeline is part of the certificate's identity.</summary>
    internal static MarkdownPipeline Pipeline => MarkdownPolicy.Pipeline;

    /// <summary>Expected full-reference source atom; endpoints are UTF-16 half-open coordinates.</summary>
    internal sealed record Atom(int Start, int Length, string TextKey, string TargetKey);

    /// <summary>Canonicalization is intentionally restricted to ASCII letters/digits/spaces.</summary>
    internal static string Key(string label) => string.Join(' ', label.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Rejects Unicode, escaping, nested labels, punctuation, empty/overlong labels.</summary>
    internal static bool IsLabel(string label) => label.Length is > 0 and <= 64 && label.Any(char.IsAsciiLetterOrDigit) && label.All(c => char.IsAsciiLetterOrDigit(c) || c == ' ');

    /// <summary>Enumerates every environment presence assignment for the complete owner, never isolated atoms.</summary>
    internal static MarkdownReferenceCertificate? Admit(string text, Action<string> checkpoint, ref int parses, MarkdownReferenceBudget ledger)
    {
        if (text.Length > 4096) return null;
        var atoms = Scan(text);
        if (atoms is null) return null;
        var keys = atoms.SelectMany(a => new[] { a.TextKey, a.TargetKey }).Distinct(StringComparer.Ordinal).Order().ToArray();
        if (keys.Length > 4 || atoms.Length > 32) return null;
        for (var mask = 0; mask < (1 << keys.Length); mask++)
        {
            checkpoint("presence-mask");
            var suffix = new StringBuilder();
            for (var k = 0; k < keys.Length; k++)
                if ((mask & (1 << k)) != 0) suffix.Append($"\n\n[{keys[k]}]: https://sentinel.test/key{k}");
            var context = text + suffix;
            var ast = ledger.Parse(context.Length, () => Markdown.Parse(context, Pipeline));
            parses++;
            var definitions = ast.GetLinkReferenceDefinitions(false);
            if ((definitions?.Links.Count ?? 0) != System.Numerics.BitOperations.PopCount((uint)mask)) return null;
            for (var k = 0; k < keys.Length; k++)
                if ((mask & (1 << k)) != 0 && (definitions is null || !definitions.TryGet(keys[k], out var definition) || definition.Url != $"https://sentinel.test/key{k}" || definition.Title is not null || definition.CreateLinkInline is not null)) return null;
            var real = ast.Where(b => b is ParagraphBlock or HeadingBlock && b.Span.Start < text.Length).ToArray();
            if (real.Length != 1 || real[0] is not LeafBlock leaf) return null;
            if (text.StartsWith('#') != (leaf is HeadingBlock) || leaf.Span.Start != 0 || leaf.Span.Length != text.Length) return null;
            if (!Permitted(leaf.Inline)) return null;
            var links = AllLinks(leaf.Inline).ToArray();
            var expected = atoms.Where(a => (mask & (1 << Array.IndexOf(keys, a.TargetKey))) != 0).ToArray();
            if (links.Length != expected.Length) return null;
            for (var i = 0; i < links.Length; i++)
            {
                var a = expected[i];
                var k = Array.IndexOf(keys, a.TargetKey);
                if (links[i].IsImage || links[i].IsShortcut || links[i].Span.Start != a.Start || links[i].Span.Length != a.Length || Key(links[i].Label!) != a.TargetKey || links[i].Url != $"https://sentinel.test/key{k}") return null;
                if (links[i].Reference is null || !ReferenceEquals(links[i].Reference, definitions!.Links[a.TargetKey]) || links[i].LabelSpan.Start < a.Start || links[i].LabelSpan.End >= a.Start + a.Length) return null;
            }
        }
        return new MarkdownReferenceCertificate(keys, atoms, atoms.GroupBy(a => a.TargetKey).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal));
    }

    /// <summary>Only plain paragraphs/ATX headings and full atoms separated by at least one literal space are candidates.</summary>
    private static Atom[]? Scan(string text)
    {
        var i = 0;
        if (text.StartsWith('#'))
        {
            while (i < text.Length && text[i] == '#') i++;
            if (i > 6 || i == text.Length || text[i] != ' ') return null;
            i++;
        }
        var atoms = new List<Atom>();
        while (i < text.Length)
        {
            if (text[i] != '[')
            {
                if (!char.IsAsciiLetterOrDigit(text[i]) && text[i] != ' ') return null;
                i++;
                continue;
            }
            var start = i;
            if (atoms.Count > 0 && text[i - 1] != ' ') return null;
            var close = text.IndexOf(']', i + 1);
            if (close < 0 || close + 1 >= text.Length || text[close + 1] != '[') return null;
            var end = text.IndexOf(']', close + 2);
            if (end < 0) return null;
            var display = text[(i + 1)..close];
            var label = text[(close + 2)..end];
            if (!IsLabel(display) || !IsLabel(label)) return null;
            if (end + 1 < text.Length && text[end + 1] != ' ') return null;
            atoms.Add(new Atom(start, end + 1 - start, Key(display), Key(label)));
            i = end + 1;
        }
        return text.Any(char.IsAsciiLetterOrDigit) ? atoms.ToArray() : null;
    }

    /// <summary>Even a matching link count does not authorize an unexpected inline syntax tree.</summary>
    private static bool Permitted(ContainerInline? container)
    {
        if (container is null) return true;
        for (var node = container.FirstChild; node is not null; node = node.NextSibling)
        {
            if (node is LiteralInline or LinkDelimiterInline) continue;
            if (node is not LinkInline link || link.IsImage || link.IsShortcut || !Permitted(link)) return false;
        }
        return true;
    }

    /// <summary>Traverses parser containers, preserving source order and observing all resolved links.</summary>
    internal static IEnumerable<LinkInline> AllLinks(ContainerInline? container)
    {
        if (container is null) yield break;
        for (var node = container.FirstChild; node is not null; node = node.NextSibling)
        {
            if (node is LinkInline link) yield return link;
            if (node is ContainerInline child)
                foreach (var nested in AllLinks(child)) yield return nested;
        }
    }
}
