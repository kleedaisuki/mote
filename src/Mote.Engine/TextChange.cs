namespace Mote.Engine;

/// <summary>Replaces a UTF-16 range in a document with new text.</summary>
/// <param name="Start">Zero-based UTF-16 offset of the first replaced code unit.</param>
/// <param name="DeleteLength">Number of UTF-16 code units removed.</param>
/// <param name="InsertText">Text inserted at <paramref name="Start"/>; never null.</param>
/// <remarks>Offsets deliberately match .NET strings and native text controls. A change may split a surrogate pair; callers that need Unicode scalar boundaries must enforce them.</remarks>
public readonly record struct TextChange(int Start, int DeleteLength, string InsertText);

/// <summary>Describes a committed document mutation.</summary>
/// <param name="Before">Snapshot before the mutation.</param>
/// <param name="After">Snapshot after the mutation.</param>
/// <param name="Change">Replacement expressed in coordinates of <paramref name="Before"/>.</param>
public sealed record DocumentChangedEventArgs(TextSnapshot Before, TextSnapshot After, TextChange Change);
