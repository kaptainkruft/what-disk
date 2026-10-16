using WhatDisk.Volumes;

namespace WhatDisk.Tests.Volumes;

/// <summary>
/// Tests the derived properties of <see cref="VolumeInfo"/>.
/// </summary>
public sealed class VolumeInfoTests
{
    /// <summary>
    /// Used space is derived from capacity and available space.
    /// </summary>
    [Fact]
    public void UsedBytes_IsCapacityMinusAvailableSpace()
    {
        var volume = new VolumeInfo { Id = "v", MountPoint = "/", Name = "Disk", TotalBytes = 1000, AvailableBytes = 250 };

        Assert.Equal(750, volume.UsedBytes);
        Assert.Equal(0.75, volume.UsedFraction, precision: 6);
    }

    /// <summary>
    /// An unknown capacity yields zero rather than dividing by zero.
    /// </summary>
    [Fact]
    public void CreateUnknown_HasNoCapacity()
    {
        var volume = VolumeInfo.CreateUnknown("/somewhere");

        Assert.Equal(VolumeKind.Unknown, volume.Kind);
        Assert.Equal(0, volume.UsedFraction);
        Assert.Equal("/somewhere", volume.MountPoint);
    }
}
