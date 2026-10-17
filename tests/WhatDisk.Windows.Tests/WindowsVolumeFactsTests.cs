using WhatDisk.Volumes;

namespace WhatDisk.Windows.Tests;

/// <summary>
/// Tests turning what Windows reports about volumes into volume descriptions (pure code; runs on any OS).
/// </summary>
public sealed class WindowsVolumeFactsTests
{
    /// <summary>A volume GUID path.</summary>
    private const string VolumeId = @"\\?\Volume{12345678-1234-1234-1234-123456789abc}\";

    /// <summary>
    /// Volumes are named as File Explorer names them.
    /// </summary>
    /// <param name="mountPoint">The mount point.</param>
    /// <param name="label">The label.</param>
    /// <param name="driveType">The drive type.</param>
    /// <param name="remotePath">The mapped network path.</param>
    /// <param name="expected">The name.</param>
    [Theory]
    [InlineData(@"C:\", "", WindowsVolumeFacts.DriveFixed, null, "Local Disk (C:)")]
    [InlineData(@"D:\", "Data", WindowsVolumeFacts.DriveFixed, null, "Data (D:)")]
    [InlineData(@"E:\", null, WindowsVolumeFacts.DriveRemovable, null, "USB Drive (E:)")]
    [InlineData(@"F:\", "  ", WindowsVolumeFacts.DriveOptical, null, "CD Drive (F:)")]
    [InlineData(@"Z:\", "Media", WindowsVolumeFacts.DriveRemote, @"\\nas\media", @"media (\\nas) (Z:)")]
    [InlineData(@"\\nas\backup\", "backup", WindowsVolumeFacts.DriveRemote, @"\\nas\backup\", @"backup (\\nas)")]
    [InlineData(@"C:\mnt\archive\", "Archive", WindowsVolumeFacts.DriveFixed, null, @"Archive (C:\mnt\archive)")]
    public void DisplayName_FollowsFileExplorer(string mountPoint, string? label, uint driveType, string? remotePath, string expected)
    {
        var facts = new WindowsVolumeFacts(VolumeId, mountPoint, label, "NTFS", driveType, 0, 1, 100, 50, remotePath);

        Assert.Equal(expected, facts.DisplayName);
    }

    /// <summary>
    /// The drive type decides the kind; read-only volumes are flagged; the serial number becomes the device ID.
    /// </summary>
    [Fact]
    public void ToVolumeInfo_MapsEveryFact()
    {
        var facts = new WindowsVolumeFacts(VolumeId, @"D:\", "Data", "ReFS", WindowsVolumeFacts.DriveFixed, WindowsVolumeFacts.ReadOnlyVolumeFlag, 0x1234ABCD, 2000, 500);

        var volume = facts.ToVolumeInfo();

        Assert.Equal(VolumeId, volume.Id);
        Assert.Equal(@"D:\", volume.MountPoint);
        Assert.Equal("Data (D:)", volume.Name);
        Assert.Equal("ReFS", volume.FileSystem);
        Assert.Equal(VolumeKind.Fixed, volume.Kind);
        Assert.True(volume.IsReadOnly);
        Assert.Equal(0x1234ABCDUL, volume.DeviceId);
        Assert.Equal(1500, volume.UsedBytes);

        Assert.Equal(VolumeKind.Network, (facts with { DriveType = WindowsVolumeFacts.DriveRemote }).Kind);
        Assert.Equal(VolumeKind.Removable, (facts with { DriveType = WindowsVolumeFacts.DriveRemovable }).Kind);
        Assert.Equal(VolumeKind.Optical, (facts with { DriveType = WindowsVolumeFacts.DriveOptical }).Kind);
        Assert.Equal(VolumeKind.Ram, (facts with { DriveType = WindowsVolumeFacts.DriveRamDisk }).Kind);
        Assert.Equal(VolumeKind.Unknown, (facts with { DriveType = WindowsVolumeFacts.DriveUnknown }).Kind);
        Assert.Null((facts with { SerialNumber = 0 }).ToVolumeInfo().DeviceId);
        Assert.False((facts with { Flags = 0 }).ToVolumeInfo().IsReadOnly);
    }

    /// <summary>
    /// The volume list shows each volume once at its drive letter, plus network drives, in drive-letter order; empty
    /// drives, subst drives and volumes mounted only in folders are left out.
    /// </summary>
    [Fact]
    public void SelectVolumes_ListsDrivesOnce()
    {
        var system = Volume("system", @"C:\", VolumeKind.Fixed);
        var data = Volume("data", @"D:\", VolumeKind.Fixed);
        var emptyCard = Volume("card", @"E:\", VolumeKind.Removable) with { TotalBytes = 0 };
        var share = Volume("network:\\\\nas\\media", @"Z:\", VolumeKind.Network);
        var mountedOnly = Volume("archive", @"C:\mnt\archive", VolumeKind.Fixed);
        MountEntry[] entries =
        [
            new(@"Z:\", share),
            new(@"D:\", data),
            new(@"C:\mnt\data", data),
            new(@"C:\", system),
            new(@"E:\", emptyCard),
            new(@"S:\", system, IsPartial: true),
            new(@"C:\mnt\archive", mountedOnly),
        ];

        var selectable = WindowsVolumeFacts.SelectVolumes(entries);

        Assert.Equal(["system", "data", "network:\\\\nas\\media"], selectable.Select(static volume => volume.Id));
    }

    /// <summary>
    /// Network paths split into server and share; other paths do not.
    /// </summary>
    [Fact]
    public void ParseShare_SplitsNetworkPaths()
    {
        Assert.Equal(("nas", "media"), WindowsVolumeFacts.ParseShare(@"\\nas\media\films"));
        Assert.Equal(("nas", "media"), WindowsVolumeFacts.ParseShare(@"\\?\UNC\nas\media"));
        Assert.Null(WindowsVolumeFacts.ParseShare(@"\\nas"));
        Assert.Null(WindowsVolumeFacts.ParseShare(@"C:\Users"));
    }

    /// <summary>
    /// Creates a volume description for the tests.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="mountPoint">The mount point.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>The volume.</returns>
    private static VolumeInfo Volume(string id, string mountPoint, VolumeKind kind) =>
        new() { Id = id, MountPoint = mountPoint, Name = id, Kind = kind, TotalBytes = 1000, AvailableBytes = 100 };
}
