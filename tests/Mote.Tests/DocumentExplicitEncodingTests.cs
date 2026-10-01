using System.Text;
using Mote.Engine;

namespace Mote.Tests;

/// <summary>Checks explicit encoding against independently fixed bytes, not codec-generated expectations.</summary>
public sealed class DocumentExplicitEncodingTests
{
    /// <summary>Literal encodings of Chinese text and CRLF; byte order and legacy mappings are explicit.</summary>
    public static IEnumerable<object[]> Encodings()
    {
        yield return [DocumentTextEncoding.Utf8, "E4B8ADE696870D0A", "EFBBBF", 65001];
        yield return [DocumentTextEncoding.Utf16LittleEndian, "2D4E87650D000A00", "FFFE", 1200];
        yield return [DocumentTextEncoding.Utf16BigEndian, "4E2D6587000D000A", "FEFF", 1201];
        yield return [DocumentTextEncoding.Utf32LittleEndian, "2D4E0000876500000D0000000A000000", "FFFE0000", 12000];
        yield return [DocumentTextEncoding.Utf32BigEndian, "00004E2D000065870000000D0000000A", "0000FEFF", 12001];
        yield return [DocumentTextEncoding.Gbk, "D6D0CEC40D0A", "", 936];
        yield return [DocumentTextEncoding.Gb18030, "D6D0CEC40D0A", "", 54936];
        yield return [DocumentTextEncoding.Big5, "A4A4A4E50D0A", "", 950];
    }

    /// <summary>Only Unicode choices have BOM fixtures; legacy cases are not vacuous passing tests.</summary>
    public static IEnumerable<object[]> UnicodeEncodings() => Encodings().Where(row => ((string)row[2]).Length != 0);

