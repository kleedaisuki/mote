using System.Reflection;
using System.Text.Json;
using Mote.Configuration;
using Mote.Engine;
using Mote.Native;
using Mote.Telemetry;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Protects the established schema-1 native Save failure contract alongside typed phases.</summary>
[Collection("Telemetry")]
public sealed class NativeSaveFailureCompatibilityTests
{
    /// <summary>The actual controller emits one legacy failure only on filesystem failure, without changing disk policy.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Native_replace_keeps_legacy_failure_and_typed_phase(bool fail)
    {
        using var temp = new RepoTemp();
        var target = temp.File("SECRET-target.txt");
        await File.WriteAllTextAsync(target, "SECRET-original");
        MoteTelemetry.Configure(new TelemetryOptions { Enabled = true, OutputDirectory = temp.Path });
        try
        {
            var shell = (INativeEditorShell)Activator.CreateInstance(
                typeof(NativeControllerTests).GetNestedType("FakeShell", BindingFlags.NonPublic)!,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [NativeLineEndingMode.Preserve], null)!;
            var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
                { UserHomeDirectory = temp.Path, UseEnvironmentOverride = false });
            using var controller = new NativeEditorController(shell, config, ThemePolicies.Get(config.ThemeId), null);
            var document = await Document.OpenAsync(target);
            Invoke(controller, "ReplaceDocument", document, default(TelemetryMark), 0);
            document.Apply(new TextChange(0, 0, "X"));
            if (fail) document.SaveOperations = new FailingReplace();
            Invoke(controller, "StartSave", NativeSaveRequest.Receive(NativeSaveKind.Save));
            await ((Task)shell.GetType().GetMethod("WaitForPostedAsync")!.Invoke(shell, null)!)
                .WaitAsync(TimeSpan.FromSeconds(10));
            shell.GetType().GetMethod("Pump")!.Invoke(shell, null);
            Assert.False((bool)typeof(NativeEditorController).GetField("_saving",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!);
            Assert.Equal(fail ? "SECRET-original" : "XSECRET-original", await File.ReadAllTextAsync(target));
            Assert.Equal(fail, document.IsModified);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var rows = Directory.GetFiles(temp.Path, "*.jsonl").SelectMany(File.ReadLines)
            .Select(line => { using var json = JsonDocument.Parse(line); return json.RootElement.Clone(); }).ToArray();
        var phase = Assert.Single(rows, r => Operation(r) == "save.commit_replace");
        Assert.Equal(fail ? "failure" : "success", phase.GetProperty("status").GetString());
        var legacy = rows.Where(r => Operation(r).StartsWith("save.failure.", StringComparison.Ordinal)).ToArray();
        if (fail)
        {
            var failure = Assert.Single(legacy);
            Assert.Equal("save.failure.replace", Operation(failure));
            Assert.Equal("failure", failure.GetProperty("status").GetString());
            foreach (var row in new[] { failure, phase })
            {
                Assert.Equal(-2147024864, row.GetProperty("attributes").GetProperty("hresult").GetInt32());
                Assert.Equal(1, row.GetProperty("attributes").GetProperty("version").GetInt64());
            }
            var saveEntry = Assert.Single(rows, r => Operation(r) == "document.save.entered");
            var phaseEntry = Assert.Single(rows, r => Operation(r) == "save.commit_replace.entered");
            Assert.Equal(saveEntry.GetProperty("span_id").GetString(), failure.GetProperty("parent_span_id").GetString());
            Assert.Equal(saveEntry.GetProperty("span_id").GetString(), phaseEntry.GetProperty("parent_span_id").GetString());
            Assert.Equal(phaseEntry.GetProperty("span_id").GetString(), phase.GetProperty("parent_span_id").GetString());
            Assert.Equal(phase.GetProperty("trace_id").GetString(), failure.GetProperty("trace_id").GetString());
        }
        else Assert.Empty(legacy);
        Assert.DoesNotContain("SECRET", string.Join('\n', rows.Select(r => r.GetRawText())));
    }

    /// <summary>Models the observed sharing violation without requiring an OS lock or changing the Save oracle.</summary>
    private sealed class FailingReplace : DocumentSaveOperations
    {
        /// <inheritdoc />
        internal override void Replace(string stage, string target) =>
            throw new IOException("SECRET-provider-error", unchecked((int)0x80070020));
    }

    /// <summary>Invokes the shipped controller without adding a production test-only API.</summary>
    private static void Invoke(NativeEditorController controller, string method, params object?[] args) =>
        typeof(NativeEditorController).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(controller, args);

    /// <summary>Reads only the stable operation field.</summary>
    private static string Operation(JsonElement row) => row.GetProperty("operation").GetString()!;
}
