using System.Text;

namespace Mote.Engine;

/// <summary>
/// Persistent AVL rope. Branch metadata makes viewport reads, offset-to-line lookup, and
/// edits logarithmic in the number of chunks; unchanged subtrees are shared by snapshots.
/// </summary>
internal sealed class RopeNode
{
    internal const int ChunkSize = 16 * 1024;
    private const int MergeThreshold = 1024;

    private RopeNode(string? text, RopeNode? left, RopeNode? right)
    {
        Text = text;
        Left = left;
        Right = right;
        if (text is not null)
        {
            Length = text.Length;
            Breaks = CountBreaks(text.AsSpan());
            StartsWithLf = text[0] == '\n';
            EndsWithCr = text[^1] == '\r';
            Height = 1;
        }
        else
        {
            Length = checked(left!.Length + right!.Length);
            Breaks = checked(left.Breaks + right.Breaks - (left.EndsWithCr && right.StartsWithLf ? 1 : 0));
            StartsWithLf = left.StartsWithLf;
            EndsWithCr = right.EndsWithCr;
            Height = Math.Max(left.Height, right.Height) + 1;
        }
    }

    internal string? Text { get; }

    internal RopeNode? Left { get; }

    internal RopeNode? Right { get; }

    internal int Length { get; }

    internal int Breaks { get; }

    internal bool StartsWithLf { get; }

    internal bool EndsWithCr { get; }

    internal int Height { get; }

