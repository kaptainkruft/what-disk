using System.Globalization;
using WhatDisk.Formatting;
using WhatDisk.ViewModels;
using WhatDisk.Volumes;

namespace WhatDisk.Tests.ViewModels;

/// <summary>
/// Tests the volume picker items.
/// </summary>
public sealed class VolumeItemViewModelTests
{
    /// <summary>
    /// Capacity is summarised as free of total, with the used share.
    /// </summary>
    [Fact]
    public void CapacityText_SummarisesFreeAndTotalSpace()
    {
        var volume = new VolumeInfo { Id = "v", MountPoint = "/Volumes/Backup", Name = "Backup", TotalBytes = 2_000_000_000_000, AvailableBytes = 500_000_000_000, Kind = VolumeKind.Network };
        var item = new VolumeItemViewModel(volume, new ByteSizeFormatter(ByteUnitConvention.PowersOf1000, CultureInfo.InvariantCulture));

        Assert.Equal("500 GB free of 2.00 TB", item.CapacityText);
        Assert.Equal(0.75, item.UsedFraction, precision: 9);
        Assert.Equal("75.0%", item.UsedPercentText);
        Assert.True(item.IsNetwork);
    }

    /// <summary>
    /// A volume with unknown capacity shows its file system instead.
    /// </summary>
    [Fact]
    public void CapacityText_FallsBackToTheFileSystem()
    {
        var volume = new VolumeInfo { Id = "v", MountPoint = "/x", Name = "x", FileSystem = "exfat" };
        var item = new VolumeItemViewModel(volume, new ByteSizeFormatter(ByteUnitConvention.PowersOf1000, CultureInfo.InvariantCulture));

        Assert.Equal("exfat", item.CapacityText);
        Assert.Equal(string.Empty, item.UsedPercentText);
    }
}
