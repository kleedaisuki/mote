using Mote.Native;

namespace Mote.Tests;

/// <summary>Grid-independent controller source clipboard safety contracts.</summary>
public sealed partial class NativeControllerTests
{
    /// <summary>Copy/Cut refuse NUL before clipboard publication and preserve the complete engine document.</summary>
    [Theory]
    [InlineData("\0alpha", false)]
    [InlineData("alpha\0omega", false)]
    [InlineData("omega\0", false)]
    [InlineData("\0alpha", true)]
    [InlineData("alpha\0omega", true)]
    [InlineData("omega\0", true)]
    public async Task Source_clipboard_nul_guard_refuses_before_publication(string source, bool cut)
    {
        using var temp = new RepoTemp();
        var path = temp.File("source-clipboard-nul.txt");
        await File.WriteAllTextAsync(path, source);
        var shell = new FakeShell(NativeLineEndingMode.Preserve);
        shell.SetClipboardText("sentinel");
        var publicationBaseline = shell.ClipboardSetCalls;
        // If publication is reached, FakeShell throws before overwriting the sentinel;
        // the controller then emits a different clipboard-rejection diagnostic.
        shell.RejectClipboard = true;
        using var controller = NewController(shell, temp.Path, path);
        controller.Run();
        await shell.PumpUntilAsync(() => shell.Document?.Text == source);
        var stamp = shell.Document!.Stamp;
        Assert.Equal(0, stamp.Version);
        shell.RequestSelectAll();
        if (cut) shell.RequestCut();
        else shell.RequestCopy();
        await shell.PumpUntilAsync(() => shell.Errors.Count > 0);
        Assert.Equal("Copy refused: the selection contains U+0000; the clipboard and document were not changed.",
            Assert.Single(shell.Errors));
        Assert.Equal("sentinel", shell.ClipboardText);
        Assert.Equal(publicationBaseline, shell.ClipboardSetCalls);
        Assert.Equal(source, shell.Document!.Text);
        Assert.Equal(source.Length, shell.Document.TotalLength);
        Assert.Equal(stamp, shell.Document.Stamp);
        Assert.False(shell.Document.IsModified);
        Assert.Equal(source, await File.ReadAllTextAsync(path));
    }
}
