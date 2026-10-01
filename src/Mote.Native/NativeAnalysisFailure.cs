namespace Mote.Native;

/// <summary>Closed, content-free categories for a failed native analysis presentation.</summary>
internal enum NativeAnalysisFailureCategory
{
    /// <summary>Invalid arguments or coordinate boundaries were rejected.</summary>
    Argument,
    /// <summary>The native presentation could not satisfy its current state contract.</summary>
    InvalidState,
    /// <summary>A managed I/O operation failed without exporting its path or message.</summary>
    Io,
    /// <summary>A managed native-interop exception supplied a numeric HResult.</summary>
    NativeInterop,
    /// <summary>A failure outside the closed known categories; no arbitrary type name is exported.</summary>
    Other
}

/// <summary>One bounded attempted-analysis failure witness; no exception, message, source or path is retained.</summary>
/// <param name="Stamp">Analysis generation/version captured before background dispatch, not the later UI document.</param>
/// <param name="AnalysisSerial">The failed presentation's captured request serial.</param>
/// <param name="Category">Closed classifier, independent of potentially private exception text.</param>
/// <param name="HResult">The original numeric managed exception code, not a synthesized success or retry reason.</param>
internal readonly record struct NativeAnalysisFailure(NativeDocumentStamp Stamp, long AnalysisSerial,
    NativeAnalysisFailureCategory Category, int HResult)
{
    /// <summary>Distinguishes this observed failure from a newer document lifetime or analysis request.</summary>
    internal bool Matches(NativeDocumentStamp stamp, long serial) => Stamp == stamp && AnalysisSerial == serial;

    /// <summary>Classifies the concrete caught exception without exporting arbitrary type names or messages.</summary>
    internal static NativeAnalysisFailure Capture(NativeDocumentStamp stamp, long serial, Exception error) =>
        new(stamp, serial, error switch
        {
            ArgumentException => NativeAnalysisFailureCategory.Argument,
            InvalidOperationException => NativeAnalysisFailureCategory.InvalidState,
            IOException => NativeAnalysisFailureCategory.Io,
            System.Runtime.InteropServices.ExternalException => NativeAnalysisFailureCategory.NativeInterop,
            _ => NativeAnalysisFailureCategory.Other
        }, error.HResult);
}
