using Mote.Native;

namespace Mote.Tests;

/// <summary>Failure observations retain identity and bounded categories, never arbitrary exception payloads.</summary>
public sealed class NativeAnalysisFailureTests
{
    /// <summary>Equal text versions in a new document cannot relabel an older caught failure as current.</summary>
    [Fact]
    public void Current_failure_requires_generation_version_and_analysis_serial()
    {
        var failure = NativeAnalysisFailure.Capture(new(3, 4), 9, new InvalidOperationException("private"));
        Assert.True(failure.Matches(new(3, 4), 9));
        Assert.False(failure.Matches(new(4, 4), 9));
        Assert.False(failure.Matches(new(3, 5), 9));
        Assert.False(failure.Matches(new(3, 4), 10));
    }

    /// <summary>Each category is fixed while the exact HResult and observed analysis identity remain intact.</summary>
    [Fact]
    public void Classification_is_closed_and_identity_preserving()
    {
        Exception[] failures = [new ArgumentException("private document content"),
            new InvalidOperationException("private path"), new IOException("private path"),
            new System.Runtime.InteropServices.ExternalException("private callback", -17),
            new NotSupportedException("private detail")];
        var categories = Enum.GetValues<NativeAnalysisFailureCategory>();
        for (var index = 0; index < failures.Length; index++)
        {
            var failure = NativeAnalysisFailure.Capture(new(3, 4), 9, failures[index]);
            Assert.Equal(categories[index], failure.Category);
            Assert.Equal(new NativeDocumentStamp(3, 4), failure.Stamp);
            Assert.Equal(9, failure.AnalysisSerial);
            Assert.Equal(failures[index].HResult, failure.HResult);
            Assert.DoesNotContain("private", failure.ToString());
        }
    }
}
