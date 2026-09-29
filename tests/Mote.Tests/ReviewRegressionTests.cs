using System.Text;
using Mote.Engine;

namespace Mote.Tests;

/// <summary>Regression tests for data-loss paths identified in code review.</summary>
public sealed class ReviewRegressionTests
{
    /// <summary>
    /// Equal-length external edits must not be hidden by a restored modification time.
    /// Filesystem timestamps are metadata, not a content identity.
    /// </summary>
    [Fact]
    public async Task Save_rejects_external_content_change_with_same_length_and_timestamp()
    {
        using var temp = new RepoTemp();
        var path = temp.File("conflict.txt");
        await File.WriteAllTextAsync(path, "AAAA", new UTF8Encoding(false));
        using var document = await Document.OpenAsync(path);
        document.Apply(new TextChange(0, 4, "CCCC"));

        var originalTime = File.GetLastWriteTimeUtc(path);
        await File.WriteAllTextAsync(path, "BBBB", new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(path, originalTime);

        await Assert.ThrowsAsync<IOException>(() => document.SaveAsync());
        Assert.Equal("BBBB", await File.ReadAllTextAsync(path));
        Assert.True(document.IsModified);
    }
}
