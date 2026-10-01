using System.Runtime.InteropServices;
using System.Text;
using Mote.Engine;

namespace Mote.Native;

/// <summary>Hosted-only fixed-byte checks of codecs embedded in the actual published Native AOT image.</summary>
internal static class NativeEncodingRuntimeProbe
{
    /// <summary>Runs tiny owned-file checks, never opening user files or registering a global provider.</summary>
    internal static int Run(string scratch)
    {
        var checks = new List<string>();
        string? root = null;
        var status = "failure";
        var errorKind = "none";
        try
        {
            root = AdmitFreshScratch(scratch);
            RunChecks(root, checks).GetAwaiter().GetResult();
            status = "success";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            errorKind = error switch
            {
                UnauthorizedAccessException => "admission",
                IOException => "io",
                DecoderFallbackException => "decode",
                EncoderFallbackException => "encode",
                _ => "check",
            };
        }
        if (root is null) return 3;
        try
        {
            // All values are closed labels or runtime architecture, never exceptions, text, or paths.
            var report = "{\"schema\":1,\"status\":\"" + status + "\",\"architecture\":\"" +
                RuntimeInformation.ProcessArchitecture + "\",\"error_kind\":\"" + errorKind +
                "\",\"checks\":[" + string.Join(',', checks.Select(check => "\"" + check + "\"")) + "]}";
            using var output = new FileStream(Path.Combine(root, "report.json"), FileMode.CreateNew, FileAccess.Write);
            var bytes = Encoding.UTF8.GetBytes(report);
            output.Write(bytes);
            output.Flush(true);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { return 2; }
        if (status != "success") return 1;
        Console.WriteLine("mote-native-encoding-ready");
        return 0;
    }

    /// <summary>Only a fresh non-reparse directory under the hosted checkout's .cache/.temp is admitted.</summary>
    private static string AdmitFreshScratch(string scratch)
    {
        var hostedOs = Environment.GetEnvironmentVariable("RUNNER_OS");
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            !(OperatingSystem.IsWindows() && hostedOs == "Windows" || OperatingSystem.IsMacOS() && hostedOs == "macOS"))
            throw new UnauthorizedAccessException("This codec diagnostic requires a disposable hosted runner.");
        var workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (string.IsNullOrWhiteSpace(workspace)) throw new UnauthorizedAccessException("Missing checkout identity.");
        workspace = Path.GetFullPath(workspace);
        if (!Directory.Exists(Path.Combine(workspace, ".git")) && !File.Exists(Path.Combine(workspace, ".git")))
            throw new UnauthorizedAccessException("Missing checkout identity.");
        scratch = Path.GetFullPath(scratch);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!scratch.StartsWith(Path.Combine(workspace, ".cache") + Path.DirectorySeparatorChar, comparison) &&
            !scratch.StartsWith(Path.Combine(workspace, ".temp") + Path.DirectorySeparatorChar, comparison))
            throw new UnauthorizedAccessException("Scratch must remain inside the checkout.");
        if (File.Exists(scratch) || Directory.Exists(scratch))
            throw new UnauthorizedAccessException("Scratch must be fresh.");
        for (var ancestor = new DirectoryInfo(scratch); ancestor is not null; ancestor = ancestor.Parent)
        {
            try
            {
                // Exists hides failures and dangling-link state; inspect attributes directly.
                if ((File.GetAttributes(ancestor.FullName) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Reparse paths are not admitted.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        Directory.CreateDirectory(scratch);
        return scratch;
    }

    /// <summary>Uses literal independent byte fixtures, including a four-byte GB18030 supplementary scalar.</summary>
    private static async Task RunChecks(string root, List<string> checks)
    {
        foreach (var (codec, hex) in new[]
        {
            (DocumentTextEncoding.Gbk, "D6D0CEC40D0A"),
            (DocumentTextEncoding.Gb18030, "D6D0CEC40D0A9439FC36"),
            (DocumentTextEncoding.Big5, "A4A4A4E50D0A"),
        })
        {
            var path = Path.Combine(root, codec + ".txt");
            var bytes = Convert.FromHexString(hex);
            WriteNew(path, bytes);
            using var document = await Document.OpenWithEncodingAsync(path, codec).ConfigureAwait(false);
            Require(document.Snapshot.GetText() == (codec == DocumentTextEncoding.Gb18030 ? "中文\r\n😀" : "中文\r\n"));
            await document.SaveAsync().ConfigureAwait(false);
            Require((await File.ReadAllBytesAsync(path).ConfigureAwait(false)).AsSpan().SequenceEqual(bytes));
            checks.Add(codec + "_exact_save");
            if (codec == DocumentTextEncoding.Gb18030) continue;
            document.Apply(new TextChange(document.Snapshot.Length, 0, "😀"));
            await MustThrow<EncoderFallbackException>(() => document.SaveAsync()).ConfigureAwait(false);
            Require(document.IsModified && document.PendingSaveRecovery is null);
            Require(!File.Exists(Document.GetSaveRecoveryPath(path)));
            Require((await File.ReadAllBytesAsync(path).ConfigureAwait(false)).AsSpan().SequenceEqual(bytes));
            checks.Add(codec + "_strict_encode_protection");
        }
        var gbkPath = Path.Combine(root, "Gbk.txt");
        await MustThrow<DecoderFallbackException>(() => Document.OpenAsync(gbkPath)).ConfigureAwait(false);
        checks.Add("default_utf8_refusal");
        var unicode = Path.Combine(root, "unicode.txt");
        var unicodeBytes = Convert.FromHexString("FFFE2D4E87650D000A00");
        WriteNew(unicode, unicodeBytes);
        await MustThrow<DocumentEncodingConflictException>(() => Document.OpenWithEncodingAsync(unicode, DocumentTextEncoding.Gbk)).ConfigureAwait(false);
        using var matching = await Document.OpenWithEncodingAsync(unicode, DocumentTextEncoding.Utf16LittleEndian).ConfigureAwait(false);
        Require(matching.HasByteOrderMark && matching.Snapshot.GetText() == "中文\r\n");
        await matching.SaveAsync().ConfigureAwait(false);
        Require((await File.ReadAllBytesAsync(unicode).ConfigureAwait(false)).AsSpan().SequenceEqual(unicodeBytes));
        checks.Add("unicode_bom_conflict_and_exact_save");
    }

    /// <summary>Each fixed fixture is created exclusively; no existing target can be overwritten.</summary>
    private static void WriteNew(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        stream.Write(bytes);
    }

    /// <summary>Checks an exact expected failure, disposing an unexpected opened document before rejecting it.</summary>
    private static async Task MustThrow<T>(Func<Task> action) where T : Exception
    {
        try
        {
            var pending = action();
            await pending.ConfigureAwait(false);
            if (pending is Task<Document> opened) opened.Result.Dispose();
        }
        catch (T) { return; }
        throw new InvalidOperationException("An expected strict codec rejection did not occur.");
    }

    /// <summary>A failed assertion is a failed diagnostic, never a repaired success.</summary>
    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("The codec contract check failed.");
    }
}
