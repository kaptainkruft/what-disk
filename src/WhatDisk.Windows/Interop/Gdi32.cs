using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WhatDisk.Windows.Interop;

/// <summary>
/// Bindings to the bitmap functions of gdi32, used to read icon pixels.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class Gdi32
{
    /// <summary>Uncompressed pixels.</summary>
    public const uint UncompressedRgb = 0;

    /// <summary>Colour table entries are RGB values.</summary>
    public const uint RgbColors = 0;

    /// <summary>
    /// Describes a GDI object; used to read a bitmap's size.
    /// </summary>
    /// <param name="handle">The object.</param>
    /// <param name="size">The size of the buffer.</param>
    /// <param name="buffer">Receives the description.</param>
    /// <returns>The number of bytes written, or zero.</returns>
    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
    public static partial int GetObject(nint handle, int size, void* buffer);

    /// <summary>
    /// Copies a bitmap's pixels in a given format.
    /// </summary>
    /// <param name="deviceContext">A device context.</param>
    /// <param name="bitmap">The bitmap.</param>
    /// <param name="start">The first scan line.</param>
    /// <param name="lines">The number of scan lines.</param>
    /// <param name="bits">Receives the pixels.</param>
    /// <param name="info">The format wanted.</param>
    /// <param name="usage">How to read the colour table.</param>
    /// <returns>The number of lines copied, or zero.</returns>
    [LibraryImport("gdi32.dll")]
    public static partial int GetDIBits(nint deviceContext, nint bitmap, uint start, uint lines, void* bits, BitmapInfoHeader* info, uint usage);

    /// <summary>
    /// Creates a memory device context.
    /// </summary>
    /// <param name="deviceContext">A device context to be compatible with, or zero for the screen.</param>
    /// <returns>The device context, or zero.</returns>
    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleDC(nint deviceContext);

    /// <summary>
    /// Deletes a memory device context.
    /// </summary>
    /// <param name="deviceContext">The device context.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint deviceContext);

    /// <summary>
    /// Deletes a GDI object such as a bitmap.
    /// </summary>
    /// <param name="handle">The object.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint handle);

    /// <summary>
    /// A bitmap's description, as returned by <see cref="GetObject"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Bitmap
    {
        /// <summary>Zero.</summary>
        public int Type;

        /// <summary>The width in pixels.</summary>
        public int Width;

        /// <summary>The height in pixels.</summary>
        public int Height;

        /// <summary>The bytes per scan line.</summary>
        public int WidthBytes;

        /// <summary>The number of colour planes.</summary>
        public ushort Planes;

        /// <summary>The bits per pixel.</summary>
        public ushort BitsPixel;

        /// <summary>The pixels, for a DIB section.</summary>
        public nint Bits;
    }

    /// <summary>
    /// The format of device-independent bitmap pixels.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        /// <summary>The size of this structure.</summary>
        public uint Size;

        /// <summary>The width in pixels.</summary>
        public int Width;

        /// <summary>The height in pixels; negative for top-down rows.</summary>
        public int Height;

        /// <summary>One.</summary>
        public ushort Planes;

        /// <summary>The bits per pixel.</summary>
        public ushort BitCount;

        /// <summary>The compression (<see cref="UncompressedRgb"/>).</summary>
        public uint Compression;

        /// <summary>The size of the pixels in bytes, or zero.</summary>
        public uint SizeImage;

        /// <summary>Horizontal resolution; unused.</summary>
        public int XPelsPerMeter;

        /// <summary>Vertical resolution; unused.</summary>
        public int YPelsPerMeter;

        /// <summary>Colours used; zero.</summary>
        public uint ColorsUsed;

        /// <summary>Important colours; zero.</summary>
        public uint ColorsImportant;
    }
}
