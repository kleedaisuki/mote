namespace Mote.Engine;

/// <summary>Replaces a UTF-16 range in a document with new text.</summary>
/// <param name="Start">Zero-based UTF-16 offset of the first replaced code unit.</param>
/// <param name="DeleteLength">Number of UTF-16 code units removed.</param>
/// <param name="InsertText">Text inserted at <paramref name="Start"/>; never null.</param>
/// <remarks>
/// Offsets deliberately match .NET strings and native text controls. This value type does not
/// validate boundaries; <see cref="Document.Apply"/> rejects edits that split a UTF-16 surrogate pair.
/// Other consumers must enforce their own boundary rules.
/// </remarks>
public readonly record struct TextChange(int Start, int DeleteLength, string InsertText);

/// <summary>Describes a committed document mutation.</summary>
/// <param name="Before">Snapshot before the mutation.</param>
/// <param name="After">Snapshot after the mutation.</param>
/// <param name="Change">Replacement expressed in coordinates of <paramref name="Before"/>.</param>
public sealed record DocumentChangedEventArgs(TextSnapshot Before, TextSnapshot After, TextChange Change);

/// <summary>Describes a UTF-16 replacement by extent without copying inserted text.</summary>
/// <param name="Start">Start offset in the snapshot before the edit.</param>
/// <param name="DeleteLength">Code units removed from the before snapshot.</param>
/// <param name="InsertLength">Code units inserted into the after snapshot.</param>
/// <remarks>Consumers that need the inserted characters may read <c>After.GetText(Start, InsertLength)</c>.</remarks>
public readonly record struct TextChangeRange(int Start, int DeleteLength, int InsertLength);

/// <summary>Describes an ordered document mutation using immutable snapshots and lengths only.</summary>
/// <param name="Before">Snapshot before the mutation.</param>
/// <param name="After">Snapshot after the mutation.</param>
/// <param name="Change">Replacement extent in coordinates of <paramref name="Before"/>.</param>
/// <remarks>
/// <see cref="Document.ChangedRange"/> is delivered before the legacy
/// <see cref="Document.Changed"/> callback for the same mutation. The event itself never
/// requires one contiguous inserted-text string.
/// </remarks>
public sealed record DocumentChangedRangeEventArgs(
    TextSnapshot Before, TextSnapshot After, TextChangeRange Change);
