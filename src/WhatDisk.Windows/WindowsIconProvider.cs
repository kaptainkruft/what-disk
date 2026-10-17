using System.Collections.Frozen;
using System.Runtime.Versioning;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Windows.Interop;

namespace WhatDisk.Windows;

/// <summary>
/// File Explorer icons from the Windows shell. Most files share their type's icon, looked up by extension without
/// touching the disk; drives, special folders and files that carry their own icon (programs, shortcuts) are looked up
/// by path.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsIconProvider : DefaultIconProvider
{
    /// <summary>Extensions of files that carry their own icon.</summary>
    private static readonly FrozenSet<string> OwnIconExtensions = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, ".exe", ".lnk", ".url", ".ico", ".cpl", ".msc", ".scr", ".appref-ms");

    /// <summary>Folders that File Explorer shows with their own icon.</summary>
    private static readonly FrozenSet<string> SpecialFolders = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "Desktop", "Documents", "Downloads", "Music", "Pictures", "Videos", "Favorites", "Links", "Contacts",
        "Saved Games", "Searches", "OneDrive", "3D Objects", "Users", "Windows", "Program Files", "Program Files (x86)");

    /// <summary>The attributes assumed for files looked up by type.</summary>
    private const uint NormalAttribute = 0x80;

    /// <summary>Draws icons with their transparency.</summary>
    private const uint TransparentDrawing = 0x1;

    /// <inheritdoc/>
    public override IconKey GetKey(FileSystemNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node switch
        {
            DirectoryNode and not RootNode when SpecialFolders.Contains(node.Name) => new IconKey(IconKey.PathCategory, node.FullPath),
            FileNode { Kind: NodeKind.File } when OwnIconExtensions.Contains(Path.GetExtension(node.Name)) => new IconKey(IconKey.PathCategory, node.FullPath),
            _ => base.GetKey(node),
        };
    }

    /// <inheritdoc/>
    public override IconImage? LoadIcon(IconKey key, int pixelSize)
    {
        using var apartment = ComApartment.Enter();
        var (path, attributes, flags) = key.Category switch
        {
            IconKey.PathCategory or IconKey.VolumeCategory or IconKey.NetworkVolumeCategory or IconKey.RemovableVolumeCategory
                when !string.IsNullOrEmpty(key.Value) => (WindowsPaths.WithTrailingSeparatorIfRoot(key.Value), 0u, 0u),
            IconKey.FolderCategory => ("folder", DirectoryEntryParser.DirectoryAttribute, Shell32.InfoUseFileAttributes),
            IconKey.FileCategory => ("file" + key.Value, NormalAttribute, Shell32.InfoUseFileAttributes),
            IconKey.LinkCategory => ("file", NormalAttribute, Shell32.InfoUseFileAttributes | Shell32.InfoLinkOverlay),
            _ => (null, 0u, 0u),
        };
        if (path is null)
        {
            return null;
        }

        var icon = pixelSize > 32 ? LoadFromImageList(path, attributes, flags, pixelSize) : LoadIconHandle(path, attributes, flags, pixelSize);
        if (icon == 0)
        {
            return null;
        }

        try
        {
            return ToImage(icon);
        }
        finally
        {
            User32.DestroyIcon(icon);
        }
    }

    /// <summary>
    /// Gets a 16 or 32 pixel icon.
    /// </summary>
    /// <param name="path">The path or type name.</param>
    /// <param name="attributes">The attributes assumed for a type name.</param>
    /// <param name="flags">Extra <c>SHGetFileInfo</c> flags.</param>
    /// <param name="pixelSize">The size wanted.</param>
    /// <returns>The icon handle, or zero.</returns>
    private static nint LoadIconHandle(string path, uint attributes, uint flags, int pixelSize)
    {
        var info = default(Shell32.FileInfo);
        var size = pixelSize <= 16 ? Shell32.InfoSmallIcon : Shell32.InfoLargeIcon;
        return Shell32.SHGetFileInfo(path, attributes, ref info, (uint)sizeof(Shell32.FileInfo), Shell32.InfoIcon | size | flags) != 0 ? info.Icon : 0;
    }

    /// <summary>
    /// Gets a 48 or 256 pixel icon from the system image lists, falling back to the 32 pixel icon.
    /// </summary>
    /// <param name="path">The path or type name.</param>
    /// <param name="attributes">The attributes assumed for a type name.</param>
    /// <param name="flags">Extra <c>SHGetFileInfo</c> flags.</param>
    /// <param name="pixelSize">The size wanted.</param>
    /// <returns>The icon handle, or zero.</returns>
    private static nint LoadFromImageList(string path, uint attributes, uint flags, int pixelSize)
    {
        var info = default(Shell32.FileInfo);
        if (Shell32.SHGetFileInfo(path, attributes, ref info, (uint)sizeof(Shell32.FileInfo), Shell32.InfoSystemIconIndex | flags) == 0)
        {
            return LoadIconHandle(path, attributes, flags, pixelSize);
        }

        var interfaceId = ComObject.ImageListId;
        var listKind = pixelSize > 48 ? Shell32.ImageListJumbo : Shell32.ImageListExtraLarge;
        if (Shell32.SHGetImageList(listKind, &interfaceId, out var list) < 0 || list == 0)
        {
            return LoadIconHandle(path, attributes, flags, pixelSize);
        }

        try
        {
            nint icon = 0;
            var getIcon = (delegate* unmanaged<nint, int, uint, nint*, int>)ComObject.Method(list, 10);
            return getIcon(list, info.IconIndex, TransparentDrawing, &icon) >= 0 ? icon : LoadIconHandle(path, attributes, flags, pixelSize);
        }
        finally
        {
            ComObject.Release(list);
        }
    }

    /// <summary>
    /// Reads an icon's pixels as premultiplied BGRA.
    /// </summary>
    /// <param name="icon">The icon.</param>
    /// <returns>The image, or <see langword="null"/> for icons without colour.</returns>
    private static IconImage? ToImage(nint icon)
    {
        if (!User32.GetIconInfo(icon, out var info))
        {
            return null;
        }

        var deviceContext = Gdi32.CreateCompatibleDC(0);
        try
        {
            if (info.ColorBitmap == 0)
            {
                return null;
            }

            Gdi32.Bitmap bitmap;
            if (Gdi32.GetObject(info.ColorBitmap, sizeof(Gdi32.Bitmap), &bitmap) == 0 || bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                return null;
            }

            var colour = ReadPixels(deviceContext, info.ColorBitmap, bitmap.Width, bitmap.Height);
            if (colour is null)
            {
                return null;
            }

            var mask = info.MaskBitmap == 0 ? null : ReadPixels(deviceContext, info.MaskBitmap, bitmap.Width, bitmap.Height);
            IconPixels.Finish(colour, mask);
            return new IconImage(colour, IconImageFormat.Bgra32Premultiplied, bitmap.Width, bitmap.Height);
        }
        finally
        {
            Gdi32.DeleteDC(deviceContext);
            Gdi32.DeleteObject(info.ColorBitmap);
            Gdi32.DeleteObject(info.MaskBitmap);
        }
    }

    /// <summary>
    /// Copies a bitmap's pixels as 32-bit BGRA, top row first.
    /// </summary>
    /// <param name="deviceContext">A memory device context.</param>
    /// <param name="bitmap">The bitmap.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The pixels, or <see langword="null"/>.</returns>
    private static byte[]? ReadPixels(nint deviceContext, nint bitmap, int width, int height)
    {
        // Room for a colour table after the header, which GetDIBits may write for some formats.
        var header = stackalloc byte[sizeof(Gdi32.BitmapInfoHeader) + (256 * 4)];
        var info = (Gdi32.BitmapInfoHeader*)header;
        *info = new Gdi32.BitmapInfoHeader
        {
            Size = (uint)sizeof(Gdi32.BitmapInfoHeader),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
            Compression = Gdi32.UncompressedRgb,
        };
        var pixels = new byte[width * height * 4];
        fixed (byte* pointer = pixels)
        {
            return Gdi32.GetDIBits(deviceContext, bitmap, 0, (uint)height, pointer, info, Gdi32.RgbColors) == height ? pixels : null;
        }
    }
}
