using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WhatDisk.Windows.Interop;

/// <summary>
/// Bindings to COM initialisation in ole32, file-association queries in shlwapi and network drive queries in mpr.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class Ole32
{
    /// <summary>A single-threaded apartment, as the shell's user interface functions need.</summary>
    public const uint ApartmentThreaded = 0x2;

    /// <summary>The multi-threaded apartment.</summary>
    public const uint MultiThreaded = 0x0;

    /// <summary>The HRESULT returned when the thread is already in a different kind of apartment.</summary>
    public const int ChangedMode = unchecked((int)0x80010106);

    /// <summary>The <see cref="AssocQueryString"/> query for the executable registered to open a type.</summary>
    public const int AssocExecutable = 2;

    /// <summary>The <see cref="AssocQueryString"/> query for the display name of that application.</summary>
    public const int AssocFriendlyAppName = 4;

    /// <summary>
    /// Initialises COM on the calling thread.
    /// </summary>
    /// <param name="reserved">Zero.</param>
    /// <param name="mode">The apartment kind.</param>
    /// <returns>S_OK, S_FALSE when already initialised, or an error such as <see cref="ChangedMode"/>.</returns>
    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint reserved, uint mode);

    /// <summary>
    /// Balances a successful <see cref="CoInitializeEx"/>.
    /// </summary>
    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    /// <summary>
    /// Looks up a file association, such as the application that opens an extension.
    /// </summary>
    /// <param name="flags">Options; zero.</param>
    /// <param name="query">What to look up.</param>
    /// <param name="association">The extension, including its dot.</param>
    /// <param name="extra">The verb, or <see langword="null"/> for the default.</param>
    /// <param name="result">Receives the answer.</param>
    /// <param name="length">The buffer length in characters; receives the length written.</param>
    /// <returns>An HRESULT.</returns>
    [LibraryImport("shlwapi.dll", EntryPoint = "AssocQueryStringW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int AssocQueryString(uint flags, int query, string association, string? extra, char* result, ref uint length);

    /// <summary>
    /// Gets the network path a drive letter is mapped to.
    /// </summary>
    /// <param name="localName">The drive, for example <c>Z:</c>.</param>
    /// <param name="remoteName">Receives the network path.</param>
    /// <param name="length">The buffer length in characters.</param>
    /// <returns>Zero on success.</returns>
    [LibraryImport("mpr.dll", EntryPoint = "WNetGetConnectionW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int WNetGetConnection(string localName, char* remoteName, ref uint length);
}