    internal static RopeNode? FromString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) return null;
        var chunks = new List<RopeNode>((text.Length - 1) / ChunkSize + 1);
        for (var i = 0; i < text.Length; i += ChunkSize)
            chunks.Add(new RopeNode(text.Substring(i, Math.Min(ChunkSize, text.Length - i)), null, null));
        return Build(chunks, 0, chunks.Count);
    }

    internal static RopeNode? FromChunks(IReadOnlyList<string> chunks)
    {
        if (chunks.Count == 0) return null;
        var nodes = new List<RopeNode>(chunks.Count);
        foreach (var chunk in chunks)
            if (chunk.Length > 0) nodes.Add(new RopeNode(chunk, null, null));
        return Build(nodes, 0, nodes.Count);
    }

    internal static RopeNode? Replace(RopeNode? root, int start, int deleteLength, string inserted)
    {
        Split(root, start, out var before, out var suffix);
        Split(suffix, deleteLength, out _, out var after);
        return Concat(Concat(before, FromString(inserted)), after);
    }

    internal static string Slice(RopeNode? root, int start, int length)
    {
        if (length == 0) return string.Empty;
        var builder = new StringBuilder(length);
        AppendRange(root, start, length, builder);
        return builder.ToString();
    }

    internal static IEnumerable<string> Chunks(RopeNode? root)
    {
        if (root is null) yield break;
        var stack = new Stack<RopeNode>();
        var current = root;
        while (current is not null || stack.Count > 0)
        {
            while (current is not null && current.Text is null)
            {
                stack.Push(current);
                current = current.Left;
            }
            if (current is not null)
            {
                yield return current.Text!;
                current = null;
            }
            if (stack.Count > 0) current = stack.Pop().Right;
        }
    }

    /// <summary>Visits only intersecting leaves of an already validated range without copying text.</summary>
    /// <remarks>The stack holds unvisited right subtrees; seeking skips entire prefixes by cached length.</remarks>
    internal static IEnumerable<ReadOnlyMemory<char>> Chunks(RopeNode? root, int start, int length)
    {
        if (root is null || length == 0) yield break;
        var stack = new Stack<RopeNode>();
        var current = root;
        while (true)
        {
            while (current.Text is null)
            {
                var left = current.Left!;
                if (start < left.Length)
                {
                    stack.Push(current.Right!);
                    current = left;
                }
                else
                {
                    start -= left.Length;
                    current = current.Right!;
                }
            }
            var count = Math.Min(length, current.Length - start);
            yield return current.Text.AsMemory(start, count);
            length -= count;
            if (length == 0) yield break;
            current = stack.Pop();
            start = 0;
        }
    }

    internal static int BreaksBefore(RopeNode? node, int offset)
    {
        if (node is null || offset == 0) return 0;
        if (offset == node.Length) return node.Breaks;
        if (node.Text is not null) return CountBreaks(node.Text.AsSpan(0, offset));
        var left = node.Left!;
        return offset <= left.Length
            ? BreaksBefore(left, offset)
            : checked(left.Breaks + BreaksBefore(node.Right, offset - left.Length) -
                (left.EndsWithCr && node.Right!.StartsWithLf ? 1 : 0));
    }

    internal static int NthBreakEnd(RopeNode node, int index)
    {
        if (node.Text is not null)
        {
            for (var i = 0; i < node.Text.Length; i++)
            {
                var c = node.Text[i];
                if (c == '\r' && i + 1 < node.Text.Length && node.Text[i + 1] == '\n') continue;
                if (c is '\r' or '\n' && index-- == 0) return i + 1;
            }
        }
        else
        {
            var left = node.Left!;
            var leftBreaks = left.Breaks - (left.EndsWithCr && node.Right!.StartsWithLf ? 1 : 0);
            return index < leftBreaks
                ? NthBreakEnd(left, index)
                : checked(left.Length + NthBreakEnd(node.Right!, index - leftBreaks));
        }
        throw new InvalidOperationException("Rope line-break metadata is inconsistent.");
    }

    internal static char CharAt(RopeNode node, int offset)
    {
        if (node.Text is not null) return node.Text[offset];
        return offset < node.Left!.Length
            ? CharAt(node.Left, offset)
            : CharAt(node.Right!, offset - node.Left.Length);
    }

    private static RopeNode? Build(IReadOnlyList<RopeNode> nodes, int start, int count)
    {
        if (count == 0) return null;
        if (count == 1) return nodes[start];
        var leftCount = count / 2;
        return Branch(Build(nodes, start, leftCount)!, Build(nodes, start + leftCount, count - leftCount)!);
    }

    private static RopeNode Branch(RopeNode left, RopeNode right) => new(null, left, right);

    private static int CountBreaks(ReadOnlySpan<char> text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                count++;
            }
            else if (text[i] == '\n') count++;
        return count;
    }

    private static void AppendRange(RopeNode? node, int start, int length, StringBuilder builder)
    {
        if (node is null || length == 0) return;
        if (node.Text is not null)
        {
            builder.Append(node.Text, start, length);
            return;
        }
        var leftLength = node.Left!.Length;
        if (start < leftLength)
        {
            var leftCount = Math.Min(length, leftLength - start);
            AppendRange(node.Left, start, leftCount, builder);
            length -= leftCount;
            start = 0;
        }
        else
        {
            start -= leftLength;
        }
        if (length > 0) AppendRange(node.Right, start, length, builder);
    }

    private static void Split(RopeNode? node, int index, out RopeNode? left, out RopeNode? right)
    {
        if (node is null) { left = null; right = null; return; }
        if (index == 0) { left = null; right = node; return; }
        if (index == node.Length) { left = node; right = null; return; }
        if (node.Text is not null)
        {
            left = FromString(node.Text[..index]);
            right = FromString(node.Text[index..]);
            return;
        }
        if (index < node.Left!.Length)
        {
            Split(node.Left, index, out left, out var middle);
            right = Concat(middle, node.Right);
        }
        else
        {
            Split(node.Right, index - node.Left.Length, out var middle, out right);
            left = Concat(node.Left, middle);
        }
    }

    private static RopeNode? Concat(RopeNode? left, RopeNode? right)
    {
        if (left is null) return right;
        if (right is null) return left;
        // Merge small adjacent leaves to keep sustained typing from creating one node per keystroke.
        if (left.Text is not null && right.Text is not null && left.Length + right.Length <= MergeThreshold)
            return new RopeNode(string.Concat(left.Text, right.Text), null, null);
        if (left.Height > right.Height + 1)
            return Balance(Branch(left.Left!, Concat(left.Right, right)!));
        if (right.Height > left.Height + 1)
            return Balance(Branch(Concat(left, right.Left)!, right.Right!));
        return Branch(left, right);
    }

    private static RopeNode Balance(RopeNode node)
    {
        var left = node.Left!;
        var right = node.Right!;
        if (left.Height > right.Height + 1)
        {
            if (left.Right!.Height > left.Left!.Height)
                left = RotateLeft(left);
            return RotateRight(Branch(left, right));
        }
        if (right.Height > left.Height + 1)
        {
            if (right.Left!.Height > right.Right!.Height)
                right = RotateRight(right);
            return RotateLeft(Branch(left, right));
        }
        return node;
    }

    private static RopeNode RotateLeft(RopeNode node)
    {
        var right = node.Right!;
        return Branch(Branch(node.Left!, right.Left!), right.Right!);
    }

    private static RopeNode RotateRight(RopeNode node)
    {
        var left = node.Left!;
        return Branch(left.Left!, Branch(left.Right!, node.Right!));
    }
}
