using WhatDisk.Scanning;

namespace WhatDisk.Windows.Tests;

/// <summary>
/// Tests decoding and classifying directory listings (pure code; runs on any OS).
/// </summary>
public sealed class DirectoryEntryParserTests
{
    /// <summary>The volume serial number used as the device of entries.</summary>
    private const ulong Device = 0xABCD1234;

    /// <summary>
    /// Every layout yields the names, sizes, attributes and times of its entries, skipping <c>.</c> and <c>..</c>, and
    /// the layouts with file IDs report them.
    /// </summary>
    /// <param name="layout">The layout (a <see cref="DirectoryListingClass"/> value).</param>
    [Theory]
    [InlineData((int)DirectoryListingClass.IdExtended)]
    [InlineData((int)DirectoryListingClass.IdBoth)]
    [InlineData((int)DirectoryListingClass.Full)]
    public void Parse_ReadsEveryLayout(int layout)
    {
        var listingClass = (DirectoryListingClass)layout;
        var written = new DateTime(2026, 10, 1, 12, 30, 0, DateTimeKind.Utc);
        var buffer = new DirectoryListingBuilder(listingClass)
            .Add(".", DirectoryEntryParser.DirectoryAttribute)
            .Add("..", DirectoryEntryParser.DirectoryAttribute)
            .Add("report.pdf", 0x20 | DirectoryEntryParser.HiddenAttribute, endOfFile: 1000, allocation: 4096, lastWrite: written, fileId: 42)
            .Add("Photos", DirectoryEntryParser.DirectoryAttribute, fileId: 43)
            .Add("naïve 名前.txt", 0x20, endOfFile: 5, allocation: 0, fileId: 44)
            .Build();
        var raw = new List<RawDirectoryEntry>();

        DirectoryEntryParser.Parse(buffer, listingClass, raw);

        Assert.Equal(["report.pdf", "Photos", "naïve 名前.txt"], raw.Select(static entry => entry.Name));
        var entries = raw.Select(entry => DirectoryEntryParser.ToEntry(in entry, Device, static _ => false)).ToList();
        var report = entries[0];
        Assert.Equal(EntryType.File, report.Type);
        Assert.Equal(1000, report.LogicalSize);
        Assert.Equal(4096, report.AllocatedSize);
        Assert.Equal(written, report.LastWriteTimeUtc);
        Assert.Equal(EntryAttributes.Hidden, report.Attributes);
        Assert.Equal(EntryType.Directory, entries[1].Type);
        Assert.Equal(0, entries[1].LogicalSize);
        Assert.Equal(FileSystemEntryInfo.UnknownSize, entries[1].AllocatedSize);
        Assert.Equal(0, entries[2].AllocatedSize);
        if (listingClass == DirectoryListingClass.Full)
        {
            Assert.False(report.Identity.IsKnown);
        }
        else
        {
            Assert.Equal(new FileIdentity(Device, 42), report.Identity);
        }
    }

    /// <summary>
    /// Symbolic links and junctions are links; a junction that mounts a volume is a mount point; cloud placeholders,
    /// deduplicated files and other non-link reparse points are ordinary items; Unix sockets are special.
    /// </summary>
    /// <param name="layout">The layout (a <see cref="DirectoryListingClass"/> value; the reparse tag is stored differently in each).</param>
    [Theory]
    [InlineData((int)DirectoryListingClass.IdExtended)]
    [InlineData((int)DirectoryListingClass.IdBoth)]
    [InlineData((int)DirectoryListingClass.Full)]
    public void ToEntry_ClassifiesReparsePoints(int layout)
    {
        var listingClass = (DirectoryListingClass)layout;
        const uint Directory = DirectoryEntryParser.DirectoryAttribute;
        const uint Reparse = DirectoryEntryParser.ReparsePointAttribute;
        var buffer = new DirectoryListingBuilder(listingClass)
            .Add("file-link", Reparse, reparseTag: DirectoryEntryParser.SymbolicLinkTag)
            .Add("folder-link", Directory | Reparse, reparseTag: DirectoryEntryParser.SymbolicLinkTag)
            .Add("Application Data", Directory | Reparse | DirectoryEntryParser.HiddenAttribute, reparseTag: DirectoryEntryParser.MountPointTag)
            .Add("Backup Volume", Directory | Reparse, reparseTag: DirectoryEntryParser.MountPointTag)
            .Add("OneDrive Folder", Directory | Reparse, reparseTag: 0x9000601A)
            .Add("online-only.docx", Reparse | DirectoryEntryParser.RecallOnDataAccessAttribute | DirectoryEntryParser.OfflineAttribute, endOfFile: 9000, allocation: 0, reparseTag: 0x9000701A)
            .Add("deduplicated.vhdx", Reparse | DirectoryEntryParser.SparseAttribute, endOfFile: 1 << 30, allocation: 1 << 20, reparseTag: 0x80000013)
            .Add("app.sock", Reparse, reparseTag: DirectoryEntryParser.UnixSocketTag)
            .Build();
        var raw = new List<RawDirectoryEntry>();
        DirectoryEntryParser.Parse(buffer, listingClass, raw);

        var entries = raw.ToDictionary(
            static entry => entry.Name,
            entry => DirectoryEntryParser.ToEntry(in entry, Device, static name => name == "Backup Volume"));

        Assert.Equal(EntryType.SymbolicLink, entries["file-link"].Type);
        Assert.Equal(EntryType.SymbolicLink, entries["folder-link"].Type);
        Assert.Equal(EntryType.SymbolicLink, entries["Application Data"].Type);
        Assert.Equal(EntryType.Directory, entries["Backup Volume"].Type);
        Assert.True(entries["Backup Volume"].Attributes.HasFlag(EntryAttributes.MountPoint));
        Assert.Equal(EntryType.Directory, entries["OneDrive Folder"].Type);
        Assert.Equal(EntryType.File, entries["online-only.docx"].Type);
        Assert.True(entries["online-only.docx"].Attributes.HasFlag(EntryAttributes.Offline));
        Assert.Equal(0, entries["online-only.docx"].AllocatedSize);
        Assert.Equal(EntryType.File, entries["deduplicated.vhdx"].Type);
        Assert.Equal(1 << 20, entries["deduplicated.vhdx"].EffectiveSize);
        Assert.Equal(EntryType.Other, entries["app.sock"].Type);
        Assert.All(entries.Values, static entry => Assert.True(entry.Attributes.HasFlag(EntryAttributes.ReparsePoint)));
    }

