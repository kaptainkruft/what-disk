using System.Buffers.Binary;
using System.Text;
using WhatDisk.MacOS.Interop;
using WhatDisk.Scanning;

namespace WhatDisk.MacOS;

/// <summary>
/// Decodes the packed attribute buffers returned by <c>getattrlistbulk</c> and <c>getattrlist</c>. Pure code, so it can
/// be tested with synthetic buffers on any platform.
/// </summary>
/// <remarks>
/// Each entry starts with its length (a <c>uint32</c>) and an <c>attribute_set_t</c> listing the attributes actually
/// returned. The error code (when present) follows; the remaining attributes appear in the documented order, each
/// aligned to four bytes. Names are referenced through an <c>attrreference_t</c> whose offset is relative to itself.
/// </remarks>
internal static class AttributeBufferParser
{
    /// <summary>The size of <c>attribute_set_t</c>: five 32-bit masks.</summary>
    private const int AttributeSetSize = 20;

    /// <summary>The size of <c>attrreference_t</c>.</summary>
    private const int ReferenceSize = 8;

    /// <summary>The size of a 64-bit <c>struct timespec</c>.</summary>
    private const int TimespecSize = 16;

    /// <summary>
    /// The attributes WhatDisk requests for each directory entry.
    /// </summary>
    public static AttrList BulkRequest => new()
    {
        BitmapCount = AttributeConstants.BitmapCount,
        CommonAttr = AttributeConstants.CommonReturnedAttributes | AttributeConstants.CommonName | AttributeConstants.CommonDeviceId
            | AttributeConstants.CommonObjectType | AttributeConstants.CommonModificationTime | AttributeConstants.CommonFlags
            | AttributeConstants.CommonFileId | AttributeConstants.CommonError,
        DirAttr = AttributeConstants.DirectoryMountStatus,
        FileAttr = AttributeConstants.FileLinkCount | AttributeConstants.FileTotalSize | AttributeConstants.FileAllocatedSize,
    };

    /// <summary>
    /// The attributes WhatDisk requests to identify a single object.
    /// </summary>
    public static AttrList IdentityRequest => new()
    {
        BitmapCount = AttributeConstants.BitmapCount,
        CommonAttr = AttributeConstants.CommonDeviceId | AttributeConstants.CommonFileId,
    };

