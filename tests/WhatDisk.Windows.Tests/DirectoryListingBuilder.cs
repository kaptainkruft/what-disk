using System.Buffers.Binary;
using System.Text;

namespace WhatDisk.Windows.Tests;

/// <summary>
/// Builds directory listing buffers in the layouts <c>GetFileInformationByHandleEx</c> fills, for parser tests.
/// </summary>
/// <param name="listingClass">The layout to build.</param>
internal sealed class DirectoryListingBuilder(DirectoryListingClass listingClass)
{
    /// <summary>The records built so far, each padded to a multiple of 8 bytes.</summary>
    private readonly List<byte[]> _records = [];

    /// <summary>
    /// Adds an entry.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="attributes">The <c>FILE_ATTRIBUTE_*</c> flags.</param>
    /// <param name="endOfFile">The size in bytes.</param>
    /// <param name="allocation">The space allocated.</param>
    /// <param name="lastWrite">The last write time.</param>
    /// <param name="reparseTag">The reparse tag, or zero.</param>
    /// <param name="fileId">The file ID.</param>
    /// <returns>This builder.</returns>
    public DirectoryListingBuilder Add(string name, uint attributes = 0x20, long endOfFile = 0, long allocation = 0, DateTime? lastWrite = null, uint reparseTag = 0, ulong fileId = 0)
    {
        var nameOffset = listingClass switch
        {
            DirectoryListingClass.IdExtended => 88,
            DirectoryListingClass.IdBoth => 104,
            _ => 68,
        };
        var nameBytes = Encoding.Unicode.GetBytes(name);
        var length = (nameOffset + nameBytes.Length + 7) & ~7;
        var record = new byte[length];
        var span = record.AsSpan();
        BinaryPrimitives.WriteInt64LittleEndian(span[8..], (lastWrite ?? DateTime.UnixEpoch).ToFileTimeUtc());
        BinaryPrimitives.WriteInt64LittleEndian(span[24..], (lastWrite ?? DateTime.UnixEpoch).ToFileTimeUtc());
        BinaryPrimitives.WriteInt64LittleEndian(span[40..], endOfFile);
        BinaryPrimitives.WriteInt64LittleEndian(span[48..], allocation);
        BinaryPrimitives.WriteUInt32LittleEndian(span[56..], attributes);
        BinaryPrimitives.WriteInt32LittleEndian(span[60..], nameBytes.Length);
        switch (listingClass)
        {
            case DirectoryListingClass.IdExtended:
                BinaryPrimitives.WriteUInt32LittleEndian(span[68..], reparseTag);
                BinaryPrimitives.WriteUInt64LittleEndian(span[72..], fileId);
                break;
            case DirectoryListingClass.IdBoth:
                BinaryPrimitives.WriteUInt32LittleEndian(span[64..], reparseTag);
                BinaryPrimitives.WriteUInt64LittleEndian(span[96..], fileId);
                break;
            default:
                BinaryPrimitives.WriteUInt32LittleEndian(span[64..], reparseTag);
                break;
        }

        nameBytes.CopyTo(span[nameOffset..]);
        _records.Add(record);
        return this;
    }

    /// <summary>
    /// Builds the buffer, chaining the records with their next-entry offsets and padding the end with stale bytes as
    /// a real buffer would have.
    /// </summary>
    /// <param name="bufferSize">The size of the buffer.</param>
    /// <returns>The buffer.</returns>
    public byte[] Build(int bufferSize = 64 * 1024)
    {
        var buffer = new byte[bufferSize];
        buffer.AsSpan().Fill(0xCD);
        var offset = 0;
        for (var index = 0; index < _records.Count; index++)
        {
            var record = _records[index];
            record.CopyTo(buffer, offset);
            var next = index == _records.Count - 1 ? 0 : record.Length;
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), next);
            offset += record.Length;
        }

        return buffer;
    }
}
