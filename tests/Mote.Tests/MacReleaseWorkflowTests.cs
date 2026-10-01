using Mote.Native.Mac;

namespace Mote.Tests;

/// <summary>Portable diagnostic guards only. These tests do not execute AppKit or certify user input.</summary>
public sealed class MacReleaseWorkflowTests
{
    /// <summary>Multiple-file Finder requests are rejected explicitly instead of selecting the first or last path.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(100, false)]
    public void External_file_count_requires_one_document(int count, bool expected) =>
        Assert.Equal(expected, MacExternalOpenModel.AcceptFileCount((nuint)count));

    /// <summary>The workflow must not silently edit a missing or ambiguous fixture marker.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("MOTE-RELEASE-ORIGINAL")]
    [InlineData("mote-release-original mote-release-original")]
    public void Missing_or_multiple_markers_are_rejected(string source) =>
        Assert.Throws<ArgumentException>(() => MacReleaseWorkflowProbe.ValidateMarker(source));

    /// <summary>A Unicode prefix and structured-text punctuation do not make the unique ordinal target ambiguous.</summary>
    [Theory]
    [InlineData("中文😀\n{\"task\":\"mote-release-original\"}")]
    [InlineData("task = \"mote-release-original\"\r\n")]
    [InlineData("name,task\n\"甲\",\"mote-release-original\"\n")]
    public void One_exact_marker_is_accepted(string source) => MacReleaseWorkflowProbe.ValidateMarker(source);

    /// <summary>Diagnostic Save As cannot target a root-level file or a missing parent directory.</summary>
    [Theory]
    [InlineData("release-output.txt")]
    [InlineData(".temp/nested/release-output.txt")]
    [InlineData("docs/release-output.txt")]
    public void Non_direct_child_outputs_are_rejected(string relative) =>
        Assert.Throws<ArgumentException>(() => MacReleaseWorkflowProbe.ValidateOutput(Path.GetFullPath(Path.Combine(RepositoryRoot(), relative)), RepositoryRoot()));

    /// <summary>Hosted six-format tasks may use isolated nested repository artifact directories.</summary>
    [Theory]
    [InlineData(".temp")]
    [InlineData(".cache")]
    public void Existing_real_nested_artifact_directory_is_accepted(string root)
    {
        var directory = Path.GetFullPath(Path.Combine(RepositoryRoot(), root, "mac-release-guards", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        MacReleaseWorkflowProbe.ValidateOutput(Path.Combine(directory, "edited.txt"), RepositoryRoot());
    }

    /// <summary>Diagnostic fixture isolation never authorizes overwriting an existing destination.</summary>
    [Fact]
    public void Existing_output_is_rejected()
    {
        var directory = Path.GetFullPath(Path.Combine(RepositoryRoot(), ".temp", "mac-release-guards", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "existing.txt");
        File.WriteAllText(path, "retained guard fixture");
        Assert.Throws<ArgumentException>(() => MacReleaseWorkflowProbe.ValidateOutput(path, RepositoryRoot()));
    }

    /// <summary>Locates the repository without mutating process-wide current-directory state.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) && Directory.Exists(Path.Combine(directory.FullName, "src")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository fixture root unavailable.");
    }
}
