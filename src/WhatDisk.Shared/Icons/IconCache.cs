using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Volumes;

namespace WhatDisk.Icons;

/// <summary>
/// Loads platform icons in the background, converts them to images and caches them by key and size.
/// </summary>
/// <param name="provider">Supplies platform icons.</param>
public sealed class IconCache(IIconProvider provider) : IDisposable
{
    /// <summary>
    /// The application resource key under which the cache is published to views.
    /// </summary>
    public const string ResourceKey = "WhatDisk.IconCache";

    /// <summary>The icons loaded or being loaded, by key and pixel size.</summary>
    private readonly ConcurrentDictionary<(IconKey Key, int PixelSize), Task<IImage?>> _icons = new();

    /// <summary>Limits how many platform icon requests run at once.</summary>
    private readonly SemaphoreSlim _throttle = new(2, 2);

    /// <summary>Supplies platform icons.</summary>
    private readonly IIconProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <summary>
    /// Gets the icon key for a node or a volume.
    /// </summary>
    /// <param name="subject">A <see cref="FileSystemNode"/> or <see cref="VolumeInfo"/>.</param>
    /// <returns>The key.</returns>
    public IconKey GetKey(object subject) => subject switch
    {
        FileSystemNode node => _provider.GetKey(node),
        VolumeInfo volume => _provider.GetKey(volume),
        _ => new IconKey(IconKey.FileCategory),
    };

    /// <summary>
    /// Gets an icon, loading it in the background the first time it is requested.
    /// </summary>
    /// <param name="key">The icon key.</param>
    /// <param name="pixelSize">The desired size in physical pixels.</param>
    /// <returns>A task producing the image, or <see langword="null"/> when the platform has none.</returns>
    public Task<IImage?> GetIconAsync(IconKey key, int pixelSize) =>
        _icons.GetOrAdd((key, pixelSize), static (entry, cache) => cache.LoadAsync(entry.Key, entry.PixelSize), this);

    /// <summary>
    /// Releases the throttle used to limit concurrent icon loads.
    /// </summary>
    public void Dispose() => _throttle.Dispose();

    /// <summary>
    /// Converts platform image data into an image.
    /// </summary>
    /// <param name="image">The image data.</param>
    /// <returns>The image, or <see langword="null"/>.</returns>
    internal static Bitmap? Decode(IconImage? image)
    {
        if (image is null || image.Data.Length == 0)
        {
            return null;
        }

        if (image.Format == IconImageFormat.Encoded)
        {
            using var stream = new MemoryStream(image.Data, writable: false);
            return new Bitmap(stream);
        }

        if (image.Width <= 0 || image.Height <= 0 || image.Data.Length < image.Width * image.Height * 4)
        {
            return null;
        }

        var handle = GCHandle.Alloc(image.Data, GCHandleType.Pinned);
        try
        {
            return new Bitmap(
                PixelFormat.Bgra8888,
                AlphaFormat.Premul,
                handle.AddrOfPinnedObject(),
                new PixelSize(image.Width, image.Height),
                new Vector(96, 96),
                image.Width * 4);
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Loads and decodes an icon on a background thread.
    /// </summary>
    /// <param name="key">The icon key.</param>
    /// <param name="pixelSize">The desired size in physical pixels.</param>
    /// <returns>The image, or <see langword="null"/>.</returns>
    private async Task<IImage?> LoadAsync(IconKey key, int pixelSize)
    {
        await _throttle.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Decode(_provider.LoadIcon(key, pixelSize))).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // An icon that fails to load falls back to the built-in icon; it must not break the UI.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Trace.TraceWarning("Could not load the icon {0}: {1}", key, exception.Message);
            return null;
        }
        finally
        {
            _throttle.Release();
        }
    }
}
