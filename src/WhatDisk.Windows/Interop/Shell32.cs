using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WhatDisk.Windows.Interop;

/// <summary>
/// Bindings to the Windows shell: the Recycle Bin, File Explorer windows, Properties, file icons and type names, and
/// the applications registered for a file type.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class Shell32
{
    /// <summary>The <see cref="SHFileOperation"/> function that deletes items.</summary>
    public const uint FileOperationDelete = 3;

    /// <summary>Shows no progress.</summary>
    public const ushort FileOperationSilent = 0x0004;

    /// <summary>Asks no "are you sure" question.</summary>
    public const ushort FileOperationNoConfirmation = 0x0010;

    /// <summary>Allows undo: deletes to the Recycle Bin.</summary>
    public const ushort FileOperationAllowUndo = 0x0040;

    /// <summary>Shows no error dialogs.</summary>
    public const ushort FileOperationNoErrorUI = 0x0400;

    /// <summary>Warns before deleting permanently an item that cannot go to the Recycle Bin.</summary>
    public const ushort FileOperationWantNukeWarning = 0x4000;

    /// <summary>The <see cref="SHObjectProperties"/> object type for a file system path.</summary>
    public const uint PropertiesFilePath = 0x2;

    /// <summary>Asks <see cref="SHGetFileInfo"/> for an icon handle.</summary>
    public const uint InfoIcon = 0x000000100;

    /// <summary>Asks <see cref="SHGetFileInfo"/> for the type name.</summary>
    public const uint InfoTypeName = 0x000000400;

    /// <summary>Asks <see cref="SHGetFileInfo"/> for the index in the system image list.</summary>
    public const uint InfoSystemIconIndex = 0x000004000;

    /// <summary>Asks <see cref="SHGetFileInfo"/> for the large (32 pixel) icon.</summary>
    public const uint InfoLargeIcon = 0x000000000;

    /// <summary>Asks <see cref="SHGetFileInfo"/> for the small (16 pixel) icon.</summary>
    public const uint InfoSmallIcon = 0x000000001;

    /// <summary>Tells <see cref="SHGetFileInfo"/> to describe the name and attributes given, without touching the disk.</summary>
    public const uint InfoUseFileAttributes = 0x000000010;

    /// <summary>Adds the shortcut arrow to the icon.</summary>
    public const uint InfoLinkOverlay = 0x000008000;

    /// <summary>The 48 pixel system image list.</summary>
    public const int ImageListExtraLarge = 0x2;

    /// <summary>The 256 pixel system image list.</summary>
    public const int ImageListJumbo = 0x4;

    /// <summary>Lists the applications recommended for a file type.</summary>
    public const int AssocFilterRecommended = 0x1;

    /// <summary>
    /// Copies, moves, renames or deletes items; used here to delete to the Recycle Bin.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <returns>Zero on success.</returns>
    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    public static partial int SHFileOperation(ref FileOperation operation);

    /// <summary>
    /// Opens the Properties window of an item.
    /// </summary>
    /// <param name="window">The owner window, or zero.</param>
    /// <param name="objectType">The kind of object (<see cref="PropertiesFilePath"/>).</param>
    /// <param name="objectName">The path.</param>
    /// <param name="propertyPage">The page to show, or <see langword="null"/> for the first.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SHObjectProperties(nint window, uint objectType, string objectName, string? propertyPage);

    /// <summary>
    /// Opens one Properties window for several items.
    /// </summary>
    /// <param name="dataObject">An <c>IDataObject</c> holding the items.</param>
    /// <param name="flags">Reserved; zero.</param>
    /// <returns>An HRESULT.</returns>
    [LibraryImport("shell32.dll")]
    public static partial int SHMultiFileProperties(nint dataObject, uint flags);

    /// <summary>
    /// Opens a File Explorer window on a folder with items selected.
    /// </summary>
    /// <param name="folder">The folder's item ID list.</param>
    /// <param name="count">The number of items to select (zero to open the parent of <paramref name="folder"/> and select it).</param>
    /// <param name="items">The items' ID lists, relative to the folder.</param>
    /// <param name="flags">Options; zero.</param>
    /// <returns>An HRESULT.</returns>
    [LibraryImport("shell32.dll")]
    public static partial int SHOpenFolderAndSelectItems(nint folder, uint count, nint* items, uint flags);

    /// <summary>
    /// Creates the item ID list of a path.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The ID list (free with <see cref="ILFree"/>), or zero.</returns>
    [LibraryImport("shell32.dll", EntryPoint = "ILCreateFromPathW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint ILCreateFromPath(string path);

    /// <summary>
    /// Frees an item ID list.
    /// </summary>
    /// <param name="idList">The ID list.</param>
    [LibraryImport("shell32.dll")]
    public static partial void ILFree(nint idList);

    /// <summary>
    /// Finds the last item of an item ID list (the item relative to its folder).
    /// </summary>
    /// <param name="idList">The ID list.</param>
    /// <returns>A pointer into <paramref name="idList"/>.</returns>
    [LibraryImport("shell32.dll")]
    public static partial nint ILFindLastID(nint idList);

    /// <summary>
    /// Creates an <c>IDataObject</c> for shell items.
    /// </summary>
    /// <param name="folder">The parent folder's ID list, or zero for the desktop (making the items absolute).</param>
    /// <param name="count">The number of items.</param>
    /// <param name="items">The items' ID lists.</param>
    /// <param name="inner">An inner data object, or zero.</param>
    /// <param name="interfaceId">The interface to return.</param>
    /// <param name="dataObject">Receives the data object.</param>
    /// <returns>An HRESULT.</returns>
    [LibraryImport("shell32.dll")]
    public static partial int SHCreateDataObject(nint folder, uint count, nint* items, nint inner, Guid* interfaceId, out nint dataObject);

    /// <summary>
    /// Gets an item's icon, type name or system image list index.
    /// </summary>
    /// <param name="path">The path (or, with <see cref="InfoUseFileAttributes"/>, any name of the type).</param>
    /// <param name="fileAttributes">The attributes to assume with <see cref="InfoUseFileAttributes"/>.</param>
    /// <param name="info">Receives the information.</param>
    /// <param name="infoSize">The size of <paramref name="info"/>.</param>
    /// <param name="flags">What to return.</param>
    /// <returns>Non-zero on success.</returns>
    [LibraryImport("shell32.dll", EntryPoint = "SHGetFileInfoW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint SHGetFileInfo(string path, uint fileAttributes, ref FileInfo info, uint infoSize, uint flags);

    /// <summary>
    /// Gets a system image list of a given size.
    /// </summary>
    /// <param name="imageList">The size (<see cref="ImageListExtraLarge"/> or <see cref="ImageListJumbo"/>).</param>
    /// <param name="interfaceId">The interface to return (<c>IImageList</c>).</param>
    /// <param name="list">Receives the image list.</param>
    /// <returns>An HRESULT.</returns>
    [LibraryImport("shell32.dll")]
    public static partial int SHGetImageList(int imageList, Guid* interfaceId, out nint list);

    /// <summary>
    /// Lists the applications registered for a file extension.
    /// </summary>
    /// <param name="extension">The extension, including its dot.</param>
    /// <param name="filter">Which applications to list (<see cref="AssocFilterRecommended"/>).</param>
    /// <param name="enumerator">Receives an <c>IEnumAssocHandlers</c>.</param>
    /// <returns>An HRESULT.</returns>
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHAssocEnumHandlers(string extension, int filter, out nint enumerator);

    /// <summary>
    /// The arguments of <see cref="SHFileOperation"/> (natural alignment, as on 64-bit Windows).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FileOperation
    {
        /// <summary>The owner window, or zero.</summary>
        public nint Window;

        /// <summary>The operation.</summary>
        public uint Function;

        /// <summary>The source paths, each ending with a null character, with an extra null at the end.</summary>
        public char* From;

        /// <summary>The destination paths; unused for deletion.</summary>
        public char* To;

        /// <summary>The options.</summary>
        public ushort Flags;

        /// <summary>Set when the user cancelled part of the operation.</summary>
        public int AnyOperationsAborted;

        /// <summary>Unused.</summary>
        public nint NameMappings;

        /// <summary>Unused.</summary>
        public char* ProgressTitle;
    }

    /// <summary>
    /// The result of <see cref="SHGetFileInfo"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct FileInfo
    {
        /// <summary>The icon handle, to be destroyed by the caller.</summary>
        public nint Icon;

        /// <summary>The index in the system image list.</summary>
        public int IconIndex;

        /// <summary>The item's attributes.</summary>
        public uint Attributes;

        /// <summary>The display name.</summary>
        public fixed char DisplayName[260];

        /// <summary>The type name, for example "Text Document".</summary>
        public fixed char TypeName[80];
    }
}
