using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WhatDisk.MacOS.Interop;

/// <summary>
/// The subset of the macOS C library used by WhatDisk.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class LibC
{
    /// <summary>The library that exports the system calls.</summary>
    private const string Library = "/usr/lib/libSystem.B.dylib";

    /// <summary>Open for reading only.</summary>
    internal const int ORdOnly = 0x0000;

    /// <summary>Fail if the path is not a directory.</summary>
    internal const int ODirectory = 0x00100000;

    /// <summary>Close the descriptor on exec.</summary>
    internal const int OCloExec = 0x01000000;

    /// <summary>
    /// Opens a file or directory.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="flags">The <c>O_*</c> flags.</param>
    /// <returns>A file descriptor, or -1 with errno set.</returns>
    [LibraryImport(Library, EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int Open(string path, int flags);

    /// <summary>
    /// Closes a file descriptor.
    /// </summary>
    /// <param name="fd">The descriptor.</param>
    /// <returns>Zero on success.</returns>
    [LibraryImport(Library, EntryPoint = "close", SetLastError = true)]
    internal static partial int Close(int fd);

    /// <summary>
    /// Reads the attributes of many directory entries at once.
    /// </summary>
    /// <param name="dirfd">A descriptor of the open directory.</param>
    /// <param name="attrList">The attributes to return.</param>
    /// <param name="attrBuf">The buffer that receives the attributes.</param>
    /// <param name="attrBufSize">The size of the buffer.</param>
    /// <param name="options">The <c>FSOPT_*</c> options.</param>
    /// <returns>The number of entries returned, 0 at the end, or -1 with errno set.</returns>
    [LibraryImport(Library, EntryPoint = "getattrlistbulk", SetLastError = true)]
    internal static partial int GetAttrListBulk(int dirfd, AttrList* attrList, byte* attrBuf, nuint attrBufSize, ulong options);

    /// <summary>
    /// Reads the attributes of the object at a path.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="attrList">The attributes to return.</param>
    /// <param name="attrBuf">The buffer that receives the attributes.</param>
    /// <param name="attrBufSize">The size of the buffer.</param>
    /// <param name="options">The <c>FSOPT_*</c> options.</param>
    /// <returns>Zero on success, or -1 with errno set.</returns>
    [LibraryImport(Library, EntryPoint = "getattrlist", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int GetAttrList(string path, AttrList* attrList, byte* attrBuf, nuint attrBufSize, nuint options);

    /// <summary>
    /// Reads the attributes of an open file or directory.
    /// </summary>
    /// <param name="fd">The descriptor.</param>
    /// <param name="attrList">The attributes to return.</param>
    /// <param name="attrBuf">The buffer that receives the attributes.</param>
    /// <param name="attrBufSize">The size of the buffer.</param>
    /// <param name="options">The <c>FSOPT_*</c> options.</param>
    /// <returns>Zero on success, or -1 with errno set.</returns>
    [LibraryImport(Library, EntryPoint = "fgetattrlist", SetLastError = true)]
    internal static partial int FGetAttrList(int fd, AttrList* attrList, byte* attrBuf, nuint attrBufSize, nuint options);
}

/// <summary>
/// The <c>struct attrlist</c> that selects attributes for <c>getattrlist</c> and <c>getattrlistbulk</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AttrList
{
    /// <summary>The number of attribute groups; always <see cref="AttributeConstants.BitmapCount"/>.</summary>
    public ushort BitmapCount;

    /// <summary>Reserved; must be zero.</summary>
    public ushort Reserved;

    /// <summary>The common attributes (<c>ATTR_CMN_*</c>).</summary>
    public uint CommonAttr;

    /// <summary>The volume attributes (<c>ATTR_VOL_*</c>).</summary>
    public uint VolAttr;

    /// <summary>The directory attributes (<c>ATTR_DIR_*</c>).</summary>
    public uint DirAttr;

    /// <summary>The file attributes (<c>ATTR_FILE_*</c>).</summary>
    public uint FileAttr;

    /// <summary>The fork attributes (<c>ATTR_FORK_*</c>).</summary>
    public uint ForkAttr;
}
