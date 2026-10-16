using System.Collections.Frozen;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WhatDisk.MacOS.Interop;
using WhatDisk.Platform;
using WhatDisk.Scanning;

namespace WhatDisk.MacOS;

/// <summary>
/// Loads Finder icons through <c>NSWorkspace</c>: per path for volumes, bundles (applications and other packages)
/// and special folders; per uniform type for ordinary files and folders.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacIconProvider : DefaultIconProvider
{
    /// <summary><c>NSBitmapImageFileTypePNG</c>.</summary>
    private const nuint PngFileType = 4;

    /// <summary>Directory extensions that macOS presents as packages with their own icons.</summary>
    private static readonly FrozenSet<string> BundleExtensions = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        ".app", ".appex", ".bundle", ".framework", ".plugin", ".kext", ".prefpane", ".qlgenerator", ".saver", ".xpc",
        ".mdimporter", ".photoslibrary", ".musiclibrary", ".tvlibrary", ".imovielibrary", ".fcpbundle", ".logicx",
        ".band", ".pkg", ".mpkg", ".xcodeproj", ".xcworkspace", ".playground", ".rtfd", ".pages", ".numbers", ".key",
        ".scptd", ".docarchive", ".photolibrary", ".aplibrary", ".theater");

    /// <summary>Folder names that have special Finder icons in the home directory or at the root.</summary>
    private static readonly FrozenSet<string> SpecialFolders = FrozenSet.Create(
        StringComparer.Ordinal,
        "Applications", "Desktop", "Developer", "Documents", "Downloads", "Library", "Movies", "Music", "Pictures",
        "Public", "Sites", "System", "Users");

    /// <summary>Whether AppKit and UniformTypeIdentifiers could be loaded.</summary>
    private static readonly bool IsAvailable = LoadFrameworks();

    /// <inheritdoc/>
    public override IconKey GetKey(FileSystemNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is DirectoryNode and not RootNode
            && (BundleExtensions.Contains(Path.GetExtension(node.Name)) || SpecialFolders.Contains(node.Name)))
        {
            return new IconKey(IconKey.PathCategory, node.FullPath);
        }

        return base.GetKey(node);
    }

    /// <inheritdoc/>
    public override IconImage? LoadIcon(IconKey key, int pixelSize)
    {
        if (!IsAvailable)
        {
            return null;
        }

        var pool = ObjectiveC.AutoreleasePoolPush();
        try
        {
            var image = key.Category switch
            {
                IconKey.PathCategory or IconKey.VolumeCategory or IconKey.NetworkVolumeCategory or IconKey.RemovableVolumeCategory
                    when !string.IsNullOrEmpty(key.Value) => IconForFile(key.Value),
                IconKey.FolderCategory => IconForType("public.folder"),
                IconKey.FileCategory when key.Value is { Length: > 1 } extension => IconForExtension(extension[1..]),
                IconKey.FileCategory => IconForType("public.data"),
                IconKey.LinkCategory => IconForType("public.symlink"),
                IconKey.MountPointCategory => IconForType("public.volume"),
                _ => 0,
            };
            return image == 0 ? null : ToPng(image, pixelSize);
        }
        finally
        {
            ObjectiveC.AutoreleasePoolPop(pool);
        }
    }

    /// <summary>
    /// Loads AppKit (for <c>NSWorkspace</c>) and UniformTypeIdentifiers (for <c>UTType</c>).
    /// </summary>
    /// <returns><see langword="true"/> when both frameworks are available.</returns>
    private static bool LoadFrameworks()
    {
        try
        {
            NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
            NativeLibrary.Load("/System/Library/Frameworks/UniformTypeIdentifiers.framework/UniformTypeIdentifiers");
            return ObjectiveC.GetClass("NSWorkspace") != 0 && ObjectiveC.GetClass("UTType") != 0;
        }
        catch (DllNotFoundException exception)
        {
            Trace.TraceWarning("macOS icons are unavailable: {0}", exception.Message);
            return false;
        }
    }

    /// <summary>
    /// Gets the shared <c>NSWorkspace</c>.
    /// </summary>
    /// <returns>The workspace object.</returns>
    private static nint Workspace() =>
        ObjectiveC.Send(ObjectiveC.GetClass("NSWorkspace"), ObjectiveC.Selector("sharedWorkspace"));

    /// <summary>
    /// Gets the Finder icon of the item at a path.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>An autoreleased <c>NSImage</c>.</returns>
    private static nint IconForFile(string path) =>
        ObjectiveC.Send(Workspace(), ObjectiveC.Selector("iconForFile:"), ObjectiveC.CreateString(path));

    /// <summary>
    /// Gets the icon of a uniform type identifier.
    /// </summary>
    /// <param name="identifier">The type identifier, such as <c>public.folder</c>.</param>
    /// <returns>An autoreleased <c>NSImage</c>, or zero.</returns>
    private static nint IconForType(string identifier)
    {
        var type = ObjectiveC.Send(ObjectiveC.GetClass("UTType"), ObjectiveC.Selector("typeWithIdentifier:"), ObjectiveC.CreateString(identifier));
        return type == 0 ? 0 : ObjectiveC.Send(Workspace(), ObjectiveC.Selector("iconForContentType:"), type);
    }

    /// <summary>
    /// Gets the icon for files with an extension.
    /// </summary>
    /// <param name="extension">The extension without the dot.</param>
    /// <returns>An autoreleased <c>NSImage</c>, or zero.</returns>
    private static nint IconForExtension(string extension)
    {
        var type = ObjectiveC.Send(ObjectiveC.GetClass("UTType"), ObjectiveC.Selector("typeWithFilenameExtension:"), ObjectiveC.CreateString(extension));
        return type == 0 ? IconForType("public.data") : ObjectiveC.Send(Workspace(), ObjectiveC.Selector("iconForContentType:"), type);
    }

    /// <summary>
    /// Renders an <c>NSImage</c> at a size and encodes it as PNG.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <param name="pixelSize">The width and height in pixels.</param>
    /// <returns>The PNG image, or <see langword="null"/> when conversion fails.</returns>
    private static IconImage? ToPng(nint image, int pixelSize)
    {
        var rect = new CGRect(0, 0, pixelSize, pixelSize);
        var cgImage = ObjectiveC.Send(image, ObjectiveC.Selector("CGImageForProposedRect:context:hints:"), &rect, 0, 0);
        if (cgImage == 0)
        {
            return null;
        }

        var allocated = ObjectiveC.Send(ObjectiveC.GetClass("NSBitmapImageRep"), ObjectiveC.Selector("alloc"));
        var representation = ObjectiveC.Send(allocated, ObjectiveC.Selector("initWithCGImage:"), cgImage);
        if (representation == 0)
        {
            return null;
        }

        try
        {
            var properties = ObjectiveC.Send(ObjectiveC.GetClass("NSDictionary"), ObjectiveC.Selector("dictionary"));
            var data = ObjectiveC.Send(representation, ObjectiveC.Selector("representationUsingType:properties:"), PngFileType, properties);
            if (data == 0)
            {
                return null;
            }

            var length = (int)ObjectiveC.SendForUnsigned(data, ObjectiveC.Selector("length"));
            var bytes = ObjectiveC.Send(data, ObjectiveC.Selector("bytes"));
            if (length <= 0 || bytes == 0)
            {
                return null;
            }

            var png = new byte[length];
            Marshal.Copy(bytes, png, 0, length);
            return new IconImage(png, IconImageFormat.Encoded);
        }
        finally
        {
            _ = ObjectiveC.Send(representation, ObjectiveC.Selector("release"));
        }
    }
}
