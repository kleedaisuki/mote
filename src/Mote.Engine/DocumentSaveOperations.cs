using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Mote.Tests")]

namespace Mote.Engine;

/// <summary>Per-document filesystem boundary for deterministic failure-policy tests.</summary>
internal class DocumentSaveOperations
{
    /// <summary>Commits an existing target without metadata-ignore or retry.</summary>
    internal virtual void Replace(string stage, string target) =>
        File.Replace(stage, target, null, ignoreMetadataErrors: false);

    /// <summary>Commits a new target without overwrite.</summary>
    internal virtual void Move(string stage, string target) => File.Move(stage, target);

    /// <summary>Deletes only an explicitly owned staging path.</summary>
    internal virtual void Delete(string stage) => File.Delete(stage);

    /// <summary>Separates post-commit bookkeeping from the commit result.</summary>
    internal virtual FileStamp ReadSavedStamp(string target) => FileStamp.Read(target);

    /// <summary>Observes failure outcomes separately so tests can inject uncommon provider/security errors.</summary>
    internal virtual Task<(SaveFileOutcome Outcome, int? Error)> InspectAsync(
        string path, byte[]? original, byte[]? saved) => SaveRecovery.InspectAsync(path, original, saved);
}
