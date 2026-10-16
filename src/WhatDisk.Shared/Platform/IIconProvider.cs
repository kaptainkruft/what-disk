using WhatDisk.Scanning;
using WhatDisk.Volumes;

namespace WhatDisk.Platform;

/// <summary>
/// Supplies platform icons for items in the scan tree and for volumes.
/// </summary>
public interface IIconProvider
{
    /// <summary>
    /// Gets the cache key of the icon for a node. Called on the UI thread, so it must be fast.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The key.</returns>
    IconKey GetKey(FileSystemNode node);

    /// <summary>
    /// Gets the cache key of the icon for a volume. Called on the UI thread, so it must be fast.
    /// </summary>
    /// <param name="volume">The volume.</param>
    /// <returns>The key.</returns>
    IconKey GetKey(VolumeInfo volume);

    /// <summary>
    /// Loads the image for an icon key. Called on a background thread; may be slow.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="pixelSize">The desired width and height in physical pixels.</param>
    /// <returns>The image, or <see langword="null"/> to use the built-in icon for the key's category.</returns>
    IconImage? LoadIcon(IconKey key, int pixelSize);
}
