namespace WhatDisk.Windows;

/// <summary>
/// Turns the pixels of a Windows icon into premultiplied BGRA. Pure code, tested on any OS.
/// </summary>
internal static class IconPixels
{
    /// <summary>
    /// Finishes an icon's colour pixels in place: icons without an alpha channel take their transparency from the mask
    /// (black is opaque, white transparent), then every pixel is premultiplied by its alpha.
    /// </summary>
    /// <param name="colour">The colour pixels as 32-bit BGRA, top row first.</param>
    /// <param name="mask">The mask pixels, converted to 32-bit BGRA, or empty when there is no mask.</param>
    public static void Finish(Span<byte> colour, ReadOnlySpan<byte> mask)
    {
        if (!HasAlpha(colour))
        {
            for (var index = 0; index + 3 < colour.Length; index += 4)
            {
                var transparent = index + 3 < mask.Length && mask[index] != 0;
                colour[index + 3] = transparent ? (byte)0 : (byte)255;
            }
        }

        Premultiply(colour);
    }

    /// <summary>
    /// Determines whether any pixel has a non-zero alpha value.
    /// </summary>
    /// <param name="pixels">The BGRA pixels.</param>
    /// <returns><see langword="true"/> when the alpha channel is used.</returns>
    public static bool HasAlpha(ReadOnlySpan<byte> pixels)
    {
        for (var index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Multiplies each pixel's colour by its alpha, as premultiplied formats expect.
    /// </summary>
    /// <param name="pixels">The BGRA pixels.</param>
    public static void Premultiply(Span<byte> pixels)
    {
        for (var index = 0; index + 3 < pixels.Length; index += 4)
        {
            var alpha = pixels[index + 3];
            if (alpha == 255)
            {
                continue;
            }

            pixels[index] = (byte)((pixels[index] * alpha + 127) / 255);
            pixels[index + 1] = (byte)((pixels[index + 1] * alpha + 127) / 255);
            pixels[index + 2] = (byte)((pixels[index + 2] * alpha + 127) / 255);
        }
    }
}
