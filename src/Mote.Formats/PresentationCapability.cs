namespace Mote.Formats;

/// <summary>A policy's conventional layout, independent of analysis completeness.</summary>
public enum DocumentPresentationDefault
{
    /// <summary>Source alone is normally useful; an explicit preview remains supported.</summary>
    SourceOnly,
    /// <summary>The policy provides a useful distinct rendered presentation.</summary>
    SourceAndPreview
}

/// <summary>Compile-time presentation registration; does not parse text or depend on UI.</summary>
public static class DocumentPresentation
{
    /// <summary>
    /// Returns the policy convention before a user's explicit layout override.
    /// For example, plain text defaults to SourceOnly but can still be shown split.
    /// This does not assert that a particular analysis result is complete or ready.
    /// </summary>
    public static DocumentPresentationDefault ForPolicy(IDocumentPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return policy.Kind switch
        {
            DocumentKind.PlainText => DocumentPresentationDefault.SourceOnly,
            DocumentKind.Markdown or DocumentKind.Toml or DocumentKind.Json or
                DocumentKind.Yaml or DocumentKind.Csv => DocumentPresentationDefault.SourceAndPreview,
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy.Kind,
                "The document kind has no registered presentation convention.")
        };
    }
}
