using WhatDisk.Volumes;

namespace WhatDisk.Tests.Volumes;

/// <summary>
/// Tests classification of file system format names.
/// </summary>
public sealed class FileSystemClassifierTests
{
    /// <summary>
    /// Known formats map to their kinds; ordinary disk formats are left to the caller.
    /// </summary>
    /// <param name="fileSystem">The format name.</param>
    /// <param name="expected">The expected kind, or <see langword="null"/>.</param>
    [Theory]
    [InlineData("proc", VolumeKind.Virtual)]
    [InlineData("devfs", VolumeKind.Virtual)]
    [InlineData("AUTOFS", VolumeKind.Virtual)]
    [InlineData("squashfs", VolumeKind.Virtual)]
    [InlineData("smbfs", VolumeKind.Network)]
    [InlineData("nfs4", VolumeKind.Network)]
    [InlineData("fuse.sshfs", VolumeKind.Network)]
    [InlineData("tmpfs", VolumeKind.Ram)]
    [InlineData("iso9660", VolumeKind.Optical)]
    [InlineData("apfs", null)]
    [InlineData("ext4", null)]
    [InlineData("NTFS", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Classify_MapsFormatNamesToKinds(string? fileSystem, VolumeKind? expected) =>
        Assert.Equal(expected, FileSystemClassifier.Classify(fileSystem));
}
