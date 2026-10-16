using System.Buffers.Binary;
using System.Text;
using WhatDisk.MacOS.Interop;

namespace WhatDisk.MacOS.Tests;

/// <summary>
/// Builds synthetic <c>getattrlistbulk</c> buffers laid out exactly as the kernel packs them.
/// </summary>
internal sealed class AttributeBufferBuilder
{
    /// <summary>The bytes written so far.</summary>
    private readonly List<byte> _bytes = [];

    /// <summary>
    /// Gets the number of entries added.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Appends an entry.
    /// </summary>
    /// <param name="name">The name, or <see langword="null"/> to omit it.</param>
    /// <param name="type">The <c>vtype</c>.</param>
    /// <param name="device">The device number.</param>
    /// <param name="fileId">The file number, or <see langword="null"/> to omit it.</param>
    /// <param name="flags">The BSD flags.</param>
    /// <param name="mountStatus">The mount status (directories only).</param>
    /// <param name="linkCount">The link count (files only).</param>
    /// <param name="totalSize">The logical size (files only).</param>
    /// <param name="allocatedSize">The allocated size (files only).</param>
    /// <param name="error">A per-entry error, or zero to omit it.</param>
    /// <param name="modifiedSeconds">The modification time in Unix seconds.</param>
    /// <returns>This builder.</returns>
    public AttributeBufferBuilder Add(
        string? name,
        uint type,
        int device = 0x01000013,
        ulong? fileId = 42,
        uint flags = 0,
        uint mountStatus = 0,
        uint linkCount = 1,
        long totalSize = 0,
        long allocatedSize = 0,
        uint error = 0,
        long modifiedSeconds = 1_767_225_600)
    {
        var isDirectory = type == AttributeConstants.TypeDirectory;
        var common = AttributeConstants.CommonReturnedAttributes | AttributeConstants.CommonDeviceId | AttributeConstants.CommonObjectType
            | AttributeConstants.CommonModificationTime | AttributeConstants.CommonFlags;
        if (name is not null)
        {
            common |= AttributeConstants.CommonName;
        }

        if (fileId is not null)
        {
            common |= AttributeConstants.CommonFileId;
        }

        if (error != 0)
        {
            common |= AttributeConstants.CommonError;
        }

        var directoryAttributes = isDirectory ? AttributeConstants.DirectoryMountStatus : 0;
        var fileAttributes = isDirectory ? 0 : AttributeConstants.FileLinkCount | AttributeConstants.FileTotalSize | AttributeConstants.FileAllocatedSize;

        var fixedPart = new List<byte>();
        void U32(uint value) => fixedPart.AddRange(BitConverter.GetBytes(value));
        void I64(long value) => fixedPart.AddRange(BitConverter.GetBytes(value));

        U32(common);
        U32(0);
        U32(directoryAttributes);
        U32(fileAttributes);
        U32(0);
        if (error != 0)
        {
            U32(error);
        }

        var nameReferenceAt = -1;
        if (name is not null)
        {
            nameReferenceAt = 4 + fixedPart.Count;
            U32(0);
            U32(0);
        }

        U32((uint)device);
        U32(type);
        I64(modifiedSeconds);
        I64(500);
        U32(flags);
        if (fileId is { } id)
        {
            fixedPart.AddRange(BitConverter.GetBytes(id));
        }

        if (isDirectory)
        {
            U32(mountStatus);
        }
        else
        {
            U32(linkCount);
            I64(totalSize);
            I64(allocatedSize);
        }

        var entry = new List<byte>(BitConverter.GetBytes(0u));
        entry.AddRange(fixedPart);
        if (name is not null)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name + "\0");
            var nameOffset = entry.Count;
            entry.AddRange(nameBytes);
            var reference = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(reference, nameOffset - nameReferenceAt);
            BinaryPrimitives.WriteUInt32LittleEndian(reference.AsSpan(4), (uint)nameBytes.Length);
            for (var index = 0; index < 8; index++)
            {
                entry[nameReferenceAt + index] = reference[index];
            }
        }

        while (entry.Count % 8 != 0)
        {
            entry.Add(0);
        }

        var length = BitConverter.GetBytes((uint)entry.Count);
        for (var index = 0; index < 4; index++)
        {
            entry[index] = length[index];
        }

        _bytes.AddRange(entry);
        Count++;
        return this;
    }

    /// <summary>
    /// Gets the buffer.
    /// </summary>
    /// <returns>The bytes.</returns>
    public byte[] ToArray() => [.. _bytes];
}
