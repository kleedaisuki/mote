using System.Buffers.Binary;
using System.IO.Compression;

namespace Mote.Native.Windows.Canvas;

/// <summary>Writes inspectable RGB PNGs from the exact top-down DIB blitted in WM_PAINT.</summary>
internal static class WindowsOnScreenCanvasPng
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>Encodes a bounded 32-bpp BGRA DIB without a native image dependency.</summary>
    internal static void Write(string path, int width, int height, byte[] bgra)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bgra);
        if (width <= 0 || height <= 0 || bgra.Length != checked(width * height * 4))
            throw new ArgumentException("The DIB dimensions and byte count disagree.", nameof(bgra));

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null) Directory.CreateDirectory(directory);
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        output.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)height);
        header[8] = 8; // Eight bits per color sample.
        header[9] = 2; // RGB; DIB alpha is not meaningful for an opaque window.
        Chunk(output, "IHDR"u8, header);

        using var compressed = new MemoryStream();
        using (var zipper = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[checked(width * 3 + 1)];
            for (var y = 0; y < height; y++)
            {
                row[0] = 0; // PNG filter None.
                for (var x = 0; x < width; x++)
                {
                    var source = (y * width + x) * 4;
                    var target = x * 3 + 1;
                    row[target] = bgra[source + 2];
                    row[target + 1] = bgra[source + 1];
                    row[target + 2] = bgra[source];
                }
                zipper.Write(row);
            }
        }
        Chunk(output, "IDAT"u8, compressed.ToArray());
        Chunk(output, "IEND"u8, []);
        output.Flush(true);
    }

    private static void Chunk(Stream output, ReadOnlySpan<byte> kind, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, checked((uint)data.Length));
        output.Write(number);
        output.Write(kind);
        output.Write(data);
        var crc = 0xFFFFFFFFu;
        foreach (var value in kind) crc = Step(crc, value);
        foreach (var value in data) crc = Step(crc, value);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        output.Write(number);
    }

    private static uint Step(uint crc, byte value)
    {
        crc ^= value;
        for (var i = 0; i < 8; i++)
            crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320u);
        return crc;
    }
}
