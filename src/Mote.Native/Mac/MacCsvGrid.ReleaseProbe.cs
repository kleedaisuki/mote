namespace Mote.Native.Mac;

/// <summary>Content-free observation of the installed renderer, independent of experimental AX registration.</summary>
internal sealed unsafe partial class MacCsvGrid
{
    /// <summary>Reads actual installed row slots/columns without registering AX nodes or mutating native state.</summary>
    internal MacReleaseGridObservation ProbeReleaseRenderedGrid => MacReleaseGridModel.Observe(_identity,
        _projection, _navigation, _slots, _displayColumns, _installedColumns,
        _ready.GetLength(0), _ready.GetLength(1), _windowPending, _installing, _installSerial);
}
