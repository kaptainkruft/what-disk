using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using WhatDisk.Icons;
using WhatDisk.Platform;
using WhatDisk.Tests.Fakes;
using WhatDisk.Views.Controls;

namespace WhatDisk.Tests.Icons;

/// <summary>
/// Tests loading, decoding and caching of platform icons, and their display by <see cref="PlatformIcon"/>.
/// </summary>
public sealed class IconCacheTests
{
    /// <summary>
    /// Missing or empty image data decodes to nothing.
    /// </summary>
    [AvaloniaFact]
    public void Decode_ReturnsNullForMissingData()
    {
        Assert.Null(IconCache.Decode(null));
        Assert.Null(IconCache.Decode(new IconImage([], IconImageFormat.Encoded)));
        Assert.Null(IconCache.Decode(new IconImage(new byte[4], IconImageFormat.Bgra32Premultiplied, 2, 2)));
    }

    /// <summary>
    /// Raw pixels and encoded PNG images both decode to bitmaps of the right size.
    /// </summary>
    [AvaloniaFact]
    public void Decode_HandlesRawPixelsAndPng()
    {
        var raw = IconCache.Decode(SolidIcon(3, 0, 0, 255));
        Assert.Equal(new PixelSize(3, 3), raw!.PixelSize);

        var png = IconCache.Decode(new IconImage(EncodePng(raw), IconImageFormat.Encoded));
        Assert.Equal(new PixelSize(3, 3), png!.PixelSize);
    }

    /// <summary>
    /// Icons are loaded once per key and size.
    /// </summary>
    /// <returns>A task that completes when the icons have loaded.</returns>
    [AvaloniaFact]
    public async Task GetIconAsync_CachesByKeyAndSize()
    {
        var provider = new CountingProvider(SolidIcon(4, 0, 255, 0));
        using var cache = new IconCache(provider);
        var key = new IconKey(IconKey.FileCategory, ".txt");

        var first = await cache.GetIconAsync(key, 16);
        var second = await cache.GetIconAsync(key, 16);
        await cache.GetIconAsync(key, 32);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(2, provider.Loads);
    }

    /// <summary>
    /// A provider failure yields no icon rather than an exception.
    /// </summary>
    /// <returns>A task that completes when the load has failed.</returns>
    [AvaloniaFact]
    public async Task GetIconAsync_ReturnsNullWhenTheProviderFails()
    {
        using var cache = new IconCache(new CountingProvider(null, fail: true));

        Assert.Null(await cache.GetIconAsync(new IconKey(IconKey.FolderCategory), 16));
    }

    /// <summary>
    /// The icon control first shows the built-in icon, then the platform icon once loaded.
    /// </summary>
    /// <returns>A task that completes when the icon has been rendered.</returns>
    [AvaloniaFact]
    public async Task PlatformIcon_ShowsTheLoadedPlatformIcon()
    {
        using var cache = new IconCache(new CountingProvider(SolidIcon(32, 255, 0, 0)));
        Application.Current!.Resources[IconCache.ResourceKey] = cache;
        try
        {
            var root = TestTree.Root();
            var icon = new PlatformIcon { Subject = TestTree.File(root, "red.txt", 1), IconSize = 32 };
            var window = new Window { Width = 64, Height = 64, Content = icon };
            window.Show();
            await cache.GetIconAsync(cache.GetKey(icon.Subject!), 32);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            var pixel = ReadCenterPixel(window.CaptureRenderedFrame()!);
            Assert.True(pixel.Red > 200 && pixel.Green < 60 && pixel.Blue < 60, $"Expected red, found {pixel}.");
            window.Close();
        }
        finally
        {
            Application.Current!.Resources.Remove(IconCache.ResourceKey);
        }
    }

    /// <summary>
    /// Creates a square icon of one colour.
    /// </summary>
    /// <param name="size">The width and height.</param>
    /// <param name="red">The red component.</param>
    /// <param name="green">The green component.</param>
    /// <param name="blue">The blue component.</param>
    /// <returns>The icon.</returns>
    private static IconImage SolidIcon(int size, byte red, byte green, byte blue)
    {
        var pixels = new byte[size * size * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = blue;
            pixels[index + 1] = green;
            pixels[index + 2] = red;
            pixels[index + 3] = 255;
        }

        return new IconImage(pixels, IconImageFormat.Bgra32Premultiplied, size, size);
    }

    /// <summary>
    /// Encodes a bitmap as PNG.
    /// </summary>
    /// <param name="bitmap">The bitmap.</param>
    /// <returns>The PNG bytes.</returns>
    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    /// <summary>
    /// Reads the colour of the pixel at the centre of a frame.
    /// </summary>
    /// <param name="frame">The rendered frame.</param>
    /// <returns>The colour.</returns>
    private static (byte Red, byte Green, byte Blue) ReadCenterPixel(WriteableBitmap frame)
    {
        using var buffer = frame.Lock();
        var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        var offset = (buffer.Size.Height / 2 * buffer.RowBytes) + (buffer.Size.Width / 2 * 4);
        return buffer.Format == PixelFormat.Rgba8888
            ? (pixels[offset], pixels[offset + 1], pixels[offset + 2])
            : (pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    /// <summary>
    /// An icon provider that counts loads and can fail.
    /// </summary>
    /// <param name="image">The image to return.</param>
    /// <param name="fail">Whether loading throws.</param>
    private sealed class CountingProvider(IconImage? image, bool fail = false) : DefaultIconProvider
    {
        /// <summary>The number of loads so far.</summary>
        private int _loads;

        /// <summary>
        /// Gets the number of loads so far.
        /// </summary>
        public int Loads => _loads;

        /// <inheritdoc/>
        public override IconImage? LoadIcon(IconKey key, int pixelSize)
        {
            Interlocked.Increment(ref _loads);
            return fail ? throw new InvalidOperationException("Icon service unavailable") : image;
        }
    }
}
