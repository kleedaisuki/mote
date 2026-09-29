namespace Mote.Native.Viewport;

/// <summary>
/// Stores only line heights that differ from the implicit constant-height layout.
/// An AVL tree keeps prefix sums and pixel-to-line lookup logarithmic in the
/// number of measured exceptions, not in the document's line count.
/// </summary>
internal sealed class SparseHeightIndex
{
    private sealed class Node(int line, double delta)
    {
        internal int Line = line;
        internal double Delta = delta;
        internal int Height = 1;
        internal Node? Left;
        internal Node? Right;
        internal double Sum = delta;
        internal int MaxLine = line;
    }

    private Node? _root;

    /// <summary>Number of explicitly measured lines.</summary>
    internal int Count { get; private set; }

    /// <summary>Sum of all deviations from the implicit line height.</summary>
    internal double TotalDelta => _root?.Sum ?? 0;

    /// <summary>Removes measurements after a typography or source-structure change.</summary>
    internal void Clear()
    {
        _root = null;
        Count = 0;
    }

    /// <summary>Gets the correction for a line, or zero for an unmeasured line.</summary>
    internal double GetDelta(int line)
    {
        var node = _root;
        while (node is not null)
        {
            if (line == node.Line) return node.Delta;
            node = line < node.Line ? node.Left : node.Right;
        }

        return 0;
    }

    /// <summary>Returns the correction sum for lines strictly before <paramref name="line"/>.</summary>
    internal double PrefixDelta(int line)
    {
        double sum = 0;
        var node = _root;
        while (node is not null)
        {
            if (line <= node.Line)
            {
                node = node.Left;
                continue;
            }

            sum += (node.Left?.Sum ?? 0) + node.Delta;
            node = node.Right;
        }

        return sum;
    }

    /// <summary>Sets a line's deviation, removing it when the height returns to baseline.</summary>
    internal void SetDelta(int line, double delta)
    {
        var existed = GetDelta(line) != 0;
        if (delta == 0)
        {
            if (!existed) return;
            _root = Delete(_root, line);
            Count--;
            return;
        }

        _root = Insert(_root, line, delta);
        if (!existed) Count++;
    }

    /// <summary>
    /// Finds the row containing an absolute document Y coordinate without visiting
    /// each implicit line. The caller clamps Y to the finite document extent.
    /// </summary>
    internal (int Line, double IntraRowY) Locate(double y, int lineCount, double baseHeight)
    {
        var cursor = 0;
        var node = _root;
        while (node is not null)
        {
            if (node.Left is { } left)
            {
                var blockHeight = (left.MaxLine - cursor + 1) * baseHeight + left.Sum;
                if (y < blockHeight)
                {
                    node = left;
                    continue;
                }

                y -= blockHeight;
                cursor = left.MaxLine + 1;
            }

            var gapHeight = (node.Line - cursor) * baseHeight;
            if (y < gapHeight)
            {
                var rows = Math.Floor(y / baseHeight);
                return (cursor + (int)rows, y - rows * baseHeight);
            }

            y -= gapHeight;
            var rowHeight = baseHeight + node.Delta;
            if (y < rowHeight) return (node.Line, y);
            y -= rowHeight;
            cursor = node.Line + 1;
            node = node.Right;
        }

        var remaining = Math.Min(lineCount - cursor - 1, (int)Math.Floor(y / baseHeight));
        return (cursor + remaining, y - remaining * baseHeight);
    }

    private static Node Insert(Node? node, int line, double delta)
    {
        if (node is null) return new Node(line, delta);
        if (line == node.Line) node.Delta = delta;
        else if (line < node.Line)
            node.Left = Insert(node.Left, line, delta);
        else
            node.Right = Insert(node.Right, line, delta);
        return Balance(node);
    }

    private static Node? Delete(Node? node, int line)
    {
        if (node is null) return null;
        if (line < node.Line) node.Left = Delete(node.Left, line);
        else if (line > node.Line) node.Right = Delete(node.Right, line);
        else if (node.Left is null) return node.Right;
        else if (node.Right is null) return node.Left;
        else
        {
            var successor = node.Right;
            while (successor!.Left is not null) successor = successor.Left;
            node.Line = successor.Line;
            node.Delta = successor.Delta;
            node.Right = Delete(node.Right, successor.Line);
        }
        return Balance(node);
    }

    private static Node RotateRight(Node node)
    {
        var left = node.Left!;
        node.Left = left.Right;
        left.Right = node;
        Refresh(node);
        Refresh(left);
        return left;
    }

    private static Node RotateLeft(Node node)
    {
        var right = node.Right!;
        node.Right = right.Left;
        right.Left = node;
        Refresh(node);
        Refresh(right);
        return right;
    }

    private static void Refresh(Node node)
    {
        node.Sum = node.Delta + (node.Left?.Sum ?? 0) + (node.Right?.Sum ?? 0);
        node.MaxLine = node.Right?.MaxLine ?? node.Line;
        node.Height = Math.Max(node.Left?.Height ?? 0, node.Right?.Height ?? 0) + 1;
    }

    private static Node Balance(Node node)
    {
        Refresh(node);
        var skew = (node.Left?.Height ?? 0) - (node.Right?.Height ?? 0);
        if (skew > 1)
        {
            var left = node.Left!;
            if ((left.Left?.Height ?? 0) < (left.Right?.Height ?? 0))
                node.Left = RotateLeft(left);
            return RotateRight(node);
        }

        if (skew < -1)
        {
            var right = node.Right!;
            if ((right.Right?.Height ?? 0) < (right.Left?.Height ?? 0))
                node.Right = RotateRight(right);
            return RotateLeft(node);
        }

        return node;
    }
}
