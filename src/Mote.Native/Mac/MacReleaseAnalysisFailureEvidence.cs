using System.Text.Json;

namespace Mote.Native.Mac;

/// <summary>Preserves a historical presentation failure without assigning it to a newer successful document/request.</summary>
internal static class MacReleaseAnalysisFailureEvidence
{
    /// <summary>Writes only closed category, numeric codes and explicit current-identity qualification.</summary>
    internal static void Write(string path, NativeAnalysisFailure failure, NativeDocumentStamp? currentStamp, long currentSerial)
    {
        MacReleaseWorkflowProbe.ValidateOutput(path);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteNumber("schema_version", 1);
        WriteFields(writer, failure, currentStamp, currentSerial);
        writer.WriteEndObject();
    }

    /// <summary>Shared fixed fields make refusal and success-side historical evidence agree on version/serial meaning.</summary>
    internal static void WriteFields(Utf8JsonWriter writer, NativeAnalysisFailure? failure,
        NativeDocumentStamp? currentStamp, long currentSerial)
    {
        writer.WriteBoolean("analysis_failure_present", failure.HasValue);
        writer.WriteString("analysis_failure_category", failure?.Category.ToString() ?? "none");
        writer.WriteNumber("analysis_failure_hresult", failure?.HResult ?? 0);
        writer.WriteNumber("analysis_failure_generation", failure?.Stamp.Generation ?? 0);
        writer.WriteNumber("analysis_failure_version", failure?.Stamp.Version ?? 0);
        writer.WriteNumber("analysis_failure_serial", failure?.AnalysisSerial ?? 0);
        writer.WriteNumber("current_analysis_serial", currentSerial);
        writer.WriteBoolean("analysis_failure_current_match", failure is { } observed && currentStamp is { } current &&
            observed.Matches(current, currentSerial));
    }
}
