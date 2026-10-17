using System.Buffers.Binary;
using System.Runtime.InteropServices;
using WhatDisk.Scanning;

namespace WhatDisk.Windows;

/// <summary>
/// The directory listing formats of <c>GetFileInformationByHandleEx</c>, best first.
/// </summary>
internal enum DirectoryListingClass
{
    /// <summary><c>FILE_ID_EXTD_DIR_INFO</c>: 128-bit file IDs and reparse tags (NTFS and ReFS, Windows 8 and later).</summary>
    IdExtended = 19,

    /// <summary><c>FILE_ID_BOTH_DIR_INFO</c>: 64-bit file IDs; reparse tags in the extended-attribute field.</summary>
    IdBoth = 10,

    /// <summary><c>FILE_FULL_DIR_INFO</c>: no file IDs; supported by every file system.</summary>
    Full = 14,
}

/// <summary>
/// One directory entry as reported by Windows, before classification.
/// </summary>
/// <param name="Name">The name.</param>
/// <param name="Attributes">The <c>FILE_ATTRIBUTE_*</c> flags.</param>
/// <param name="ReparseTag">The reparse tag, or zero.</param>
/// <param name="EndOfFile">The size in bytes.</param>
/// <param name="AllocationSize">The space allocated on disk.</param>
/// <param name="LastWriteTime">The last write time as a <c>FILETIME</c>.</param>
/// <param name="FileId">The file ID (low 64 bits folded with the high 64 bits), or zero when unknown.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct RawDirectoryEntry(string Name, uint Attributes, uint ReparseTag, long EndOfFile, long AllocationSize, long LastWriteTime, ulong FileId);

/// <summary>
/// Decodes the buffers filled by <c>GetFileInformationByHandleEx</c> directory listings and classifies their entries.
/// Pure code, tested on any OS.
/// </summary>
internal static class DirectoryEntryParser
{
    /// <summary>The attribute of read-only items.</summary>
    public const uint ReadOnlyAttribute = 0x1;

    /// <summary>The attribute of hidden items.</summary>
    public const uint HiddenAttribute = 0x2;

    /// <summary>The attribute of system items.</summary>
    public const uint SystemAttribute = 0x4;

    /// <summary>The attribute of directories.</summary>
    public const uint DirectoryAttribute = 0x10;

    /// <summary>The attribute of sparse files.</summary>
    public const uint SparseAttribute = 0x200;

    /// <summary>The attribute of reparse points.</summary>
    public const uint ReparsePointAttribute = 0x400;

    /// <summary>The attribute of compressed items.</summary>
    public const uint CompressedAttribute = 0x800;

    /// <summary>The attribute of items whose data is offline.</summary>
    public const uint OfflineAttribute = 0x1000;

    /// <summary>The attribute of cloud files that are fetched when opened.</summary>
    public const uint RecallOnOpenAttribute = 0x40000;

    /// <summary>The attribute of cloud files whose data is fetched when read (online-only files).</summary>
    public const uint RecallOnDataAccessAttribute = 0x400000;

    /// <summary>The reparse tag of junctions and mounted folders.</summary>
    public const uint MountPointTag = 0xA0000003;

    /// <summary>The reparse tag of symbolic links.</summary>
    public const uint SymbolicLinkTag = 0xA000000C;

    /// <summary>The reparse tag of Unix domain sockets.</summary>
    public const uint UnixSocketTag = 0x80000023;

    /// <summary>The bit set in the tags of reparse points that stand for another item (links).</summary>
    public const uint NameSurrogateBit = 0x20000000;

    /// <summary>
    /// Reads the entries of one listing buffer, skipping <c>.</c> and <c>..</c>.
    /// </summary>
    /// <param name="buffer">The bytes written by the call.</param>
    /// <param name="listingClass">The format of the buffer.</param>
    /// <param name="entries">Receives the entries.</param>
    public static void Parse(ReadOnlySpan<byte> buffer, DirectoryListingClass listingClass, ICollection<RawDirectoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var offset = 0;
        while (offset + 68 <= buffer.Length)
        {
            var record = buffer[offset..];
            var next = BinaryPrimitives.ReadInt32LittleEndian(record);
            var entry = ParseRecord(record, listingClass);
            if (entry is { } value)
            {
                entries.Add(value);
            }

            if (next <= 0)
            {
                break;
            }

            offset += next;
        }
    }

    /// <summary>
    /// Converts an entry to the scanner's form.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="device">The volume serial number of the directory being read.</param>
    /// <param name="isVolumeMountPoint">
    /// Decides whether a junction-type reparse point with this name is a mounted volume (a boundary to another volume)
    /// rather than a junction (a link, never followed).
    /// </param>
    /// <returns>The entry.</returns>
    public static FileSystemEntryInfo ToEntry(in RawDirectoryEntry entry, ulong device, Func<string, bool> isVolumeMountPoint)
    {
        ArgumentNullException.ThrowIfNull(isVolumeMountPoint);
        var (type, extra) = Classify(entry.Attributes, entry.ReparseTag, entry.Name, isVolumeMountPoint);
        var attributes = MapAttributes(entry.Attributes) | extra;
        var isFile = type is EntryType.File;
        return new FileSystemEntryInfo(
            entry.Name,
            type,
            isFile ? entry.EndOfFile : 0,
            isFile ? entry.AllocationSize : FileSystemEntryInfo.UnknownSize,
            ToDateTime(entry.LastWriteTime),
            attributes,
            entry.FileId == 0 ? FileIdentity.Unknown : new FileIdentity(device, entry.FileId));
    }

