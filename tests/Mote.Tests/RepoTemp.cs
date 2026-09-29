namespace Mote.Tests;

/// <summary>Creates isolated test files inside the repository's .temp directory.</summary>
internal sealed class RepoTemp : IDisposable
{
    private readonly string _basePath;

    /// <summary>Creates a unique directory for one test and verifies its containment.</summary>
    public RepoTemp()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "mote.sln")))
            directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Cannot locate the mote repository root.");
        _basePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory.FullName, ".temp", "tests"));
        Directory.CreateDirectory(_basePath);
        Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(_basePath, Guid.NewGuid().ToString("N")));
        EnsureContained(Path);
        Directory.CreateDirectory(Path);
    }

    /// <summary>The unique directory for the current test.</summary>
    public string Path { get; }

    /// <summary>Builds a file path inside the test directory.</summary>
    public string File(string name)
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, name));
        if (!path.StartsWith(Path + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Test file must stay in its isolated directory.", nameof(name));
        return path;
    }

    /// <summary>Deletes only the verified isolated directory after the test finishes.</summary>
    public void Dispose()
    {
        EnsureContained(Path);
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }

    /// <summary>Rejects a target outside the repository-local test scratch directory.</summary>
    private void EnsureContained(string target)
    {
        if (!target.StartsWith(_basePath + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Recursive cleanup target escaped .temp/tests.");
    }
}
