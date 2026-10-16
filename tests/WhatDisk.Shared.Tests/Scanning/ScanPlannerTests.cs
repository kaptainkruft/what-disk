using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;
using WhatDisk.Volumes;

namespace WhatDisk.Tests.Scanning;

/// <summary>
/// Tests how requested paths become top-level roots.
/// </summary>
public sealed class ScanPlannerTests
{
    /// <summary>The in-memory file system.</summary>
    private readonly FakeFileSystem _fs = new();

    /// <summary>The volume service.</summary>
    private readonly FakeVolumeService _volumes = new();

    /// <summary>
    /// A path on a volume reached through a firmlink is attributed to the volume its device belongs to.
    /// </summary>
    [Fact]
    public void CreateRoots_ResolvesTheVolumeByDeviceBeforeByPathPrefix()
    {
        _volumes.Mount("system", _fs.Root, deviceId: 1);
        _volumes.Mount("data", _fs.PathOf("System/Volumes/Data"), deviceId: 2);
        _fs.Identify("Users/me", new FileIdentity(2, 77));

        var root = Assert.Single(Plan(_fs.PathOf("Users/me")));

        Assert.Equal("data", root.Volume.Id);
        Assert.False(root.IsVolumeRoot);
    }

    /// <summary>
    /// The mount point of a volume is recognised as a volume root.
    /// </summary>
    [Fact]
    public void CreateRoots_RecognisesVolumeRoots()
    {
        _volumes.Mount("system", _fs.Root, deviceId: 1);

        var root = Assert.Single(Plan(_fs.Root));

        Assert.True(root.IsVolumeRoot);
        Assert.Equal("Volume system", root.Name);
    }

    /// <summary>
    /// Paths outside the mount table are resolved through the volume service.
    /// </summary>
    [Fact]
    public void CreateRoots_QueriesTheVolumeServiceForUnknownPaths()
    {
        _volumes.QueriedVolume = FakeVolumeService.CreateVolume("share", _fs.Root);

        var root = Assert.Single(Plan(_fs.Root));

        Assert.Equal("share", root.Volume.Id);
        Assert.True(root.IsVolumeRoot);
    }

    /// <summary>
    /// Volumes mounted beneath a requested path become roots, except virtual ones, network ones (by default) and
    /// those hidden beneath a virtual file system.
    /// </summary>
    [Fact]
    public void CreateRoots_AddsVolumesMountedBeneathTheRequestedPaths()
    {
        _volumes.Mount("root", _fs.Root, deviceId: 1);
        _volumes.Mount("home", _fs.PathOf("home"), deviceId: 2);
        _volumes.Mount("dev", _fs.PathOf("dev"), VolumeKind.Virtual, deviceId: 3);
        _volumes.Mount("shm", _fs.PathOf("dev/shm"), VolumeKind.Ram, deviceId: 4);
        _volumes.Mount("tmp", _fs.PathOf("tmp"), VolumeKind.Ram, deviceId: 5);
        _volumes.Mount("nas", _fs.PathOf("nas"), VolumeKind.Network, deviceId: 6);
        _volumes.Mount("home", _fs.PathOf("bind"), deviceId: 2, isPartial: true);
        _volumes.Mount("elsewhere", Path.Combine(Path.GetTempPath(), "not-beneath"), deviceId: 7);

        var roots = Plan(_fs.Root);

        Assert.Equal(["root", "home", "tmp"], roots.Select(root => root.Volume.Id));
        Assert.All(roots.Skip(1), root => Assert.True(root.IsDiscovered && root.IsVolumeRoot));
    }

    /// <summary>
    /// Target normalisation removes duplicates and nested paths but keeps request order.
    /// </summary>
    [Fact]
    public void NormalizeTargets_RemovesDuplicatesAndNestedPaths()
    {
        var targets = ScanPlanner.NormalizeTargets(
            [_fs.PathOf("b"), _fs.PathOf("a"), _fs.PathOf("a/x"), _fs.PathOf("b") + Path.DirectorySeparatorChar],
            StringComparison.Ordinal,
            static path => path);

        Assert.Equal([_fs.PathOf("b"), _fs.PathOf("a")], targets);
    }

    /// <summary>
    /// Plans a scan of the given paths.
    /// </summary>
    /// <param name="targets">The paths.</param>
    /// <returns>The roots.</returns>
    private IReadOnlyList<RootNode> Plan(params string[] targets) =>
        ScanPlanner.CreateRoots(new ScanRequest(targets), _volumes.GetMountTable(), _fs, _volumes);

    /// <summary>
    /// Targets are canonicalised by the enumerator before they are compared.
    /// </summary>
    [Fact]
    public void NormalizeTargets_CanonicalisesTargets()
    {
        var targets = ScanPlanner.NormalizeTargets([_fs.PathOf("alias"), _fs.PathOf("real")], StringComparison.Ordinal, path => path.Replace("alias", "real", StringComparison.Ordinal));

        Assert.Equal([_fs.PathOf("real")], targets);
    }
}