    /// <summary>
    /// Decides what kind of entry an item is. Reparse points that stand for another item (symbolic links and
    /// junctions) are links and never followed, except junction-type points that mount another volume, which are
    /// directories marked as mount points. Other reparse points, such as OneDrive placeholders and deduplicated files,
    /// are ordinary files and directories.
    /// </summary>
    /// <param name="attributes">The <c>FILE_ATTRIBUTE_*</c> flags.</param>
    /// <param name="reparseTag">The reparse tag, or zero.</param>
    /// <param name="name">The item's name.</param>
    /// <param name="isVolumeMountPoint">Decides whether a junction-type reparse point mounts a volume.</param>
    /// <returns>The entry type and any attributes it implies.</returns>
    public static (EntryType Type, EntryAttributes Extra) Classify(uint attributes, uint reparseTag, string name, Func<string, bool> isVolumeMountPoint)
    {
        ArgumentNullException.ThrowIfNull(isVolumeMountPoint);
        var isDirectory = (attributes & DirectoryAttribute) != 0;
        if ((attributes & ReparsePointAttribute) != 0)
        {
            if ((reparseTag & NameSurrogateBit) != 0)
            {
                return reparseTag == MountPointTag && isDirectory && isVolumeMountPoint(name)
                    ? (EntryType.Directory, EntryAttributes.MountPoint)
                    : (EntryType.SymbolicLink, EntryAttributes.None);
            }

            if (reparseTag == UnixSocketTag)
            {
                return (EntryType.Other, EntryAttributes.None);
            }
        }

        return (isDirectory ? EntryType.Directory : EntryType.File, EntryAttributes.None);
    }

    /// <summary>
    /// Maps <c>FILE_ATTRIBUTE_*</c> flags to the scanner's attributes. Cloud files that are not on this device count
    /// as offline.
    /// </summary>
    /// <param name="attributes">The flags.</param>
    /// <returns>The attributes.</returns>
    public static EntryAttributes MapAttributes(uint attributes)
    {
        var result = EntryAttributes.None;
        if ((attributes & HiddenAttribute) != 0)
        {
            result |= EntryAttributes.Hidden;
        }

        if ((attributes & SystemAttribute) != 0)
        {
            result |= EntryAttributes.System;
        }

        if ((attributes & ReadOnlyAttribute) != 0)
        {
            result |= EntryAttributes.ReadOnly;
        }

        if ((attributes & ReparsePointAttribute) != 0)
        {
            result |= EntryAttributes.ReparsePoint;
        }

        if ((attributes & CompressedAttribute) != 0)
        {
            result |= EntryAttributes.Compressed;
        }

        if ((attributes & SparseAttribute) != 0)
        {
            result |= EntryAttributes.Sparse;
        }

        if ((attributes & (OfflineAttribute | RecallOnDataAccessAttribute)) != 0)
        {
            result |= EntryAttributes.Offline;
        }

        return result;
    }

    /// <summary>
    /// Converts a <c>FILETIME</c> to UTC, or <see cref="DateTime.MinValue"/> when it is not set.
    /// </summary>
    /// <param name="fileTime">The time in 100-nanosecond intervals since 1601.</param>
    /// <returns>The time.</returns>
    public static DateTime ToDateTime(long fileTime) =>
        fileTime is > 0 and <= 2650467743999999999 ? DateTime.FromFileTimeUtc(fileTime) : DateTime.MinValue;

    /// <summary>
    /// Reads one record.
    /// </summary>
    /// <param name="record">The bytes from the record's start.</param>
    /// <param name="listingClass">The format.</param>
    /// <returns>The entry, or <see langword="null"/> for <c>.</c> and <c>..</c> or a malformed record.</returns>
    private static RawDirectoryEntry? ParseRecord(ReadOnlySpan<byte> record, DirectoryListingClass listingClass)
    {
        var nameOffset = listingClass switch
        {
            DirectoryListingClass.IdExtended => 88,
            DirectoryListingClass.IdBoth => 104,
            _ => 68,
        };
        if (record.Length < nameOffset)
        {
            return null;
        }

        var lastWrite = BinaryPrimitives.ReadInt64LittleEndian(record[24..]);
        var endOfFile = BinaryPrimitives.ReadInt64LittleEndian(record[40..]);
        var allocation = BinaryPrimitives.ReadInt64LittleEndian(record[48..]);
        var attributes = BinaryPrimitives.ReadUInt32LittleEndian(record[56..]);
        var nameLength = BinaryPrimitives.ReadInt32LittleEndian(record[60..]);
        var eaSize = BinaryPrimitives.ReadUInt32LittleEndian(record[64..]);
        var (reparseTag, fileId) = listingClass switch
        {
            DirectoryListingClass.IdExtended => (BinaryPrimitives.ReadUInt32LittleEndian(record[68..]),
                BinaryPrimitives.ReadUInt64LittleEndian(record[72..]) ^ BinaryPrimitives.ReadUInt64LittleEndian(record[80..])),
            DirectoryListingClass.IdBoth => (eaSize, BinaryPrimitives.ReadUInt64LittleEndian(record[96..])),
            _ => (eaSize, 0UL),
        };
        if (nameLength <= 0 || (nameLength & 1) != 0 || nameOffset + nameLength > record.Length)
        {
            return null;
        }

        var name = MemoryMarshal.Cast<byte, char>(record.Slice(nameOffset, nameLength));
        if (name is "." or "..")
        {
            return null;
        }

        if ((attributes & ReparsePointAttribute) == 0)
        {
            reparseTag = 0;
        }

        return new RawDirectoryEntry(name.ToString(), attributes, reparseTag, endOfFile, allocation, lastWrite, fileId);
    }
}
