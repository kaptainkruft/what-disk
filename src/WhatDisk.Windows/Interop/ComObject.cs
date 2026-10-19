using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WhatDisk.Windows.Interop;

/// <summary>
/// Calls COM interface methods through their virtual tables, which keeps the few shell interfaces used here free of
/// runtime-generated wrappers (and so compatible with Native AOT).
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe class ComObject
{
    /// <summary>The interface ID of <c>IDataObject</c>.</summary>
    public static readonly Guid DataObjectId = new("0000010e-0000-0000-C000-000000000046");

    /// <summary>The interface ID of <c>IImageList</c>.</summary>
    public static readonly Guid ImageListId = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    /// <summary>The interface ID of <c>IShellFolder</c>.</summary>
    public static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");

    /// <summary>The interface ID of <c>IContextMenu</c>.</summary>
    public static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");

    /// <summary>The interface ID of <c>IContextMenu2</c>, which handles the messages of owner-drawn menu items.</summary>
    public static readonly Guid ContextMenu2Id = new("000214F4-0000-0000-C000-000000000046");

    /// <summary>The interface ID of <c>IContextMenu3</c>, which also handles menu keyboard shortcuts.</summary>
    public static readonly Guid ContextMenu3Id = new("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719");

    /// <summary>
    /// Releases a COM object.
    /// </summary>
    /// <param name="instance">The object, or zero.</param>
    public static void Release(nint instance)
    {
        if (instance != 0)
        {
            _ = ((delegate* unmanaged<nint, uint>)Method(instance, 2))(instance);
        }
    }

    /// <summary>
    /// Asks a COM object for another of its interfaces.
    /// </summary>
    /// <param name="instance">The object.</param>
    /// <param name="interfaceId">The interface.</param>
    /// <returns>The interface (release it with <see cref="Release"/>), or zero when the object does not have it.</returns>
    public static nint QueryInterface(nint instance, Guid interfaceId)
    {
        nint result = 0;
        var query = (delegate* unmanaged<nint, Guid*, nint*, int>)Method(instance, 0);
        return query(instance, &interfaceId, &result) >= 0 ? result : 0;
    }

    /// <summary>
    /// Gets a method of a COM object's virtual table.
    /// </summary>
    /// <param name="instance">The object.</param>
    /// <param name="slot">The method's slot (0 to 2 are <c>IUnknown</c>).</param>
    /// <returns>The function pointer.</returns>
    public static nint Method(nint instance, int slot) => (*(nint**)instance)[slot];

    /// <summary>
    /// Reads a string returned by a COM method and frees it.
    /// </summary>
    /// <param name="value">The string allocated with <c>CoTaskMemAlloc</c>.</param>
    /// <returns>The string, or <see langword="null"/>.</returns>
    public static string? TakeString(char* value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return new string(value);
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)value);
        }
    }
}
