using System.Security.Cryptography;
using System.Text;

namespace Mote.Engine;

public sealed partial class Document
{
    /// <summary>
    /// Opens using an explicitly chosen strict codec. A recognized BOM must agree,
    /// and legacy bytes must reproduce exactly before an editable document is returned.
    /// No fallback, locale detection, or encoding conversion occurs.
    /// </summary>
    /// <exception cref="DocumentEncodingConflictException">The source BOM disagrees with the choice.</exception>
    /// <exception cref="DocumentEncodingRoundTripException">The decoded text cannot reproduce the source bytes.</exception>
    /// <exception cref="DecoderFallbackException">The input is invalid for the selected codec.</exception>
    /// <example><code>using var document = await Document.OpenWithEncodingAsync(path, DocumentTextEncoding.Gbk);</code></example>
    public static Task<Document> OpenWithEncodingAsync(string path, DocumentTextEncoding encoding,
        CancellationToken cancellationToken = default) => OpenCoreAsync(path, encoding, cancellationToken);

    /// <summary>Shares the original streaming read/hash boundary between default and explicit opens.</summary>
    private static async Task<Document> OpenCoreAsync(string path, DocumentTextEncoding? selected,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var chosen = selected is { } choice ? DocumentEncodingPolicy.Create(choice) : null;
        path = Path.GetFullPath(path);
        var before = FileStamp.Read(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var marker = new byte[4];
        var markerLength = await stream.ReadAtLeastAsync(marker, 4, false, cancellationToken).ConfigureAwait(false);
        var (detected, markerSize) = DetectEncoding(marker.AsSpan(0, markerLength));
        if (chosen is not null && markerSize > 0 && chosen.CodePage != detected.CodePage)
            throw new DocumentEncodingConflictException(selected!.Value,
                DocumentEncodingPolicy.FromUnicodeCodePage(detected.CodePage));
        var encoding = chosen ?? detected;
        stream.Position = markerSize;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(marker, 0, markerSize);
        using var inverse = selected is { } explicitChoice && DocumentEncodingPolicy.IsLegacy(explicitChoice)
            ? new SourceByteRoundTrip(encoding) : null;
        var decoder = encoding.GetDecoder();
        var bytes = new byte[64 * 1024];
        var chars = new char[64 * 1024];
        var chunks = new List<string>();
        long length = 0;
        int count;
        while ((count = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(bytes, 0, count);
            var consumed = 0;
            while (consumed < count)
            {
                decoder.Convert(bytes, consumed, count - consumed, chars, 0, chars.Length, false,
                    out var bytesUsed, out var charsUsed, out _);
                if (bytesUsed == 0 && charsUsed == 0)
                    throw new IOException("The decoder did not make progress.");
                consumed += bytesUsed;
                inverse?.Append(chars.AsSpan(0, charsUsed));
                AddChunk(chunks, chars, charsUsed, ref length);
            }
        }
        decoder.Convert(Array.Empty<byte>(), 0, 0, chars, 0, chars.Length, true,
            out _, out var finalChars, out _);
        inverse?.Append(chars.AsSpan(0, finalChars));
        AddChunk(chunks, chars, finalChars, ref length);
        var after = FileStamp.Read(path);
        if (after != before) throw new IOException("The file changed while it was being opened.");
        var rawHash = hash.GetHashAndReset();
        if (inverse is not null && !inverse.Matches(rawHash))
            throw new DocumentEncodingRoundTripException(selected!.Value);
        var document = new Document(RopeNode.FromChunks(chunks), path, encoding, markerSize > 0);
        document._fileStamp = after;
        document._fileHash = rawHash;
        return document;
    }

    /// <summary>Checks inverse byte identity in fixed space, carrying encoder state across chunks.</summary>
    private sealed class SourceByteRoundTrip : IDisposable
    {
        private readonly Encoder _encoder;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly byte[] _bytes = new byte[64 * 1024];

        /// <summary>Uses the same strict codec that subsequent Save will retain.</summary>
        internal SourceByteRoundTrip(Encoding encoding) => _encoder = encoding.GetEncoder();

        /// <summary>Appends decoded UTF-16 without materializing a second full document.</summary>
        internal void Append(ReadOnlySpan<char> chars) => Convert(chars, false);

        /// <summary>Flushes final encoder state and compares the entire inverse byte stream.</summary>
        internal bool Matches(ReadOnlySpan<byte> sourceHash)
        {
            Convert(ReadOnlySpan<char>.Empty, true);
            return _hash.GetHashAndReset().AsSpan().SequenceEqual(sourceHash);
        }

        /// <summary>Handles output-buffer exhaustion and split surrogate pairs without dropping bytes.</summary>
        private void Convert(ReadOnlySpan<char> chars, bool flush)
        {
            bool complete;
            do
            {
                _encoder.Convert(chars, _bytes, flush, out var usedChars, out var usedBytes, out complete);
                _hash.AppendData(_bytes, 0, usedBytes);
                chars = chars[usedChars..];
                if (!complete && usedChars == 0 && usedBytes == 0)
                    throw new IOException("The source-byte verifier did not make progress.");
            } while (!complete);
        }

        /// <summary>Releases the admission hash even if decoding or inverse encoding fails.</summary>
        public void Dispose() => _hash.Dispose();
    }
}
