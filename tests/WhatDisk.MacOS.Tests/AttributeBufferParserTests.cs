using System.Buffers.Binary;
using WhatDisk.MacOS.Interop;
using WhatDisk.Scanning;

namespace WhatDisk.MacOS.Tests;

/// <summary>
/// Tests decoding of synthetic <c>getattrlistbulk</c> buffers. These run on every platform.
/// </summary>
public sealed class AttributeBufferParserTests
{
    /// <summary>
    /// A regular file reports its sizes, identity, link count and modification time.
    /// </summary>
    [Fact]
    public void ParseEntries_DecodesAFile()
    {
        var buffer = new AttributeBufferBuilder()
            .Add("report.pdf", AttributeConstants.TypeRegular, device: 7, fileId: 1234, linkCount: 2, totalSize: 1000, allocatedSize: 4096);

        var entry = Assert.Single(Parse(buffer));

        Assert.Equal("report.pdf", entry.Name);
        Assert.Equal(EntryType.File, entry.Type);
        Assert.Equal(1000, entry.LogicalSize);
        Assert.Equal(4096, entry.AllocatedSize);
        Assert.Equal(new FileIdentity(7, 1234), entry.Identity);
        Assert.Equal(2u, entry.LinkCount);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(5), entry.LastWriteTimeUtc);
    }

    /// <summary>
    /// Directories report mount points, automount triggers and firmlinks.
    /// </summary>
    [Fact]
    public void ParseEntries_DecodesDirectoryBoundaries()
    {
        var buffer = new AttributeBufferBuilder()
            .Add("Data", AttributeConstants.TypeDirectory, mountStatus: AttributeConstants.MountStatusMountPoint)
            .Add("Users", AttributeConstants.TypeDirectory, flags: AttributeConstants.FlagFirmlink)
            .Add("home", AttributeConstants.TypeDirectory, mountStatus: AttributeConstants.MountStatusTrigger)
            .Add("plain", AttributeConstants.TypeDirectory);

        var entries = Parse(buffer).ToDictionary(static entry => entry.Name);

        Assert.Equal(EntryAttributes.MountPoint, entries["Data"].Attributes);
        Assert.Equal(EntryAttributes.Firmlink, entries["Users"].Attributes);
        Assert.Equal(EntryAttributes.AutomountTrigger, entries["home"].Attributes);
        Assert.Equal(EntryAttributes.None, entries["plain"].Attributes);
        Assert.All(entries.Values, static entry => Assert.Equal(EntryType.Directory, entry.Type));
        Assert.All(entries.Values, static entry => Assert.Equal(0, entry.LogicalSize));
    }

    /// <summary>
    /// Links, special files, hidden and compressed flags are mapped.
    /// </summary>
    [Fact]
    public void ParseEntries_MapsTypesAndFlags()
    {
        var buffer = new AttributeBufferBuilder()
            .Add("link", AttributeConstants.TypeSymbolicLink, totalSize: 12)
            .Add("socket", 6)
            .Add("secret", AttributeConstants.TypeRegular, flags: AttributeConstants.FlagHidden | AttributeConstants.FlagCompressed)
            .Add(".profile", AttributeConstants.TypeRegular)
            .Add("cloud.doc", AttributeConstants.TypeRegular, flags: AttributeConstants.FlagDataless);

        var entries = Parse(buffer).ToDictionary(static entry => entry.Name);

        Assert.Equal(EntryType.SymbolicLink, entries["link"].Type);
        Assert.Equal(EntryType.Other, entries["socket"].Type);
        Assert.Equal(EntryAttributes.Hidden | EntryAttributes.Compressed, entries["secret"].Attributes);
        Assert.Equal(EntryAttributes.Hidden, entries[".profile"].Attributes);
        Assert.Equal(EntryAttributes.Offline, entries["cloud.doc"].Attributes);
    }

    /// <summary>
    /// An entry with a per-entry error and missing attributes is still decoded with what was returned.
    /// </summary>
    [Fact]
    public void ParseEntries_ToleratesErrorsAndMissingAttributes()
    {
        var buffer = new AttributeBufferBuilder()
            .Add("broken", AttributeConstants.TypeRegular, fileId: null, error: 13, totalSize: 5)
            .Add("ok", AttributeConstants.TypeRegular, totalSize: 6);

        var entries = Parse(buffer);

        Assert.Equal(["broken", "ok"], entries.Select(static entry => entry.Name));
        Assert.False(entries[0].Identity.IsKnown);
        Assert.Equal(5, entries[0].LogicalSize);
    }

    /// <summary>
    /// Entries without a name are skipped.
    /// </summary>
    [Fact]
    public void ParseEntries_SkipsEntriesWithoutNames()
    {
        var buffer = new AttributeBufferBuilder().Add(null, AttributeConstants.TypeRegular).Add("kept", AttributeConstants.TypeRegular);

        Assert.Equal("kept", Assert.Single(Parse(buffer)).Name);
    }

    /// <summary>
    /// Names are decoded as UTF-8.
    /// </summary>
    [Fact]
    public void ParseEntries_DecodesUtf8Names()
    {
        var buffer = new AttributeBufferBuilder().Add("Fotos – Ålesund 😀.jpg", AttributeConstants.TypeRegular);

        Assert.Equal("Fotos – Ålesund 😀.jpg", Assert.Single(Parse(buffer)).Name);
    }

    /// <summary>
    /// A buffer whose entry length is impossible is rejected.
    /// </summary>
    [Fact]
    public void ParseEntries_RejectsMalformedBuffers()
    {
        var bytes = new AttributeBufferBuilder().Add("x", AttributeConstants.TypeRegular).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 9999);

        Assert.Throws<InvalidDataException>(() => AttributeBufferParser.ParseEntries(bytes, 1, new List<FileSystemEntryInfo>()));
    }

    /// <summary>
    /// The identity request result decodes device and file number.
    /// </summary>
    [Fact]
    public void ParseIdentity_DecodesDeviceAndFileNumber()
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 16);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), unchecked((int)0x8100000d));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 2);

        Assert.Equal(new FileIdentity(0x8100000d, 2), AttributeBufferParser.ParseIdentity(bytes));
        Assert.Equal(FileIdentity.Unknown, AttributeBufferParser.ParseIdentity(new byte[8]));
    }

    /// <summary>
    /// Parses a built buffer.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <returns>The decoded entries.</returns>
    private static List<FileSystemEntryInfo> Parse(AttributeBufferBuilder builder)
    {
        var entries = new List<FileSystemEntryInfo>();
        AttributeBufferParser.ParseEntries(builder.ToArray(), builder.Count, entries);
        return entries;
    }
}
