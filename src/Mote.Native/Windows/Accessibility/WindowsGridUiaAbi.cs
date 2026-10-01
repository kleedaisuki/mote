using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Mote.Native.Windows.Accessibility;

/// <summary>Exact independent SDK pattern vtables; SAFEARRAY and BSTR ownership transfers to UIA.</summary>
[GeneratedComInterface, Guid("b17d6187-0907-464b-a168-0ef17a1572b1")]
internal partial interface IGridProviderAbi
{
    /// <summary>Returns a local rectangular slot, including pending and missing slots.</summary>
    [PreserveSig] int GetItem(int row, int column, out nint provider);
    /// <summary>Installed local row count.</summary>
    [PreserveSig] int GetRowCount(out int count);
    /// <summary>Installed local data column count, excluding the gutter.</summary>
    [PreserveSig] int GetColumnCount(out int count);
}
/// <summary>SDK table header relationships, independent of the Grid vtable.</summary>
[GeneratedComInterface, Guid("9c860395-97b3-490a-b52a-858cc22af166")]
internal partial interface ITableProviderAbi
{
    /// <summary>Absolute ordinal row headers.</summary>
    [PreserveSig] int GetRowHeaders(out nint headers);
    /// <summary>Absolute ordinal column headers.</summary>
    [PreserveSig] int GetColumnHeaders(out nint headers);
    /// <summary>Row-major traversal.</summary>
    [PreserveSig] int GetRowOrColumnMajor(out int major);
}
/// <summary>SDK bounded selection enumeration.</summary>
[GeneratedComInterface, Guid("fb8b03af-3bdf-48d4-bd36-1a65793be168")]
internal partial interface ISelectionProviderAbi
{
    /// <summary>Returns selected cells intersecting this installed window only.</summary>
    [PreserveSig] int GetSelection(out nint selection);
    /// <summary>Rectangular multiple selection is supported.</summary>
    [PreserveSig] int GetCanSelectMultiple(out int multiple);
    /// <summary>Selection can be cleared.</summary>
    [PreserveSig] int GetIsSelectionRequired(out int required);
}
/// <summary>SDK local cell coordinates and containing table.</summary>
[GeneratedComInterface, Guid("d02541f1-fb81-4d64-ae32-f520f8a6dbd1")]
internal partial interface IGridItemProviderAbi
{
    /// <summary>Local zero-based row.</summary>
    [PreserveSig] int GetRow(out int row);
    /// <summary>Local zero-based column.</summary>
    [PreserveSig] int GetColumn(out int column);
    /// <summary>Single row slot.</summary>
    [PreserveSig] int GetRowSpan(out int span);
    /// <summary>Single data column slot.</summary>
    [PreserveSig] int GetColumnSpan(out int span);
    /// <summary>Containing bounded table.</summary>
    [PreserveSig] int GetContainingGrid(out nint grid);
}
/// <summary>SDK cell ordinal header relationships.</summary>
[GeneratedComInterface, Guid("b9734fa6-771f-4d78-9c90-2517999349cd")]
internal partial interface ITableItemProviderAbi
{
    /// <summary>One matching row header.</summary>
    [PreserveSig] int GetRowHeaderItems(out nint headers);
    /// <summary>One matching column header.</summary>
    [PreserveSig] int GetColumnHeaderItems(out nint headers);
}
/// <summary>SDK exact selection operations; impossible unions and holes fail atomically.</summary>
[GeneratedComInterface, Guid("2acad808-b2d4-452d-a407-91ff1ad167b2")]
internal partial interface ISelectionItemProviderAbi
{
    /// <summary>Replaces selection with this cell.</summary>
    [PreserveSig] int Select();
    /// <summary>Adds this cell only when the exact union is rectangular.</summary>
    [PreserveSig] int AddToSelection();
    /// <summary>Removes this cell only when the exact difference is rectangular.</summary>
    [PreserveSig] int RemoveFromSelection();
    /// <summary>Current adapter-owned membership.</summary>
    [PreserveSig] int GetIsSelected(out int selected);
    /// <summary>Containing bounded table.</summary>
    [PreserveSig] int GetSelectionContainer(out nint container);
}
/// <summary>Read-only bounded presentation value; never a complete source decoding promise.</summary>
[GeneratedComInterface, Guid("c7935180-6fb3-4201-b174-7df73adbf64a")]
internal partial interface IGridValueProviderAbi
{
    /// <summary>Source editing is deliberately unavailable from this presentation pattern.</summary>
    [PreserveSig] int SetValue(nint value);
    /// <summary>Bounded displayed value; unavailable states do not fabricate a value.</summary>
    [PreserveSig] int GetValue(out nint value);
    /// <summary>Always read-only.</summary>
    [PreserveSig] int GetIsReadOnly(out int readOnly);
}
