using System.Runtime.Versioning;
using WhatDisk.Volumes;

namespace WhatDisk.MacOS.Tests;

/// <summary>
/// Tests volume discovery on a real Mac. Skipped on other platforms.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacVolumeServiceTests
{
    /// <summary>The service under test.</summary>
    private readonly MacVolumeService _service = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MacVolumeServiceTests"/> class, skipping off macOS.
    /// </summary>
    public MacVolumeServiceTests() => Assert.SkipUnless(OperatingSystem.IsMacOS(), "Requires macOS.");

    /// <summary>
    /// The startup volume is described with a device number, a name and its capacity.
    /// </summary>
    [Fact]
    public void GetMountTable_DescribesTheStartupVolume()
    {
        var root = _service.GetMountTable().FindByMountPoint("/");

        Assert.NotNull(root);
        Assert.NotNull(root.Volume.DeviceId);
        Assert.False(string.IsNullOrWhiteSpace(root.Volume.Name));
        Assert.Equal(VolumeKind.Fixed, root.Volume.Kind);
        Assert.True(root.Volume.TotalBytes > 0);
    }

    /// <summary>
    /// The data volume is found from the device of a firmlinked directory.
    /// </summary>
    [Fact]
    public void GetMountTable_FindsTheDataVolumeByDevice()
    {
        var device = new MacFileSystemEnumerator().GetIdentity("/Users").Device;

        Assert.Equal("/System/Volumes/Data", _service.GetMountTable().FindByDevice(device)?.MountPoint);
    }

    /// <summary>
    /// The picker lists the startup disk first and hides virtual and system volumes.
    /// </summary>
    [Fact]
    public void GetSelectableVolumes_ListsTheStartupDiskFirstAndHidesSystemVolumes()
    {
        var volumes = _service.GetSelectableVolumes();

        Assert.Equal("/", volumes[0].MountPoint);
        Assert.DoesNotContain(volumes, static volume => volume.Kind == VolumeKind.Virtual);
        Assert.DoesNotContain(volumes, static volume => volume.MountPoint.StartsWith("/System/Volumes/", StringComparison.Ordinal));
    }
}
