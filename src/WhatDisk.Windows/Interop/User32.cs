using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WhatDisk.Windows.Interop;

/// <summary>
/// Bindings to the clipboard, window and icon functions of user32.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class User32
{
    /// <summary>The parent that makes a window message-only (invisible, used to own the clipboard).</summary>
    public const nint MessageOnlyParent = -3;

    /// <summary>The clipboard format for UTF-16 text.</summary>
    public const uint UnicodeTextFormat = 13;

    /// <summary>The clipboard format for a list of files (a <c>DROPFILES</c> structure).</summary>
    public const uint FileDropFormat = 15;

    /// <summary>
    /// Opens the clipboard for changes.
    /// </summary>
    /// <param name="owner">The window that will own the clipboard.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenClipboard(nint owner);

    /// <summary>
    /// Closes the clipboard.
    /// </summary>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseClipboard();

    /// <summary>
    /// Empties the clipboard and makes the opening window its owner.
    /// </summary>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyClipboard();

    /// <summary>
    /// Puts data on the clipboard; the clipboard takes ownership of the memory.
    /// </summary>
    /// <param name="format">The format.</param>
    /// <param name="memory">The global memory handle.</param>
    /// <returns>The handle, or zero on failure.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint SetClipboardData(uint format, nint memory);

    /// <summary>
    /// Gets data from the clipboard.
    /// </summary>
    /// <param name="format">The format.</param>
    /// <returns>The global memory handle (owned by the clipboard), or zero.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint GetClipboardData(uint format);

    /// <summary>
    /// Registers a named clipboard format.
    /// </summary>
    /// <param name="format">The name.</param>
    /// <returns>The format number, or zero.</returns>
    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterClipboardFormat(string format);

    /// <summary>
    /// Creates a window.
    /// </summary>
    /// <param name="extendedStyle">The extended style.</param>
    /// <param name="className">The window class.</param>
    /// <param name="windowName">The title.</param>
    /// <param name="style">The style.</param>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="parent">The parent window, or <see cref="MessageOnlyParent"/>.</param>
    /// <param name="menu">The menu, or zero.</param>
    /// <param name="instance">The module, or zero.</param>
    /// <param name="parameter">Creation data, or zero.</param>
    /// <returns>The window, or zero.</returns>
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowEx(uint extendedStyle, string className, string? windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    /// <summary>
    /// Destroys a window.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint window);

    /// <summary>
    /// Destroys an icon.
    /// </summary>
    /// <param name="icon">The icon.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    /// <summary>
    /// Gets an icon's colour and mask bitmaps (which the caller must delete).
    /// </summary>
    /// <param name="icon">The icon.</param>
    /// <param name="info">Receives the bitmaps.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetIconInfo(nint icon, out IconInfo info);

    /// <summary>
    /// An icon's bitmaps, as returned by <see cref="GetIconInfo"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct IconInfo
    {
        /// <summary>Non-zero for an icon, zero for a cursor.</summary>
        public int IsIcon;

        /// <summary>The hot spot's x coordinate.</summary>
        public uint HotspotX;

        /// <summary>The hot spot's y coordinate.</summary>
        public uint HotspotY;

        /// <summary>The mask bitmap.</summary>
        public nint MaskBitmap;

        /// <summary>The colour bitmap, or zero for a monochrome icon.</summary>
        public nint ColorBitmap;
    }
}
