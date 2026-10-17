using System.Runtime.Versioning;
using WhatDisk.Scanning;
using WhatDisk.Volumes;

namespace WhatDisk.Windows.Tests;

/// <summary>
/// Tests volume discovery against the machine's real volumes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsVolumeServiceTests
{
    /// <summary>The service under test.</summary>
    private readonly WindowsVolumeService _service = new();

    /// <summary>The root of the system drive, for example <c>C:\</c>.</summary>
    private readonly string _systemRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsVolumeServiceTests"/> class, skipping off Windows.
    /// </summary>
    public WindowsVolumeServiceTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires Windows.");
        _systemRoot = Path.GetPathRoot(Environment.SystemDirectory)!;
    }

    /// <summary>
    /// The mount table holds the system drive as a fixed NTFS (or ReFS) volume with a capacity and a device ID that
    /// matches the volume serial number reported for files on it.
    /// </summary>
    [Fact]
    public void GetMountTable_ContainsTheSystemDrive()
    {
        var table = _service.GetMountTable();

        var entry = table.FindByMountPoint(_systemRoot);
        Assert.NotNull(entry);
        var volume = entry.Volume;
        Assert.StartsWith(@"\\?\Volume{", volume.Id, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(VolumeKind.Fixed, volume.Kind);
        Assert.True(volume.FileSystem is "NTFS" or "ReFS", $"Unexpected file system {volume.FileSystem}.");
        Assert.True(volume.TotalBytes > volume.AvailableBytes && volume.AvailableBytes > 0);
        Assert.False(volume.IsReadOnly);
        Assert.EndsWith($"({_systemRoot[..2]})", volume.Name, StringComparison.Ordinal);
        Assert.Equal(new WindowsFileSystemEnumerator().GetIdentity(Environment.SystemDirectory).Device, volume.DeviceId);
        Assert.Same(entry, table.FindContaining(Path.Join(_systemRoot, "Windows", "System32")));
        Assert.Same(entry, table.FindContaining(Path.Join(_systemRoot.ToLowerInvariant(), "windows")));
    }

    /// <summary>
    /// The volume list offers the system drive once.
    /// </summary>
    [Fact]
    public void GetSelectableVolumes_OffersTheSystemDriveOnce()
    {
        var volumes = _service.GetSelectableVolumes();

        Assert.Single(volumes, volume => string.Equals(volume.MountPoint, _systemRoot, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(volumes.Count, volumes.Select(static volume => volume.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(volumes, static volume => Assert.True(volume.TotalBytes > 0));
    }

    /// <summary>
    /// A path on the system drive is described as the system volume.
    /// </summary>
    [Fact]
    public void QueryVolume_DescribesThePathsVolume()
    {
        var volume = _service.QueryVolume(Path.GetTempPath());
        var expected = _service.GetMountTable().FindContaining(Path.GetTempPath())?.Volume;

        Assert.NotNull(volume);
        Assert.Equal(expected?.Id, volume.Id);
        Assert.Equal(VolumeKind.Fixed, volume.Kind);
        Assert.Null(_service.QueryVolume(@"Q:\does\not\exist\anywhere"));
    }

    /// <summary>
    /// A scan of a temporary folder with the Windows services counts every file once, at its allocated size, and does
    /// not follow a junction back into the folder (the junction counts as one item of no size).
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [Fact]
    public async Task Scan_CountsFilesOnceAndDoesNotFollowJunctions()
    {
        using var tree = new TempTree();
        tree.Write("a/one.bin", 100_000);
        tree.Write("a/b/two.bin", 50_000);
        tree.Write("c.txt", 10);
        tree.Junction("a/loop", tree.Root);
        var scanner = new DiskScanner(new WindowsFileSystemEnumerator(), _service, TimeProvider.System);

        using var session = scanner.Start(new ScanRequest([tree.Root]));
        Assert.Equal(ScanStatus.Completed, await session.Completion.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));

        var root = Assert.Single(session.Roots);
        Assert.Equal(4, root.FileCount); // three files and the junction
        Assert.Equal(150_010, root.LogicalSize);
        Assert.True(root.Size >= 150_000);
        var loop = ((DirectoryNode)((DirectoryNode)root).Children.Single(static child => child.Name == "a")).Children.Single(static child => child.Name == "loop");
        Assert.Equal(NodeKind.SymbolicLink, loop.Kind);
    }
}
