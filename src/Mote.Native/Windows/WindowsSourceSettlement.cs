namespace Mote.Native.Windows;

/// <summary>Pure acknowledgement certificate used by the Windows command-settlement barrier.</summary>
internal static class WindowsSourceSettlement
{
    /// <summary>Candidate emission alone is insufficient: exact same-document canonical advancement is required.</summary>
    internal static bool Acknowledged(NativeSourceInstallation original, NativeSourceInstallation? accepted,
        string finalDisplay, string installedDisplay, bool mapInstalled, bool inputReadOnly) =>
        accepted is not null && mapInstalled && !inputReadOnly &&
        accepted.Nonce == original.Nonce && accepted.Stamp.Generation == original.Stamp.Generation &&
        accepted.Stamp.Version > original.Stamp.Version &&
        string.Equals(accepted.Projection.Display, finalDisplay, StringComparison.Ordinal) &&
        string.Equals(installedDisplay, finalDisplay, StringComparison.Ordinal);
}
