namespace WhatDisk.Platform;

/// <summary>
/// Identifies an icon in the icon cache. Items that share an icon (for example all files with the same extension)
/// share a key.
/// </summary>
/// <param name="Category">The broad category, one of the constants on <see cref="IconKey"/>.</param>
/// <param name="Value">A category-specific qualifier such as an extension or a path.</param>
public readonly record struct IconKey(string Category, string? Value = null)
{
    /// <summary>The category for generic folders.</summary>
    public const string FolderCategory = "folder";

    /// <summary>The category for files, qualified by lower-case extension.</summary>
    public const string FileCategory = "file";

    /// <summary>The category for items with an icon of their own, qualified by path.</summary>
    public const string PathCategory = "path";

    /// <summary>The category for volumes, qualified by mount point.</summary>
    public const string VolumeCategory = "volume";

    /// <summary>The category for network volumes, qualified by mount point.</summary>
    public const string NetworkVolumeCategory = "network-volume";

    /// <summary>The category for removable volumes, qualified by mount point.</summary>
    public const string RemovableVolumeCategory = "removable-volume";

    /// <summary>The category for symbolic links.</summary>
    public const string LinkCategory = "link";

    /// <summary>The category for mount points that were not traversed in place.</summary>
    public const string MountPointCategory = "mount-point";

    /// <summary>The category for special files such as devices and sockets.</summary>
    public const string SpecialCategory = "special";
}