    /// <summary>
    /// The extended-attribute size of an ordinary item is not mistaken for a reparse tag.
    /// </summary>
    [Fact]
    public void Parse_IgnoresTagsOfItemsThatAreNotReparsePoints()
    {
        var buffer = new DirectoryListingBuilder(DirectoryListingClass.IdBoth)
            .Add("with-ea", DirectoryEntryParser.DirectoryAttribute, reparseTag: DirectoryEntryParser.SymbolicLinkTag)
            .Build();
        var raw = new List<RawDirectoryEntry>();

        DirectoryEntryParser.Parse(buffer, DirectoryListingClass.IdBoth, raw);

        Assert.Equal(0u, Assert.Single(raw).ReparseTag);
        Assert.Equal(EntryType.Directory, DirectoryEntryParser.ToEntry(raw[0], Device, static _ => true).Type);
    }

    /// <summary>
    /// A buffer filled to the brim is read entry by entry, and a record that claims more bytes than remain is dropped
    /// rather than read past the end.
    /// </summary>
    [Fact]
    public void Parse_HandlesFullAndTruncatedBuffers()
    {
        var builder = new DirectoryListingBuilder(DirectoryListingClass.IdExtended);
        for (var index = 0; index < 300; index++)
        {
            builder.Add($"file-{index:D4}-with-a-reasonably-long-name.bin", endOfFile: index);
        }

        var raw = new List<RawDirectoryEntry>();
        DirectoryEntryParser.Parse(builder.Build(), DirectoryListingClass.IdExtended, raw);
        Assert.Equal(300, raw.Count);
        Assert.Equal(299, raw[^1].EndOfFile);

        var truncated = new DirectoryListingBuilder(DirectoryListingClass.IdExtended).Add("long-name.txt").Build()[..96];
        raw.Clear();
        DirectoryEntryParser.Parse(truncated, DirectoryListingClass.IdExtended, raw);
        Assert.Empty(raw);
    }

    /// <summary>
    /// File attributes map to the scanner's attributes; online-only cloud files count as offline.
    /// </summary>
    [Fact]
    public void MapAttributes_MapsEveryFlag()
    {
        Assert.Equal(EntryAttributes.None, DirectoryEntryParser.MapAttributes(0x20));
        Assert.Equal(
            EntryAttributes.Hidden | EntryAttributes.System | EntryAttributes.ReadOnly | EntryAttributes.Compressed | EntryAttributes.Sparse,
            DirectoryEntryParser.MapAttributes(0x2 | 0x4 | 0x1 | 0x800 | 0x200));
        Assert.Equal(EntryAttributes.Offline, DirectoryEntryParser.MapAttributes(DirectoryEntryParser.OfflineAttribute));
        Assert.Equal(EntryAttributes.Offline, DirectoryEntryParser.MapAttributes(DirectoryEntryParser.RecallOnDataAccessAttribute));
        Assert.Equal(EntryAttributes.None, DirectoryEntryParser.MapAttributes(DirectoryEntryParser.RecallOnOpenAttribute));
    }

    /// <summary>
    /// Unset and out-of-range file times become <see cref="DateTime.MinValue"/>.
    /// </summary>
    [Fact]
    public void ToDateTime_HandlesUnsetTimes()
    {
        Assert.Equal(DateTime.MinValue, DirectoryEntryParser.ToDateTime(0));
        Assert.Equal(DateTime.MinValue, DirectoryEntryParser.ToDateTime(-1));
        Assert.Equal(DateTime.MinValue, DirectoryEntryParser.ToDateTime(long.MaxValue));
        Assert.Equal(DateTime.UnixEpoch, DirectoryEntryParser.ToDateTime(DateTime.UnixEpoch.ToFileTimeUtc()));
    }
}
