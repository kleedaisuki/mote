using System.Reflection;
using Mote.Native;

namespace Mote.Tests;

/// <summary>Exercises the diagnostic's actual fixed-file body without impersonating a hosted runner.</summary>
public sealed class NativeEncodingRuntimeProbeTests
{
    /// <summary>The seven checks use the production engine and retain exact owned fixture bytes.</summary>
    [Fact]
    public async Task Fixed_codec_body_completes_without_gui_or_global_environment_changes()
    {
        using var temp = new RepoTemp();
        var checks = new List<string>();
        var body = typeof(NativeEncodingRuntimeProbe).GetMethod("RunChecks", BindingFlags.NonPublic | BindingFlags.Static)!;
        await (Task)body.Invoke(null, [temp.Path, checks])!;
        Assert.Equal(new[]
        {
            "Gbk_exact_save", "Gbk_strict_encode_protection", "Gb18030_exact_save",
            "Big5_exact_save", "Big5_strict_encode_protection", "default_utf8_refusal",
            "unicode_bom_conflict_and_exact_save",
        }, checks);
        Assert.Equal("D6D0CEC40D0A", Convert.ToHexString(await File.ReadAllBytesAsync(temp.File("Gbk.txt"))));
        Assert.Equal("D6D0CEC40D0A9439FC36", Convert.ToHexString(await File.ReadAllBytesAsync(temp.File("Gb18030.txt"))));
        Assert.Equal("A4A4A4E50D0A", Convert.ToHexString(await File.ReadAllBytesAsync(temp.File("Big5.txt"))));
        Assert.Equal("FFFE2D4E87650D000A00", Convert.ToHexString(await File.ReadAllBytesAsync(temp.File("unicode.txt"))));
        Assert.Equal(4, Directory.GetFiles(temp.Path).Length);
    }
}
