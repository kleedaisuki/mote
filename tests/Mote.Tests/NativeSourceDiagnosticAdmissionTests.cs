using System.Reflection;
using Mote.Native;
using Xunit.Abstractions;

namespace Mote.Tests;

/// <summary>Checks the actual path admission helper without running the native probe or changing global state.</summary>
public sealed class NativeSourceDiagnosticAdmissionTests(ITestOutputHelper output)
{
    /// <summary>A fresh path under either admitted artifact area is accepted without creating it.</summary>
    [Theory]
    [InlineData(".cache")]
    [InlineData(".temp")]
    public void FreshLeafIsAdmittedWithoutAnyArtifacts(string area)
    {
        var root = RepositoryRoot();
        var parent = Path.Combine(root, area, "native-source-admission");
        Directory.CreateDirectory(parent);
        var leaf = Path.Combine(parent, Guid.NewGuid().ToString("N"), "output");
        var before = Directory.GetFileSystemEntries(parent).OrderBy(item => item).ToArray();
        var supplied = area == ".cache" ? Path.GetRelativePath(root, leaf) : leaf;
        Assert.Equal(Path.GetFullPath(leaf), Admit(supplied));
        Assert.False(Directory.Exists(leaf));
        Assert.False(File.Exists(leaf));
        Assert.Equal(before, Directory.GetFileSystemEntries(parent).OrderBy(item => item).ToArray());
    }

    /// <summary>Admission must not overwrite an existing file or reuse an existing directory.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingOutputIsRejected(bool file)
    {
        var scratch = Scratch();
        var path = Path.Combine(scratch, "existing");
        if (file) File.WriteAllText(path, "owned sentinel");
        else Directory.CreateDirectory(path);
        AssertFailure(path, "artifact-already-exists");
        if (file) Assert.Equal("owned sentinel", File.ReadAllText(path));
        else Assert.Empty(Directory.GetFileSystemEntries(path));
    }

    /// <summary>A non-directory ancestor is not equivalent to a missing directory.</summary>
    [Fact]
    public void FileAncestorIsRejectedAndUnchanged()
    {
        var parent = Path.Combine(Scratch(), "file-parent");
        File.WriteAllText(parent, "owned sentinel");
        AssertFailure(Path.Combine(parent, "child", "output"), "artifact-ancestor-file");
        Assert.Equal("owned sentinel", File.ReadAllText(parent));
    }

    /// <summary>Lexical containment includes the separator and canonicalizes parent traversal.</summary>
    [Theory]
    [InlineData("root")]
    [InlineData("area-itself")]
    [InlineData("prefix-lookalike")]
    [InlineData("traversal")]
    public void NonAdmittedAreasAreRejectedWithoutWriting(string shape)
    {
        var root = RepositoryRoot();
        var path = shape switch
        {
            "root" => root,
            "area-itself" => Path.Combine(root, ".temp"),
            "prefix-lookalike" => Path.Combine(root, ".temp-not-admitted", "never-created"),
            _ => Path.Combine(root, ".temp", "..", "never-created-admission-output")
        };
        AssertFailure(path, "artifact-boundary-invalid");
    }

    /// <summary>Existing linked ancestors cannot redirect an accepted lexical path.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryLinkAncestorIsRejectedIncludingDanglingTarget(bool dangling)
    {
        var scratch = Scratch();
        var target = Path.Combine(scratch, "owned-target");
        var link = Path.Combine(scratch, "owned-link");
        if (!dangling) Directory.CreateDirectory(target);
        CreateOwnedDirectoryLinkOrSkip(link, target);
        try
        {
            Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
            AssertFailure(Path.Combine(link, "output"), "artifact-ancestor-linked");
            if (dangling) Assert.False(Directory.Exists(target));
            else Assert.Empty(Directory.GetFileSystemEntries(target));
        }
        finally
        {
            // The known owned link alone is deleted, never its target or descendants.
            Directory.Delete(link, recursive: false);
        }
    }

    /// <summary>A linked output itself is rejected, even if its target does not exist.</summary>
    [Fact]
    public void DanglingOutputLinkIsRejected()
    {
        var scratch = Scratch();
        var link = Path.Combine(scratch, "output-link");
        var target = Path.Combine(scratch, "missing-target");
        CreateOwnedDirectoryLinkOrSkip(link, target);
        try
        {
            AssertFailure(link, "artifact-ancestor-linked");
            Assert.False(Directory.Exists(target));
        }
        finally { Directory.Delete(link, recursive: false); }
    }

    /// <summary>Discovers the real repository without changing the test process working directory.</summary>
    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, ".git")))
            current = current.Parent;
        Assert.NotNull(current);
        return current.FullName;
    }
    /// <summary>Creates only a unique repository-owned fixture directory.</summary>
    private static string Scratch()
    {
        var path = Path.Combine(RepositoryRoot(), ".temp", "native-source-admission",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Invokes only the private pure admission helper, never Run or the command-line entry point.</summary>
    private static string Admit(string path)
    {
        var method = typeof(NativeSourceCapabilityProbe).GetMethod("AdmitDirectory",
            BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(string), typeof(string)], null);
        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(null, [path, RepositoryRoot()]));
    }

    /// <summary>Checks the closed production failure identifier rather than a user-supplied exception message.</summary>
    private static void AssertFailure(string path, string expectedCode)
    {
        var outer = Assert.Throws<TargetInvocationException>(() => Admit(path));
        var inner = Assert.IsAssignableFrom<Exception>(outer.InnerException);
        Assert.Equal("ProbeFailure", inner.GetType().Name);
        var code = inner.GetType().GetProperty("Code", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(code);
        Assert.Equal(expectedCode, code.GetValue(inner));
    }

    /// <summary>Missing unprivileged link support is environmental unknown, never permission escalation.</summary>
    private void CreateOwnedDirectoryLinkOrSkip(string link, string target)
    {
        try { Directory.CreateSymbolicLink(link, target); }
        catch (Exception error) when (error is UnauthorizedAccessException or PlatformNotSupportedException ||
            error is IOException && error.HResult == unchecked((int)0x80070522))
        {
            output.WriteLine("Reparse admission unexercised: unprivileged symbolic-link creation unavailable.");
            throw Xunit.Sdk.SkipException.ForSkip("Unprivileged symbolic-link capability unavailable; no privileges changed.");
        }
    }
}

