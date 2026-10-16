namespace WhatDisk.Platform;

/// <summary>
/// The pixel format of an <see cref="IconImage"/>.
/// </summary>
public enum IconImageFormat
{
    /// <summary>An encoded image file (PNG, for example) that the UI framework decodes.</summary>
    Encoded,

    /// <summary>Raw 32-bit pixels in BGRA order with premultiplied alpha, top row first.</summary>
    Bgra32Premultiplied,
}

/// <summary>
/// Image data for an icon, produced by a platform <see cref="IIconProvider"/> without depending on the UI framework.
/// </summary>
/// <param name="Data">The encoded file or raw pixels.</param>
/// <param name="Format">How <paramref name="Data"/> is laid out.</param>
/// <param name="Width">The width in pixels (ignored for encoded images).</param>
/// <param name="Height">The height in pixels (ignored for encoded images).</param>
public sealed record IconImage(byte[] Data, IconImageFormat Format, int Width = 0, int Height = 0);
