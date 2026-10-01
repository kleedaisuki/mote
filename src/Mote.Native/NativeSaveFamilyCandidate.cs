namespace Mote.Native;

/// <summary>Content-free classification for optional menu observation, not key binding.</summary>
internal static class NativeSaveFamilyCandidate
{
    /// <summary>Accepts key-down Command-S family; Shift/Caps Lock do not determine request kind.</summary>
    internal static bool Matches(nuint type, nuint modifiers, nuint length, ushort character) =>
        HasCandidateMetadata(type, modifiers) && length == 1 && character is 's' or 'S';

    /// <summary>Rejects non-key-down and Control/Option chords before accessing native characters.</summary>
    internal static bool HasCandidateMetadata(nuint type, nuint modifiers) =>
        type == 10 && (modifiers & (1u << 20)) != 0 &&
        (modifiers & ((1u << 18) | (1u << 19))) == 0;
}
