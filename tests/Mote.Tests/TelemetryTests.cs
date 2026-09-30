using System.Text.Json;
using System.Security;
using System.Collections;
using Mote.Telemetry;

namespace Mote.Tests;

/// <summary>Serializes tests of process-wide telemetry configuration.</summary>
[CollectionDefinition("Telemetry", DisableParallelization = true)]
public sealed class TelemetryCollection;

/// <summary>Privacy, schema, correlation, and bounded-queue tests for opt-in local tracing.</summary>
[Collection("Telemetry")]
public sealed class TelemetryTests
{
    /// <summary>Disabled instrumentation writes nothing and reports inactive health.</summary>
    [Fact]
    public async Task Disabled_mode_creates_no_trace_file()
    {
        using var temp = new RepoTemp();
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = false, OutputDirectory = temp.Path });
        using (MoteTelemetry.Start(TelemetryOperation.Startup)) { }
        MoteTelemetry.Record(TelemetryEvent.EditCommitted, 42);
        MoteTelemetry.RecordSaveFailure(new IOException("private-source-name.txt", unchecked((int)0x80070497)));
        await MoteTelemetry.ShutdownAsync();
        Assert.False(MoteTelemetry.Health.Enabled);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path));
    }

    /// <summary>Persisted JSONL is allowlisted, path-free, and carries correlated spans.</summary>
    [Fact]
    public async Task Enabled_trace_has_private_schema_and_parent_correlation()
    {
        using var temp = new RepoTemp();
        var output = temp.File("private-document-name-SECRET.md");
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = output });
        try
        {
            using (MoteTelemetry.Start(TelemetryOperation.OpenToEditable,
                new TelemetryDimensions(TelemetryFormat.Markdown, DocumentBytes: 2048, Version: 1)))
            {
                var mark = MoteTelemetry.Mark();
                using (var decode = MoteTelemetry.Start(TelemetryOperation.DocumentDecode))
                    decode?.SetStatus(TelemetryStatus.Failure);
                MoteTelemetry.Record(TelemetryEvent.EditCommitted, value: 3);
                MoteTelemetry.RecordElapsed(TelemetryOperation.EditToPaint, mark);
            }
        }
        finally { await MoteTelemetry.ShutdownAsync(); }

        var lines = await ReadLinesAsync(output);
        Assert.True(lines.Length >= 3);
        Assert.DoesNotContain("SECRET", string.Join("\n", lines), StringComparison.OrdinalIgnoreCase);
        var records = lines.Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            var rootFields = new[] { "schema_version", "utc_time", "session_id", "trace_id", "span_id", "parent_span_id", "operation", "duration_us", "status", "attributes" };
            foreach (var record in records)
            {
                var root = record.RootElement;
                Assert.Equal(rootFields.Order(), root.EnumerateObject().Select(p => p.Name).Order());
                Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
                Assert.InRange(root.GetProperty("duration_us").GetInt64(), 0, long.MaxValue);
                foreach (var attribute in root.GetProperty("attributes").EnumerateObject())
                    Assert.Contains(attribute.Name, new[] { "format", "size_bucket", "version", "count", "hresult" });
            }
            var parent = records.Single(r => r.RootElement.GetProperty("operation").GetString() == "document.open_to_editable").RootElement;
            var child = records.Single(r => r.RootElement.GetProperty("operation").GetString() == "document.decode").RootElement;
            Assert.Equal(parent.GetProperty("trace_id").GetString(), child.GetProperty("trace_id").GetString());
            Assert.Equal(parent.GetProperty("span_id").GetString(), child.GetProperty("parent_span_id").GetString());
            Assert.Equal("failure", child.GetProperty("status").GetString());
            var delayed = records.Single(r => r.RootElement.GetProperty("operation").GetString() == "document.edit_to_paint").RootElement;
            Assert.Equal(parent.GetProperty("trace_id").GetString(), delayed.GetProperty("trace_id").GetString());
            Assert.Equal(parent.GetProperty("span_id").GetString(), delayed.GetProperty("parent_span_id").GetString());
            Assert.Equal("markdown", parent.GetProperty("attributes").GetProperty("format").GetString());
            Assert.Equal("1-4KiB", parent.GetProperty("attributes").GetProperty("size_bucket").GetString());
        }
        finally { foreach (var record in records) record.Dispose(); }
    }

    /// <summary>Only the allowlisted Save phase and numeric filesystem code enter JSONL.</summary>
    [Fact]
    public async Task Save_failure_trace_excludes_exception_message_and_arbitrary_data()
    {
        using var temp = new RepoTemp();
        var output = temp.File("trace");
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = output });
        try
        {
            var error = new IOException("SECRET-user-file-path.txt", unchecked((int)0x80070497));
            error.Data["Mote.Engine.SavePhase"] = "Replace";
            error.Data["private"] = "SECRET-document-content";
            MoteTelemetry.RecordSaveFailure(error);

            var unknown = new IOException("SECRET-other-path", unchecked((int)0x80070020));
            unknown.Data["Mote.Engine.SavePhase"] = "SECRET-injected-phase";
            MoteTelemetry.RecordSaveFailure(unknown);
            MoteTelemetry.RecordSaveFailure(new InvalidOperationException("SECRET-nonfilesystem"));
        }
        finally { await MoteTelemetry.ShutdownAsync(); }

        var json = string.Join("\n", await ReadLinesAsync(output));
        Assert.DoesNotContain("SECRET", json, StringComparison.Ordinal);
        using var replace = JsonDocument.Parse((await ReadLinesAsync(output)).Single(
            line => line.Contains("\"operation\":\"save.failure.replace\"", StringComparison.Ordinal)));
        Assert.Equal("failure", replace.RootElement.GetProperty("status").GetString());
        Assert.Equal(unchecked((int)0x80070497),
            replace.RootElement.GetProperty("attributes").GetProperty("hresult").GetInt32());
        var records = (await ReadLinesAsync(output)).Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            Assert.Single(records, record => record.RootElement.GetProperty("operation").GetString() == "save.failure.unknown");
            Assert.DoesNotContain(records, record => record.RootElement.GetProperty("operation").GetString() == "save.failure.SECRET-injected-phase");
        }
        finally { foreach (var record in records) record.Dispose(); }
    }

    /// <summary>An exception with unavailable Data still records its primary code safely, without leaking diagnostic errors.</summary>
    [Fact]
    public async Task Save_failure_trace_rejecting_data_records_unknown_primary_code()
    {
        using var temp = new RepoTemp();
        var output = temp.File("trace");
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = output });
        try { MoteTelemetry.RecordSaveFailure(new RejectingSaveDataException()); }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var lines = await ReadLinesAsync(output);
        var json = string.Join("\n", lines);
        Assert.DoesNotContain("SECRET", json, StringComparison.Ordinal);
        using var record = JsonDocument.Parse(lines.Single(
            line => line.Contains("\"operation\":\"save.failure.unknown\"", StringComparison.Ordinal)));
        Assert.Equal(unchecked((int)0x80070498), record.RootElement.GetProperty("attributes").GetProperty("hresult").GetInt32());
    }

    /// <summary>Models provider diagnostics that refuse reads, without altering the original filesystem code.</summary>
    private sealed class RejectingSaveDataException() : IOException("SECRET-save-path", unchecked((int)0x80070498))
    {
        public override IDictionary Data => throw new SecurityException("SECRET-data-access");
    }

    /// <summary>Concurrent producers do not block indefinitely; queue loss is accounted for.</summary>
    [Fact]
    public async Task Bounded_queue_accounts_for_dropped_events_and_flushes_on_shutdown()
    {
        using var temp = new RepoTemp();
        var output = temp.File("trace");
        MoteTelemetry.Configure(new TelemetryOptions
        {
            Enabled = true, OutputDirectory = output, QueueCapacity = 8,
            MaxFileBytes = 4096, MaxFilesPerSession = 128
        });
        TelemetryHealth health;
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 10_000; i++) MoteTelemetry.Record(TelemetryEvent.EditCommitted, i);
            })));
            health = MoteTelemetry.Health;
            Assert.True(health.Enabled);
            Assert.False(health.SinkFaulted);
            Assert.True(health.DroppedRecords > 0, "Small bounded queue should drop records under concurrent load.");
        }
        finally { await MoteTelemetry.ShutdownAsync(TimeSpan.FromSeconds(20)); }
        Assert.False(MoteTelemetry.Health.Enabled);
        var lines = await ReadLinesAsync(output);
        Assert.NotEmpty(lines);
        Assert.Contains(lines, line => line.Contains("\"operation\":\"telemetry.dropped\"", StringComparison.Ordinal));
    }

    /// <summary>A child started later links to the delayed parent span, not merely its trace.</summary>
    [Fact]
    public async Task Cross_callback_child_preserves_explicit_parent_span()
    {
        using var temp = new RepoTemp();
        var output = temp.File("trace");
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = output });
        try
        {
            var mark = MoteTelemetry.Mark();
            await Task.Run(() =>
            {
                using var child = MoteTelemetry.StartChild(TelemetryOperation.AnalysisParse, mark);
            });
            MoteTelemetry.RecordElapsed(TelemetryOperation.EditToAnalysis, mark);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = (await ReadLinesAsync(output)).Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            var parent = records.Single(r => r.RootElement.GetProperty("operation").GetString() == "document.edit_to_analysis").RootElement;
            var child = records.Single(r => r.RootElement.GetProperty("operation").GetString() == "analysis.parse").RootElement;
            Assert.Equal(parent.GetProperty("trace_id").GetString(), child.GetProperty("trace_id").GetString());
            Assert.Equal(parent.GetProperty("span_id").GetString(), child.GetProperty("parent_span_id").GetString());
        }
        finally { foreach (var record in records) record.Dispose(); }
    }

    /// <summary>Reads every retained JSONL row without assuming one file per session.</summary>
    private static async Task<string[]> ReadLinesAsync(string directory)
    {
        var files = Directory.GetFiles(directory, "*.jsonl");
        Assert.NotEmpty(files);
        var lines = new List<string>();
        foreach (var file in files) lines.AddRange(await File.ReadAllLinesAsync(file));
        return lines.ToArray();
    }
}
