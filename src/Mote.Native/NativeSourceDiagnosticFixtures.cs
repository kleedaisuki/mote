using System.Text;
using Mote.Formats;

namespace Mote.Native;

/// <summary>Generated nonprivate full-source fixture; byte size and UTF-16 extent are distinct.</summary>
internal sealed record NativeSourceDiagnosticFixture(string Id, string Text,
    int EditOffset, IDocumentPolicy Policy);

/// <summary>Three fixed ordinary-file tasks, not a new large-file benchmark suite.</summary>
internal static class NativeSourceDiagnosticFixtures
{
    /// <summary>Original synthetic insertion; supplementary text exercises canonical UTF-16 offsets.</summary>
    internal const string Insertion = "新🧪";
    /// <summary>Approximate user-reported 3.54 MiB, expressed as exact UTF-8 bytes, not characters.</summary>
    private const int NovelBytes = 3_711_959;
    /// <summary>Dense structured fixture measured independently of the novel's reading shape.</summary>
    private const int JsonBytes = 512 * 1024;
    /// <summary>Unambiguous ASCII insertion context; no user data is accepted by this generator.</summary>
    private const string Marker = "EDIT_TARGET";

    /// <summary>Builds one fixture at a time so there is no queue of three resident source copies.</summary>
    internal static NativeSourceDiagnosticFixture Create(int index)
    {
        var text = index switch
        {
            0 => MixedText(),
            1 => Novel(),
            2 => DenseJson(),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
        return new(index switch { 0 => "mixed-text", 1 => "novel-text", _ => "dense-json" },
            text, text.IndexOf(Marker, StringComparison.Ordinal),
            index == 2 ? new JsonPolicy() : new PlainTextPolicy());
    }

    /// <summary>Includes original mixed-script context and all three canonical newline spellings.</summary>
    private static string MixedText()
    {
        var text = new StringBuilder("alpha\r\n" + Marker + "\n");
        for (var i = 0; i < 36; i++)
            text.Append("iW中🧪e\u0301مرحبا\t 原创测试段落：阅读、选择与精确修改。")
                .Append(i % 3 == 0 ? "\r\n" : i % 3 == 1 ? "\n" : "\r");
        return text.ToString();
    }

    /// <summary>Creates normal synthetic paragraphs with an exact UTF-8 byte target.</summary>
    private static string Novel()
    {
        const string paragraph = "这是原创的合成阅读段落。窗边的小灯照着纸页，读者停下片刻，再继续向下一段走去。\n\n";
        var text = new StringBuilder(Marker + "\n\n");
        var bytes = Encoding.UTF8.GetByteCount(text.ToString());
        var paragraphBytes = Encoding.UTF8.GetByteCount(paragraph);
        while (bytes + paragraphBytes <= NovelBytes)
        {
            text.Append(paragraph);
            bytes += paragraphBytes;
        }
        text.Append(' ', NovelBytes - bytes);
        return text.ToString();
    }

    /// <summary>Creates valid dense JSON; padding is legal whitespace, not removed semantic content.</summary>
    private static string DenseJson()
    {
        const string row = "{\"name\":\"中文 é\",\"enabled\":true,\"count\":123,\"items\":[1,2,3]}";
        const string suffix = "\n]}\n";
        var text = new StringBuilder("{\"edit_target\":\"" + Marker + "\",\"rows\":[\n");
        var bytes = Encoding.UTF8.GetByteCount(text.ToString());
        var rowBytes = Encoding.UTF8.GetByteCount(row);
        var first = true;
        while (bytes + rowBytes + (first ? 0 : 2) + suffix.Length <= JsonBytes)
        {
            if (!first) { text.Append(",\n"); bytes += 2; }
            text.Append(row);
            bytes += rowBytes;
            first = false;
        }
        text.Append(suffix);
        bytes += suffix.Length;
        text.Append(' ', JsonBytes - bytes);
        return text.ToString();
    }
}