    /// <summary>
    /// Decodes the entries of one <c>getattrlistbulk</c> result.
    /// </summary>
    /// <param name="buffer">The attribute buffer.</param>
    /// <param name="count">The number of entries the call reported.</param>
    /// <param name="entries">Receives the decoded entries.</param>
    /// <exception cref="InvalidDataException">The buffer is malformed.</exception>
    public static void ParseEntries(ReadOnlySpan<byte> buffer, int count, ICollection<FileSystemEntryInfo> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var offset = 0;
        for (var index = 0; index < count; index++)
        {
            if (offset + 4 + AttributeSetSize > buffer.Length)
            {
                throw new InvalidDataException("The attribute buffer ended before all entries were read.");
            }

            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer[offset..]);
            if (length < 4 + AttributeSetSize || offset + length > buffer.Length)
            {
                throw new InvalidDataException($"An attribute entry has an invalid length of {length} bytes.");
            }

            if (ParseEntry(buffer.Slice(offset, length)) is { } entry)
            {
                entries.Add(entry);
            }

            offset += length;
        }
    }

    /// <summary>
    /// Decodes the result of an identity request (<see cref="IdentityRequest"/>).
    /// </summary>
    /// <param name="buffer">The attribute buffer.</param>
    /// <returns>The identity, or <see cref="FileIdentity.Unknown"/> when the buffer is too short.</returns>
    public static FileIdentity ParseIdentity(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 16 || BinaryPrimitives.ReadUInt32LittleEndian(buffer) < 16)
        {
            return FileIdentity.Unknown;
        }

        var device = (uint)BinaryPrimitives.ReadInt32LittleEndian(buffer[4..]);
        var fileId = BinaryPrimitives.ReadUInt64LittleEndian(buffer[8..]);
        return new FileIdentity(device, fileId);
    }

    /// <summary>
    /// Decodes a single entry.
    /// </summary>
    /// <param name="entry">The entry's bytes, starting with its length.</param>
    /// <returns>The entry, or <see langword="null"/> when it has no usable name.</returns>
    private static FileSystemEntryInfo? ParseEntry(ReadOnlySpan<byte> entry)
    {
        var common = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
        var directory = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
        var file = BinaryPrimitives.ReadUInt32LittleEndian(entry[16..]);
        var position = 4 + AttributeSetSize;

        if ((common & AttributeConstants.CommonError) != 0)
        {
            position += 4;
        }

        string? name = null;
        if ((common & AttributeConstants.CommonName) != 0)
        {
            name = ReadName(entry, position);
            position += ReferenceSize;
        }

        if (string.IsNullOrEmpty(name) || name is "." or "..")
        {
            return null;
        }

        uint device = 0, type = 0, flags = 0, mountStatus = 0, linkCount = 0;
        ulong fileId = 0;
        long totalSize = 0, allocatedSize = FileSystemEntryInfo.UnknownSize;
        var modified = DateTime.MinValue;

        if ((common & AttributeConstants.CommonDeviceId) != 0)
        {
            device = (uint)BinaryPrimitives.ReadInt32LittleEndian(entry[position..]);
            position += 4;
        }

        if ((common & AttributeConstants.CommonObjectType) != 0)
        {
            type = BinaryPrimitives.ReadUInt32LittleEndian(entry[position..]);
            position += 4;
        }

        if ((common & AttributeConstants.CommonModificationTime) != 0)
        {
            modified = ToDateTime(
                BinaryPrimitives.ReadInt64LittleEndian(entry[position..]),
                BinaryPrimitives.ReadInt64LittleEndian(entry[(position + 8)..]));
            position += TimespecSize;
        }

        if ((common & AttributeConstants.CommonFlags) != 0)
        {
            flags = BinaryPrimitives.ReadUInt32LittleEndian(entry[position..]);
            position += 4;
        }

        if ((common & AttributeConstants.CommonFileId) != 0)
        {
            fileId = BinaryPrimitives.ReadUInt64LittleEndian(entry[position..]);
            position += 8;
        }

        if ((directory & AttributeConstants.DirectoryMountStatus) != 0)
        {
            mountStatus = BinaryPrimitives.ReadUInt32LittleEndian(entry[position..]);
            position += 4;
        }

        if ((file & AttributeConstants.FileLinkCount) != 0)
        {
            linkCount = BinaryPrimitives.ReadUInt32LittleEndian(entry[position..]);
            position += 4;
        }

        if ((file & AttributeConstants.FileTotalSize) != 0)
        {
            totalSize = BinaryPrimitives.ReadInt64LittleEndian(entry[position..]);
            position += 8;
        }

        if ((file & AttributeConstants.FileAllocatedSize) != 0)
        {
            allocatedSize = BinaryPrimitives.ReadInt64LittleEndian(entry[position..]);
        }

        var entryType = type switch
        {
            AttributeConstants.TypeRegular => EntryType.File,
            AttributeConstants.TypeDirectory => EntryType.Directory,
            AttributeConstants.TypeSymbolicLink => EntryType.SymbolicLink,
            _ => EntryType.Other,
        };

        return new FileSystemEntryInfo(
            name,
            entryType,
            entryType == EntryType.Directory ? 0 : Math.Max(0, totalSize),
            entryType == EntryType.Directory ? 0 : allocatedSize,
            modified,
            ToAttributes(name, flags, mountStatus, entryType),
            fileId == 0 ? FileIdentity.Unknown : new FileIdentity(device, fileId),
            linkCount);
    }

    /// <summary>
    /// Reads a name referenced by an <c>attrreference_t</c>.
    /// </summary>
    /// <param name="entry">The entry's bytes.</param>
    /// <param name="referenceOffset">The offset of the reference within the entry.</param>
    /// <returns>The name, or <see langword="null"/> when the reference is invalid.</returns>
    private static string? ReadName(ReadOnlySpan<byte> entry, int referenceOffset)
    {
        var dataOffset = BinaryPrimitives.ReadInt32LittleEndian(entry[referenceOffset..]);
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[(referenceOffset + 4)..]);
        var start = referenceOffset + dataOffset;
        if (dataOffset <= 0 || length <= 0 || start + length > entry.Length)
        {
            return null;
        }

        var bytes = entry.Slice(start, length);
        var terminator = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(terminator >= 0 ? bytes[..terminator] : bytes);
    }

    /// <summary>
    /// Converts a <c>timespec</c> into a UTC timestamp.
    /// </summary>
    /// <param name="seconds">Seconds since the Unix epoch.</param>
    /// <param name="nanoseconds">Additional nanoseconds.</param>
    /// <returns>The timestamp, or <see cref="DateTime.MinValue"/> when out of range.</returns>
    private static DateTime ToDateTime(long seconds, long nanoseconds)
    {
        const long MaxSeconds = 253_402_300_799; // 9999-12-31T23:59:59Z
        if (seconds is < -62_135_596_800 or > MaxSeconds)
        {
            return DateTime.MinValue;
        }

        return DateTime.UnixEpoch.AddSeconds(seconds).AddTicks(nanoseconds / 100);
    }

    /// <summary>
    /// Converts BSD flags and mount status into entry attributes.
    /// </summary>
    /// <param name="name">The entry's name; dot-files are hidden.</param>
    /// <param name="flags">The <c>ATTR_CMN_FLAGS</c> value.</param>
    /// <param name="mountStatus">The <c>ATTR_DIR_MOUNTSTATUS</c> value.</param>
    /// <param name="type">The entry type.</param>
    /// <returns>The attributes.</returns>
    private static EntryAttributes ToAttributes(string name, uint flags, uint mountStatus, EntryType type)
    {
        var attributes = EntryAttributes.None;
        if ((flags & AttributeConstants.FlagHidden) != 0 || name.StartsWith('.'))
        {
            attributes |= EntryAttributes.Hidden;
        }

        if ((flags & AttributeConstants.FlagImmutable) != 0)
        {
            attributes |= EntryAttributes.ReadOnly;
        }

        if ((flags & AttributeConstants.FlagCompressed) != 0)
        {
            attributes |= EntryAttributes.Compressed;
        }

        if ((flags & AttributeConstants.FlagDataless) != 0)
        {
            attributes |= EntryAttributes.Offline;
        }

        if (type == EntryType.Directory)
        {
            if ((flags & AttributeConstants.FlagFirmlink) != 0)
            {
                attributes |= EntryAttributes.Firmlink;
            }

            if ((mountStatus & AttributeConstants.MountStatusMountPoint) != 0)
            {
                attributes |= EntryAttributes.MountPoint;
            }

            if ((mountStatus & AttributeConstants.MountStatusTrigger) != 0)
            {
                attributes |= EntryAttributes.AutomountTrigger;
            }
        }

        return attributes;
    }
}