    /// <summary>Each supported choice decodes exact text and saves its unchanged bytes without adding a BOM.</summary>
    [Theory]
    [MemberData(nameof(Encodings))]
    public async Task Bomless_fixed_bytes_round_trip(DocumentTextEncoding choice, string hex, string bom, int codePage)
    {
        _ = bom;
        using var temp = new RepoTemp();
        var path = temp.File("input.txt");
        var bytes = Convert.FromHexString(hex);
        await File.WriteAllBytesAsync(path, bytes);
        using var document = await Document.OpenWithEncodingAsync(path, choice);
        Assert.Equal("中文\r\n", document.Snapshot.GetText());
        Assert.Equal(codePage, document.Encoding.CodePage);
        Assert.False(document.HasByteOrderMark);
        Assert.False(document.IsModified);
        await document.SaveAsync();
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    /// <summary>All Unicode BOMs are preserved when selected correctly, and never silently override a wrong choice.</summary>
    [Theory]
    [MemberData(nameof(UnicodeEncodings))]
    public async Task Known_bom_requires_matching_choice(DocumentTextEncoding choice, string hex, string bom, int codePage)
    {
        _ = codePage;
        using var temp = new RepoTemp();
        var path = temp.File("bom.txt");
        var bytes = Convert.FromHexString(bom + hex);
        await File.WriteAllBytesAsync(path, bytes);
        using (var document = await Document.OpenWithEncodingAsync(path, choice))
        {
            Assert.Equal("中文\r\n", document.Snapshot.GetText());
            Assert.True(document.HasByteOrderMark);
            await document.SaveAsync();
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        foreach (var other in Enum.GetValues<DocumentTextEncoding>().Where(value => value != choice))
        {
            var error = await Assert.ThrowsAsync<DocumentEncodingConflictException>(() => Document.OpenWithEncodingAsync(path, other));
            Assert.Equal(other, error.SelectedEncoding);
            Assert.Equal(choice, error.DetectedEncoding);
            Assert.DoesNotContain(path, error.Message, StringComparison.Ordinal);
        }
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    /// <summary>BOM detection prefers UTF-32 over its UTF-16 prefix in the unchanged automatic route.</summary>
    [Theory]
    [MemberData(nameof(UnicodeEncodings))]
    public async Task Existing_automatic_open_preserves_unicode_detection(DocumentTextEncoding choice, string hex, string bom, int codePage)
    {
        _ = choice;
        using var temp = new RepoTemp();
        var path = temp.File("automatic.txt");
        var bytes = Convert.FromHexString(bom + hex);
        await File.WriteAllBytesAsync(path, bytes);
        using var document = await Document.OpenAsync(path);
        Assert.Equal("中文\r\n", document.Snapshot.GetText());
        Assert.Equal(codePage, document.Encoding.CodePage);
        Assert.True(document.HasByteOrderMark);
        await document.SaveAsync();
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    /// <summary>Empty input preserves the explicitly chosen encoding without guessing or inventing a marker.</summary>
    [Theory]
    [MemberData(nameof(Encodings))]
    public async Task Empty_file_retains_choice_without_bom(DocumentTextEncoding choice, string hex, string bom, int codePage)
    {
        _ = hex;
        _ = bom;
        using var temp = new RepoTemp();
        var path = temp.File("empty.txt");
        await File.WriteAllBytesAsync(path, []);
        using var document = await Document.OpenWithEncodingAsync(path, choice);
        Assert.Equal("", document.Snapshot.GetText());
        Assert.Equal(codePage, document.Encoding.CodePage);
        Assert.False(document.HasByteOrderMark);
        await document.SaveAsync();
        Assert.Empty(await File.ReadAllBytesAsync(path));
    }

    /// <summary>Malformed Unicode is rejected rather than replaced or retried as a legacy encoding.</summary>
    [Theory]
    [InlineData(DocumentTextEncoding.Utf8, "C0AF")]
    [InlineData(DocumentTextEncoding.Utf16LittleEndian, "00D8")]
    [InlineData(DocumentTextEncoding.Utf16BigEndian, "D800")]
    [InlineData(DocumentTextEncoding.Utf32LittleEndian, "00001100")]
    [InlineData(DocumentTextEncoding.Utf32BigEndian, "00110000")]
    public async Task Invalid_unicode_choice_fails_strictly(DocumentTextEncoding choice, string hex)
    {
        using var temp = new RepoTemp();
        var path = temp.File("invalid-unicode.txt");
        var bytes = Convert.FromHexString(hex);
        await File.WriteAllBytesAsync(path, bytes);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => Document.OpenWithEncodingAsync(path, choice));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    /// <summary>Changing supported Chinese text retains the selected representation and correct saved-state history.</summary>
    [Theory]
    [InlineData(DocumentTextEncoding.Gbk, "D6D0", "CEC4")]
    [InlineData(DocumentTextEncoding.Gb18030, "D6D0", "CEC4")]
    [InlineData(DocumentTextEncoding.Big5, "A4A4", "A4E5")]
    public async Task Edited_legacy_save_preserves_encoding_and_history(DocumentTextEncoding choice, string original, string edited)
    {
        using var temp = new RepoTemp();
        var path = temp.File("legacy.txt");
        await File.WriteAllBytesAsync(path, Convert.FromHexString(original));
        using var document = await Document.OpenWithEncodingAsync(path, choice);
        var initialVersion = document.Snapshot.Version;
        document.Apply(new TextChange(0, 1, "文"));
        var editedVersion = document.Snapshot.Version;
        Assert.True(editedVersion > initialVersion);
        Assert.True(document.IsModified);
        await document.SaveAsync();
        Assert.Equal(editedVersion, document.Snapshot.Version);
        Assert.False(document.IsModified);
        Assert.Equal(Convert.FromHexString(edited), await File.ReadAllBytesAsync(path));
        Assert.True(document.Undo());
        Assert.Equal("中", document.Snapshot.GetText());
        Assert.True(document.IsModified);
        Assert.True(document.Redo());
        Assert.Equal("文", document.Snapshot.GetText());
        Assert.False(document.IsModified);
    }

    /// <summary>An unrepresentable edit cannot replace the original or leave encoder staging files behind.</summary>
    [Theory]
    [InlineData(DocumentTextEncoding.Gbk, "D6D0")]
    [InlineData(DocumentTextEncoding.Big5, "A4A4")]
    public async Task Unsupported_emoji_save_protects_original_and_dirty_state(DocumentTextEncoding choice, string hex)
    {
        using var temp = new RepoTemp();
        var path = temp.File("legacy.txt");
        var original = Convert.FromHexString(hex);
        await File.WriteAllBytesAsync(path, original);
        using var document = await Document.OpenWithEncodingAsync(path, choice);
        document.Apply(new TextChange(1, 0, "😀"));
        var version = document.Snapshot.Version;
        await Assert.ThrowsAsync<EncoderFallbackException>(() => document.SaveAsync());
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Equal("中😀", document.Snapshot.GetText());
        Assert.Equal(version, document.Snapshot.Version);
        Assert.True(document.IsModified);
        Assert.Equal(new[] { path }, Directory.GetFiles(temp.Path));
        Assert.True(document.Undo());
        Assert.False(document.IsModified);
        await document.SaveAsync();
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    /// <summary>The decoder retains partial legacy sequences across the actual 64-KiB read boundary.</summary>
    [Theory]
    [InlineData(DocumentTextEncoding.Gbk, "D6D0", "中")]
    [InlineData(DocumentTextEncoding.Big5, "A4A4", "中")]
    [InlineData(DocumentTextEncoding.Gb18030, "9439FC36", "😀")]
    public async Task Legacy_sequence_crosses_read_boundary(DocumentTextEncoding choice, string hex, string expected)
    {
        using var temp = new RepoTemp();
        var path = temp.File("boundary.txt");
        var prefix = Enumerable.Repeat((byte)0x61, 65535).ToArray();
        var bytes = prefix.Concat(Convert.FromHexString(hex)).Concat(new byte[] { 0x0A }).ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        using var document = await Document.OpenWithEncodingAsync(path, choice);
        Assert.Equal(new string('a', 65535) + expected + "\n", document.Snapshot.GetText());
        await document.SaveAsync();
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    /// <summary>UTF-8 scalars and UTF-16 surrogate pairs survive read boundaries; UTF-32 remains correctly aligned.</summary>
    [Theory]
    [InlineData(DocumentTextEncoding.Utf8, "61", 65535, "F09F9880")]
    [InlineData(DocumentTextEncoding.Utf16LittleEndian, "6100", 32767, "3DD800DE")]
    [InlineData(DocumentTextEncoding.Utf16BigEndian, "0061", 32767, "D83DDE00")]
    [InlineData(DocumentTextEncoding.Utf32LittleEndian, "61000000", 16383, "00F60100")]
    [InlineData(DocumentTextEncoding.Utf32BigEndian, "00000061", 16383, "0001F600")]
    public async Task Unicode_astral_at_read_boundary_round_trips(DocumentTextEncoding choice, string unitHex, int prefixLength, string astralHex)
    {
        using var temp = new RepoTemp();
        var path = temp.File("unicode-boundary.txt");
        var unit = Convert.FromHexString(unitHex);
        var bytes = Enumerable.Range(0, prefixLength).SelectMany(_ => unit).Concat(Convert.FromHexString(astralHex)).ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        using var document = await Document.OpenWithEncodingAsync(path, choice);
        Assert.Equal(new string('a', prefixLength) + "😀", document.Snapshot.GetText());
        await document.SaveAsync();
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    /// <summary>Default open remains strict UTF-8; opting into a legacy codec does not enable replacement fallbacks.</summary>
    [Fact]
    public async Task Legacy_is_explicit_and_invalid_input_is_rejected()
    {
        using var temp = new RepoTemp();
        var path = temp.File("invalid.txt");
        await File.WriteAllBytesAsync(path, Convert.FromHexString("D6D0"));
        await Assert.ThrowsAsync<DecoderFallbackException>(() => Document.OpenAsync(path));
        foreach (var choice in new[] { DocumentTextEncoding.Gbk, DocumentTextEncoding.Gb18030, DocumentTextEncoding.Big5 })
        {
            await File.WriteAllBytesAsync(path, Convert.FromHexString("81"));
            await Assert.ThrowsAsync<DecoderFallbackException>(() => Document.OpenWithEncodingAsync(path, choice));
            Assert.Equal(new byte[] { 0x81 }, await File.ReadAllBytesAsync(path));
        }
    }

    /// <summary>Hash identity protects legacy source bytes even when an external editor restores length and timestamp.</summary>
    [Fact]
    public async Task Same_size_external_change_with_restored_mtime_cannot_be_overwritten()
    {
        using var temp = new RepoTemp();
        var path = temp.File("external.txt");
        await File.WriteAllBytesAsync(path, Convert.FromHexString("D6D0"));
        using var document = await Document.OpenWithEncodingAsync(path, DocumentTextEncoding.Gbk);
        var timestamp = File.GetLastWriteTimeUtc(path);
        document.Apply(new TextChange(0, 1, "文"));
        await File.WriteAllBytesAsync(path, Convert.FromHexString("CEC4"));
        File.SetLastWriteTimeUtc(path, timestamp);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
        await Assert.ThrowsAsync<IOException>(() => document.SaveAsync());
        Assert.True(document.IsModified);
        Assert.Equal(Convert.FromHexString("CEC4"), await File.ReadAllBytesAsync(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(temp.Path));
    }

    /// <summary>Invalid API choices and canceled opens do not return a document or modify source bytes.</summary>
    [Fact]
    public async Task Invalid_choice_and_precanceled_open_fail_without_mutation()
    {
        using var temp = new RepoTemp();
        var path = temp.File("cancel.txt");
        await File.WriteAllBytesAsync(path, new byte[] { 0x61 });
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Document.OpenWithEncodingAsync(path, (DocumentTextEncoding)999));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Document.OpenWithEncodingAsync(path, DocumentTextEncoding.Gbk, cancellation.Token));
        Assert.Equal(new byte[] { 0x61 }, await File.ReadAllBytesAsync(path));
    }
}
