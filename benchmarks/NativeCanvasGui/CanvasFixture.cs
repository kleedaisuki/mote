using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

/// <summary>Byte-exact synthetic canvas fixtures and streaming save oracle.</summary>
/// <remarks>No user text is read or written by this helper.</remarks>
public static class MoteCanvasFixture
{
    private const int MiB = 1024 * 1024;

    /// <summary>Writes 100 MiB of repeating fixed-width CRLF source rows.</summary>
    public static void WriteManyLines(string path)
    {
        var block = new byte[2048 * 64];
        for (var row = 0; row < 2048; row++)
        {
            var text = "line " + row.ToString("D4", CultureInfo.InvariantCulture) +
                " " + new string('a', 52) + "\r\n";
            var bytes = Encoding.ASCII.GetBytes(text);
            if (bytes.Length != 64) throw new InvalidOperationException("Canvas row is not 64 bytes.");
            Buffer.BlockCopy(bytes, 0, block, row * 64, bytes.Length);
        }
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, block.Length);
        for (var i = 0; i < 800; i++) stream.Write(block);
        stream.Flush(true);
        if (stream.Length != 100L * MiB) throw new InvalidOperationException("Many-line fixture size differs.");
    }

    /// <summary>Writes 50 MiB of one ASCII line, with no terminator.</summary>
    public static void WriteLongLine(string path)
    {
        var block = new byte[64 * 1024];
        Array.Fill(block, (byte)'a');
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, block.Length);
        for (var i = 0; i < 800; i++) stream.Write(block);
        stream.Flush(true);
        if (stream.Length != 50L * MiB) throw new InvalidOperationException("Long-line fixture size differs.");
    }

    /// <summary>Hashes a file by streaming; avoids a full-file managed string or byte array.</summary>
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>Proves Save produced exactly one prefixed X and retained every original byte.</summary>
    public static bool HasOnePrefixedEdit(string path, long originalLength, string originalSha256)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite, 128 * 1024);
            if (stream.Length != originalLength + 1 || stream.ReadByte() != 'X') return false;
            var remainingHash = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(remainingHash, originalSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
    }
}
