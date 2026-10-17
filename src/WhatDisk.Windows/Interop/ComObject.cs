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
