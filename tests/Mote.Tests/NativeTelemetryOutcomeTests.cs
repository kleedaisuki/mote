using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Mote.Configuration;
using Mote.Engine;
using Mote.Formats;
using Mote.Native;
using Mote.Telemetry;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Certifies controller outcome classification without native UI or trace payload secrets.</summary>
[Collection("Telemetry")]
public sealed class NativeTelemetryOutcomeTests
{
    /// <summary>Save As approval controls both disk mutation and the one live Save span.</summary>
    [Theory]
    [InlineData(false, "cancelled")]
    [InlineData(true, "success")]
    public async Task Save_as_overwrite_decision_has_truthful_outcome(bool approve, string expected)
    {
        using var temp = new RepoTemp();
        var target = temp.File("SECRET-target.txt");
        await File.WriteAllTextAsync(target, "SECRET-original");
        Configure(temp.Path);
        try
        {
            var shell = Shell();
            SetProperty(shell, "SavePath", target);
            SetProperty(shell, "OverwriteApproved", approve);
            using var controller = Controller(shell, temp.Path);
            Document(controller).Apply(new TextChange(0, 0, "SECRET-new"));
            Invoke(controller, "StartSave", true);
            await PumpUntil(shell, () => !(bool)Field(controller, "_saving")!);
            Assert.Equal(1, shell.GetType().GetProperty("OverwritePromptCount")!.GetValue(shell));
            Assert.Equal(approve ? "SECRET-new" : "SECRET-original", await File.ReadAllTextAsync(target));
            Assert.Equal(!approve, Document(controller).IsModified);
            Assert.Empty((IEnumerable<string>)shell.GetType().GetProperty("Errors")!.GetValue(shell)!);
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        var save = Assert.Single(records, r => Operation(r) == "document.save");
        Assert.Equal(expected, save.GetProperty("status").GetString());
        Assert.Equal(approve ? 1 : 0, records.Count(r => Operation(r) == "save.completed"));
        Assert.DoesNotContain("SECRET", string.Join('\n', records.Select(r => r.GetRawText())));
    }

    /// <summary>Projection and native installation throws retain their exception while recording Failure.</summary>
    [Theory]
    [InlineData("success")]
    [InlineData("projection")]
    [InlineData("native")]
    public async Task Idle_publication_is_success_only_after_complete_installation(string mode)
    {
        using var temp = new RepoTemp();
        Configure(temp.Path);
        try
        {
            var shell = Shell();
            using var controller = Controller(shell, temp.Path);
            var document = Document(controller);
            document.Apply(new TextChange(0, 0, "SECRET-source"));
            Invoke(controller, "ShowDocument", default(TelemetryMark));
            var snapshot = document.Snapshot;
            var range = new TextSpan(0, snapshot.Length);
            var result = new DocumentAnalysis(snapshot.Version, range, AnalysisCompleteness.Complete,
                new SemanticNode("document", range), [], [new SemanticToken("text", range)], 0);
            var publicationError = new InvalidOperationException("SECRET-publication");
            if (mode == "native")
                SetProperty(shell, "DuringAnalysisApply", (Action<NativeAnalysisView>)(_ => throw publicationError));
            if (mode == "projection")
                typeof(NativeEditorController).GetField("_projection", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(controller, null);
            var arguments = new object?[] { Field(controller, "_sessionDriver"), document,
                Field(controller, "_policy"), snapshot, result, range, new NativePreview("SECRET-preview", []) };
            if (mode == "success") Invoke(controller, "PublishIdleFullAnalysis", arguments);
            else
            {
                var thrown = Assert.Throws<TargetInvocationException>(() =>
                    Invoke(controller, "PublishIdleFullAnalysis", arguments));
                if (mode == "native") Assert.Same(publicationError, thrown.InnerException);
                else Assert.IsType<NullReferenceException>(thrown.InnerException);
            }
        }
        finally { await MoteTelemetry.ShutdownAsync(); }
        var records = Read(temp.Path);
        var publication = Assert.Single(records, r => Operation(r) == "analysis.to_presentation");
        Assert.Equal(mode == "success" ? "success" : "failure", publication.GetProperty("status").GetString());
        Assert.Equal(mode == "success" ? 1 : 0, records.Count(r => Operation(r) == "analysis.published"));
        Assert.DoesNotContain("SECRET", string.Join('\n', records.Select(r => r.GetRawText())));
    }

    /// <summary>Reuses the established fake event queue; no native window or OS input is created.</summary>
    private static INativeEditorShell Shell() => (INativeEditorShell)Activator.CreateInstance(
        typeof(NativeControllerTests).GetNestedType("FakeShell", BindingFlags.NonPublic)!,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        [NativeLineEndingMode.Preserve], null)!;

    /// <summary>Loads isolated repository-local defaults without environment overrides.</summary>
    private static NativeEditorController Controller(INativeEditorShell shell, string home)
    {
        var config = MoteConfigLoader.Load(new MoteConfigLoadOptions
        { UserHomeDirectory = home, UseEnvironmentOverride = false });
        return new NativeEditorController(shell, config, ThemePolicies.Get(config.ThemeId), null);
    }

    /// <summary>Gets the controller's actual canonical document, not a replacement fixture.</summary>
    private static Document Document(NativeEditorController controller) => (Document)Field(controller, "_document")!;

    /// <summary>Reads private orchestration state solely in deterministic tests.</summary>
    private static object? Field(NativeEditorController controller, string name) =>
        typeof(NativeEditorController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller);

    /// <summary>Invokes the shipped private path without adding a public test API.</summary>
    private static void Invoke(NativeEditorController controller, string name, params object?[] args) =>
        typeof(NativeEditorController).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, args);

    /// <summary>Configures the existing shell's injected UI behavior.</summary>
    private static void SetProperty(object shell, string name, object value) => shell.GetType().GetProperty(name)!.SetValue(shell, value);

    /// <summary>Pumps queued confirmation/completion; timeout is a deadlock guard, not a latency claim.</summary>
    private static async Task PumpUntil(object shell, Func<bool> completed)
    {
        var pump = shell.GetType().GetMethod("Pump")!;
        var timer = Stopwatch.StartNew();
        while (!completed() && timer.Elapsed < TimeSpan.FromSeconds(10))
        {
            pump.Invoke(shell, null);
            await Task.Delay(10);
        }
        Assert.True(completed(), "Save worker did not complete its queued UI callback.");
    }

    /// <summary>Enables local-only tracing in the repository scratch directory.</summary>
    private static void Configure(string path) => MoteTelemetry.Configure(new TelemetryOptions
    { Enabled = true, OutputDirectory = path });

    /// <summary>Parses independent records only after producer/writer shutdown.</summary>
    private static JsonElement[] Read(string path) => Directory.GetFiles(path, "*.jsonl")
        .SelectMany(File.ReadLines).Select(line =>
        { using var json = JsonDocument.Parse(line); return json.RootElement.Clone(); }).ToArray();

    /// <summary>Reads the fixed schema operation, never user data.</summary>
    private static string Operation(JsonElement record) => record.GetProperty("operation").GetString()!;
}
