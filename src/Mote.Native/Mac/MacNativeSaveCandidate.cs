using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Mote.Native.Mac;

/// <summary>Shared native-only Save-family filter; never interprets a candidate as a command.</summary>
[SupportedOSPlatform("macos")]
internal static class MacNativeSaveCandidate
{
    /// <summary>Reads one UTF-16 unit only after key-down metadata and length match.</summary>
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern ushort CharacterAt(nint value, nint selector, nuint index);

    /// <summary>Inspects the borrowed event without retaining it or marshaling its text.</summary>
    internal static bool Matches(nint originalEvent)
    {
        if (originalEvent == 0) return false;
        var type = (nuint)ObjC.Send(originalEvent, ObjC.Sel("type"));
        var modifiers = (nuint)ObjC.Send(originalEvent, ObjC.Sel("modifierFlags"));
        if (!NativeSaveFamilyCandidate.HasCandidateMetadata(type, modifiers)) return false;
        var text = ObjC.Send(originalEvent, ObjC.Sel("charactersIgnoringModifiers"));
        var length = (nuint)ObjC.Send(text, ObjC.Sel("length"));
        return length == 1 && NativeSaveFamilyCandidate.Matches(type, modifiers, length,
            CharacterAt(text, ObjC.Sel("characterAtIndex:"), 0));
    }
}
