using System.Text.Json;
using Mote.Telemetry;

namespace Mote.Tests;

/// <summary>Product source vocabulary, numeric compatibility, and content-free duration contracts.</summary>
[Collection("Telemetry")]
public sealed class NativeSourceTelemetryTests
{
    /// <summary>New operations append after every established operation without renumbering it.</summary>
    [Fact]
    public void Source_operations_preserve_established_numeric_identifiers()
    {
        var established = new[] { TelemetryOperation.Startup, TelemetryOperation.OpenToEditable,
            TelemetryOperation.EditToAnalysis, TelemetryOperation.EditToPresentation,
            TelemetryOperation.AnalysisToPresentation, TelemetryOperation.EditToPaint,
            TelemetryOperation.Save, TelemetryOperation.DocumentOpen, TelemetryOperation.DocumentDecode,
            TelemetryOperation.DocumentEdit, TelemetryOperation.AnalysisParse,
            TelemetryOperation.AnalysisSemantic, TelemetryOperation.AnalysisPublish,
            TelemetryOperation.ViewLayout, TelemetryOperation.ViewPaint,
            TelemetryOperation.StartupToEditable, TelemetryOperation.EditToDrawSubmission,
            TelemetryOperation.OpenToDrawSubmission, TelemetryOperation.CommandSave,
            TelemetryOperation.CommandSaveAs, TelemetryOperation.SaveGateWait,
            TelemetryOperation.SaveSnapshotCapture, TelemetryOperation.SaveTargetCheck,
            TelemetryOperation.SaveTempEncodeWrite, TelemetryOperation.SaveTempFlush,
            TelemetryOperation.SaveTempHash, TelemetryOperation.SaveFinalTargetCheck,
            TelemetryOperation.SaveCommitMove, TelemetryOperation.SaveCommitReplace,
            TelemetryOperation.SaveSavedStamp, TelemetryOperation.SaveBookkeeping,
            TelemetryOperation.SaveFailureCleanup, TelemetryOperation.SaveFailureInspection };
        for (var i = 0; i < established.Length; i++)
            Assert.Equal(i, (int)established[i]);
        var operations = Operations();
        for (var i = 0; i < operations.Length; i++)
            Assert.Equal(33 + i, (int)operations[i]);
        Assert.Equal(38, Enum.GetValues<TelemetryOperation>().Length);
    }

    /// <summary>Default-off scopes return null without warmed allocation, activity, or files.</summary>
    [Fact]
    public async Task Disabled_source_operations_are_allocation_free()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = false, OutputDirectory = temp.Path });
        var operations = Operations();
        foreach (var operation in operations) Assert.Null(MoteTelemetry.Start(operation));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            foreach (var operation in operations)
                _ = MoteTelemetry.Start(operation);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        await MoteTelemetry.ShutdownAsync();
        Assert.Empty(Directory.GetFileSystemEntries(temp.Path));
    }

    /// <summary>Fixed names retain v1 fields, numeric dimensions, actual outcomes, and explicit parentage.</summary>
    [Fact]
    public async Task Source_scopes_emit_closed_names_statuses_and_parent_fields()
    {
        using var temp = new RepoTemp();
        var output = temp.File("SECRET-private-document.txt");
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = output });
        var statuses = new[] { TelemetryStatus.Success, TelemetryStatus.Failure,
            TelemetryStatus.Cancelled, TelemetryStatus.Skipped, TelemetryStatus.Success };
        try
        {
            using var parent = MoteTelemetry.Start(TelemetryOperation.DocumentEdit);
            var operations = Operations();
            for (var i = 0; i < operations.Length; i++)
            {
                using var scope = MoteTelemetry.Start(operations[i],
                    new TelemetryDimensions(TelemetryFormat.Json, 2048, 7, i));
                Assert.NotNull(scope);
                scope.SetStatus(statuses[i]);
            }
        }
        finally { await MoteTelemetry.ShutdownAsync(); }

        var lines = Directory.GetFiles(output, "*.jsonl").SelectMany(File.ReadAllLines).ToArray();
        Assert.DoesNotContain("SECRET", string.Join("\n", lines), StringComparison.Ordinal);
        var rows = lines.Select(line =>
        {
            using var parsed = JsonDocument.Parse(line);
            return parsed.RootElement.Clone();
        }).ToArray();
        var parentRow = Assert.Single(rows, row => Operation(row) == "document.edit");
        var sourceRows = rows.Where(row => Operation(row).StartsWith("native.source.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { "native.source.install", "native.source.readback", "native.source.reconcile",
            "native.source.range_publish", "native.source.style_publish" }, sourceRows.Select(Operation));
        Assert.Equal(new[] { "success", "failure", "cancelled", "skipped", "success" },
            sourceRows.Select(row => row.GetProperty("status").GetString()));
        Assert.Equal(5, sourceRows.Select(row => row.GetProperty("span_id").GetString()).Distinct().Count());
        foreach (var row in sourceRows)
        {
            Assert.Equal(new[] { "attributes", "duration_us", "operation", "parent_span_id", "schema_version",
                "session_id", "span_id", "status", "trace_id", "utc_time" },
                row.EnumerateObject().Select(property => property.Name).Order());
            Assert.Equal(1, row.GetProperty("schema_version").GetInt32());
            Assert.Equal(parentRow.GetProperty("span_id").GetString(), row.GetProperty("parent_span_id").GetString());
            Assert.Equal(parentRow.GetProperty("trace_id").GetString(), row.GetProperty("trace_id").GetString());
            Assert.Equal(parentRow.GetProperty("session_id").GetString(), row.GetProperty("session_id").GetString());
            Assert.InRange(row.GetProperty("duration_us").GetInt64(), 0, long.MaxValue);
            var attributes = row.GetProperty("attributes");
            Assert.Equal(new[] { "count", "format", "size_bucket", "version" },
                attributes.EnumerateObject().Select(property => property.Name).Order());
            Assert.Equal("json", attributes.GetProperty("format").GetString());
            Assert.Equal("1-4KiB", attributes.GetProperty("size_bucket").GetString());
            Assert.Equal(7, attributes.GetProperty("version").GetInt64());
        }
    }

    /// <summary>Returns the append-only production source operation order.</summary>
    private static TelemetryOperation[] Operations() =>
    [
        TelemetryOperation.NativeSourceInstall, TelemetryOperation.NativeSourceReadback,
        TelemetryOperation.NativeSourceReconcile, TelemetryOperation.NativeSourceRangePublish,
        TelemetryOperation.NativeSourceStylePublish
    ];

    /// <summary>Reads the already validated JSON operation string for local assertions.</summary>
    private static string Operation(JsonElement row) => row.GetProperty("operation").GetString()!;
}
