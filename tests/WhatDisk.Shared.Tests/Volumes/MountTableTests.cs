using WhatDisk.Volumes;

namespace WhatDisk.Tests.Volumes;

/// <summary>
/// Tests the mount table lookups.
/// </summary>
public sealed class MountTableTests
{
    /// <summary>
    /// Mount points match with or without a trailing separator.
    /// </summary>
    [Fact]
    public void IsMountPoint_IgnoresTrailingSeparators()
    {
        var table = new MountTable([Entry("/Volumes/Backup/")], StringComparison.Ordinal);

        Assert.True(table.IsMountPoint("/Volumes/Backup"));
        Assert.True(table.IsMountPoint("/Volumes/Backup/"));
        Assert.False(table.IsMountPoint("/Volumes"));
    }

    /// <summary>
    /// The longest matching mount point contains a path.
    /// </summary>
    [Fact]
    public void FindContaining_ReturnsTheLongestMatchingMountPoint()
    {
        var table = new MountTable([Entry("/"), Entry("/home"), Entry("/home/user/mnt")], StringComparison.Ordinal);

        Assert.Equal("/home", table.FindContaining("/home/user/docs")?.MountPoint);
        Assert.Equal("/home/user/mnt", table.FindContaining("/home/user/mnt/a")?.MountPoint);
        Assert.Equal("/", table.FindContaining("/homeless")?.MountPoint);
    }

    /// <summary>
    /// Lookups by device prefer a full mount to a partial (bind) mount of the same file system.
    /// </summary>
    [Fact]
    public void FindByDevice_PrefersFullMountsOverPartialOnes()
    {
        var table = new MountTable([Entry("/mnt/bind", device: 7, partial: true), Entry("/data", device: 7)], StringComparison.Ordinal);

        Assert.Equal("/data", table.FindByDevice(7)?.MountPoint);
        Assert.Null(table.FindByDevice(8));
    }

    /// <summary>
    /// Case-insensitive platforms match mount points regardless of case.
    /// </summary>
    [Fact]
    public void FindByMountPoint_HonoursThePathComparison()
    {
        var table = new MountTable([Entry("/Volumes/Data")], StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(table.FindByMountPoint("/volumes/data"));
        Assert.Null(new MountTable([Entry("/Volumes/Data")], StringComparison.Ordinal).FindByMountPoint("/volumes/data"));
    }

    /// <summary>
    /// A later mount on the same directory shadows an earlier one.
    /// </summary>
    [Fact]
    public void FindByMountPoint_ReturnsTheMostRecentMount()
    {
        var first = Entry("/mnt", device: 1);
        var second = Entry("/mnt", device: 2);

        Assert.Same(second.Volume, new MountTable([first, second], StringComparison.Ordinal).FindByMountPoint("/mnt")?.Volume);
    }

    /// <summary>
    /// The empty table finds nothing.
    /// </summary>
    [Fact]
    public void Empty_FindsNothing()
    {
        Assert.Empty(MountTable.Empty.Entries);
        Assert.Null(MountTable.Empty.FindContaining("/"));
    }

    /// <summary>
    /// Creates a mount entry for tests.
    /// </summary>
    /// <param name="mountPoint">The mount point.</param>
    /// <param name="device">The device number.</param>
    /// <param name="partial">Whether the mount is partial.</param>
    /// <returns>The entry.</returns>
    private static MountEntry Entry(string mountPoint, ulong? device = null, bool partial = false) =>
        new(mountPoint, new VolumeInfo { Id = mountPoint, MountPoint = mountPoint, Name = mountPoint, DeviceId = device }, partial);
}
