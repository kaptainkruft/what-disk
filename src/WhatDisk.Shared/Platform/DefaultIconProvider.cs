using WhatDisk.Scanning;
using WhatDisk.Volumes;

namespace WhatDisk.Platform;

/// <summary>
/// An icon provider that maps items to icon categories and leaves every image to the built-in vector icons.
/// Platform providers derive from it and load real images.
/// </summary>
public class DefaultIconProvider : IIconProvider
{
    /// <inheritdoc/>
    public virtual IconKey GetKey(FileSystemNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node switch
        {
            RootNode { IsVolumeRoot: true } root => GetKey(root.Volume),
            DirectoryNode => new IconKey(IconKey.FolderCategory),
            PlaceholderNode => new IconKey(IconKey.MountPointCategory),
            _ when node.Kind == NodeKind.SymbolicLink => new IconKey(IconKey.LinkCategory),
            _ when node.Kind == NodeKind.Special => new IconKey(IconKey.SpecialCategory),
            _ => new IconKey(IconKey.FileCategory, GetExtension(node.Name)),
        };
    }

    /// <inheritdoc/>
    public virtual IconKey GetKey(VolumeInfo volume)
    {
        ArgumentNullException.ThrowIfNull(volume);
        var category = volume.Kind switch
        {
            VolumeKind.Network => IconKey.NetworkVolumeCategory,
            VolumeKind.Removable or VolumeKind.Optical => IconKey.RemovableVolumeCategory,
            _ => IconKey.VolumeCategory,
        };
        return new IconKey(category, volume.MountPoint);
    }

    /// <inheritdoc/>
    public virtual IconImage? LoadIcon(IconKey key, int pixelSize) => null;

    /// <summary>
    /// Gets the lower-case extension of a file name, including the dot, or an empty string.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <returns>The extension.</returns>
    protected static string GetExtension(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var extension = Path.GetExtension(name);
        return extension.Length is > 1 and <= 16 ? extension.ToLowerInvariant() : string.Empty;
    }
}
