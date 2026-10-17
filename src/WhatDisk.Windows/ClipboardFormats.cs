using System.Buffers.Binary;
using System.Text;

namespace WhatDisk.Windows;

/// <summary>
/// Builds the clipboard data File Explorer reads when pasting. Pure code, tested on any OS.
/// </summary>
internal static class ClipboardFormats
{
    /// <summary>The size of the <c>DROPFILES</c> header.</summary>
    public const int DropFilesHeaderSize = 20;

    /// <summary>The <c>Preferred DropEffect</c> value that makes File Explorer copy (rather than move) on paste.</summary>
    public const int DropEffectCopy = 1;

    /// <summary>
    /// Builds a <c>CF_HDROP</c> block: a <c>DROPFILES</c> header followed by UTF-16 paths, each ending with a null
    /// character, and an extra null character at the end.
    /// </summary>
    /// <param name="paths">The full paths.</param>
    /// <returns>The bytes.</returns>
    public static byte[] BuildFileDrop(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var characters = paths.Sum(static path => path.Length + 1) + 1;
        var bytes = new byte[DropFilesHeaderSize + (characters * 2)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, DropFilesHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 1);
        var offset = DropFilesHeaderSize;
        foreach (var path in paths)
        {
            offset += Encoding.Unicode.GetBytes(path, bytes.AsSpan(offset));
            offset += 2;
        }

        return bytes;
    }

    /// <summary>
    /// Builds a <c>CF_UNICODETEXT</c> block: the UTF-16 text ending with a null character.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The bytes.</returns>
    public static byte[] BuildText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = new byte[(text.Length + 1) * 2];
        Encoding.Unicode.GetBytes(text, bytes);
        return bytes;
    }

    /// <summary>
    /// Reads the paths of a <c>CF_HDROP</c> block.
    /// </summary>
    /// <param name="block">The bytes.</param>
    /// <returns>The paths.</returns>
    public static List<string> ReadFileDrop(ReadOnlySpan<byte> block)
    {
        var start = BinaryPrimitives.ReadInt32LittleEndian(block);
        var characters = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, char>(block[start..]);
        return WindowsPaths.SplitMultiString(characters);
    }
}
