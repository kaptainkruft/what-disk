using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;
using WhatDisk.Volumes;

namespace WhatDisk.Tests.Scanning;

/// <summary>
/// Tests the scan engine end to end against an in-memory file system.
/// </summary>
public sealed class DiskScannerTests
{
    /// <summary>The in-memory file system under test.</summary>
    private readonly FakeFileSystem _fs = new();

    /// <summary>The volume service under test.</summary>
    private readonly FakeVolumeService _volumes = new();

    /// <summary>
    /// Totals of files, directories and bytes are aggregated into every ancestor.
    /// </summary>
    [Fact]
    public async Task Scan_AggregatesTotalsIntoEveryAncestor()
    {
        _fs.File("a/1.bin", 100).File("a/2.bin", 50).File("a/b/3.bin", 25).File("top.txt", 10).Directory("empty");

        using var session = Start();
        Assert.Equal(ScanStatus.Completed, await session.WaitAsync());

        var root = Assert.Single(session.Roots);
        Assert.Equal(185, root.Size);
        Assert.Equal(185, root.LogicalSize);
        Assert.Equal(4, root.FileCount);
        Assert.Equal(3, root.DirectoryCount);
        Assert.True(root.IsComplete);

        var a = root.Dir("a");
        Assert.Equal(175, a.Size);
        Assert.Equal(3, a.FileCount);
        Assert.Equal(1, a.DirectoryCount);
        Assert.Equal(25, a.Dir("b").Size);
        Assert.True(root.Dir("empty").IsComplete);

        Assert.Equal(4, session.Statistics.FileCount);
        Assert.Equal(4, session.Statistics.DirectoryCount);
        Assert.Equal(185, session.Statistics.ByteCount);
        Assert.Equal(0, session.Statistics.ErrorCount);
        Assert.NotNull(session.CompletedAt);
    }

    /// <summary>
    /// The allocated size is counted when the enumerator reports it; the logical size is kept separately.
    /// </summary>
    [Fact]
    public async Task Scan_PrefersAllocatedSizeOverLogicalSize()
    {
        _fs.File("small.txt", logicalSize: 10, allocatedSize: 4096).File("sparse.img", logicalSize: 1_000_000, allocatedSize: 8192);

        using var session = Start();
        await session.WaitAsync();

        var root = session.Roots[0];
        Assert.Equal(4096 + 8192, root.Size);
        Assert.Equal(1_000_010, root.LogicalSize);
    }

    /// <summary>
    /// A directory that cannot be read is marked with an error and the rest of the scan continues.
    /// </summary>
    [Fact]
    public async Task Scan_RecordsAccessDeniedAndContinues()
    {
        _fs.File("ok/x.bin", 5).Fail("locked", new UnauthorizedAccessException("Access denied"));

        using var session = Start();
        Assert.Equal(ScanStatus.Completed, await session.WaitAsync());

        var root = session.Roots[0];
        var locked = root.Dir("locked");
        Assert.Equal(ScanErrorKind.AccessDenied, locked.Error?.Kind);
        Assert.True(locked.IsComplete);
        Assert.Equal(5, root.Size);
        Assert.Equal(1, session.Statistics.ErrorCount);
        var record = Assert.Single(session.Errors);
        Assert.Equal(_fs.PathOf("locked"), record.Path);
    }

    /// <summary>
    /// Entries read before a failure are kept.
    /// </summary>
    [Fact]
    public async Task Scan_KeepsEntriesReadBeforeAFailure()
    {
        _fs.File("partial/a.bin", 1).File("partial/b.bin", 2).Fail("partial", new IOException("Device error"), entriesBeforeFailure: 1);

        using var session = Start();
        await session.WaitAsync();

        var partial = session.Roots[0].Dir("partial");
        Assert.Single(partial.Children);
        Assert.Equal(ScanErrorKind.IO, partial.Error?.Kind);
        Assert.Equal(1, partial.Size);
    }

    /// <summary>
    /// Symbolic links are listed but never followed.
    /// </summary>
    [Fact]
    public async Task Scan_ListsButDoesNotFollowSymbolicLinks()
    {
        _fs.File("link-to-elsewhere", 12, type: EntryType.SymbolicLink).File("real.bin", 3);

        using var session = Start();
        await session.WaitAsync();

        var root = session.Roots[0];
        var link = Assert.IsType<FileNode>(root.Child("link-to-elsewhere"));
        Assert.Equal(NodeKind.SymbolicLink, link.Kind);
        Assert.DoesNotContain(_fs.PathOf("link-to-elsewhere"), _fs.ReadLog);
    }

    /// <summary>
    /// A file with several hard links is counted once.
    /// </summary>
    [Fact]
    public async Task Scan_CountsHardLinkedDataOnce()
    {
        var identity = new FileIdentity(1, 42);
        _fs.File("one/data.bin", 1000, identity: identity, linkCount: 2)
            .File("two/data.bin", 1000, identity: identity, linkCount: 2)
            .File("other.bin", 1, identity: new FileIdentity(1, 43), linkCount: 1);

        using var session = Start();
        await session.WaitAsync();

        var root = session.Roots[0];
        Assert.Equal(1001, root.Size);
        var copies = new[] { root.Dir("one").Child("data.bin"), root.Dir("two").Child("data.bin") }.Cast<FileNode>().ToArray();
        Assert.Single(copies, copy => copy.IsHardLinkDuplicate);
        Assert.Single(copies, copy => copy.Size == 1000);
    }

    /// <summary>
    /// Cancelling stops the scan and leaves partial results available.
    /// </summary>
    [Fact]
    public async Task Cancel_StopsTheScanAndKeepsPartialResults()
    {
        using var started = new ManualResetEventSlim();
        _fs.File("fast/a.bin", 10).Directory("slow");
        _fs.OnRead = (path, token) =>
        {
            if (path.EndsWith("slow", StringComparison.Ordinal))
            {
                started.Set();
                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
            }
        };

        using var session = Start(parallelism: 2);
        Assert.True(started.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        session.Cancel();

        Assert.Equal(ScanStatus.Cancelled, await session.WaitAsync());
        Assert.False(session.Roots[0].IsComplete);
        Assert.False(session.IsRunning);
    }

    /// <summary>
    /// A volume mounted beneath the scanned path is scanned as its own top-level root and shown as a placeholder in place.
    /// </summary>
    [Fact]
    public async Task Scan_ListsMountedVolumesAsSeparateRoots()
    {
        _fs.Directory("mnt", EntryAttributes.MountPoint).File("mnt/data.bin", 1000).File("local.bin", 10);
        _volumes.Mount("system", _fs.Root, deviceId: 1);
        var external = _volumes.Mount("external", _fs.PathOf("mnt"), VolumeKind.Removable, deviceId: 2);

        using var session = Start();
        await session.WaitAsync();

        Assert.Equal(2, session.Roots.Count);
        var root = session.Roots[0];
        var mounted = session.Roots[1];
        Assert.True(mounted.IsVolumeRoot);
        Assert.True(mounted.IsDiscovered);
        Assert.Equal(external, mounted.Volume);
        Assert.Equal(10, root.Size);
        Assert.Equal(1000, mounted.Size);

        var placeholder = Assert.IsType<PlaceholderNode>(root.Child("mnt"));
        Assert.Equal(SkipReason.SeparateVolume, placeholder.Reason);
        Assert.Same(mounted, placeholder.LinkedRoot);
    }

    /// <summary>
    /// A firmlink into a volume that is scanned as its own root is not counted twice.
    /// </summary>
    [Fact]
    public async Task Scan_SkipsFirmlinksIntoVolumesThatAreScannedSeparately()
    {
        _fs.Directory("Users", EntryAttributes.Firmlink).File("Users/u.bin", 100)
            .Directory("System/Volumes/Data", EntryAttributes.MountPoint)
            .File("System/Volumes/Data/Users/u.bin", 100)
            .File("System/kernel", 7)
            .Identify("Users", new FileIdentity(2, 500));
        _volumes.Mount("system", _fs.Root, deviceId: 1);
        _volumes.Mount("data", _fs.PathOf("System/Volumes/Data"), deviceId: 2);

        using var session = Start();
        await session.WaitAsync();

        var system = session.Roots[0];
        var data = Assert.Single(session.Roots, root => root.Volume.Id == "data");
        Assert.Equal(7, system.Size);
        Assert.Equal(100, data.Size);
        var users = Assert.IsType<PlaceholderNode>(system.Child("Users"));
        Assert.Same(data, users.LinkedRoot);
    }

    /// <summary>
    /// A firmlink into a volume that is not otherwise scanned becomes a discovered root.
    /// </summary>
    [Fact]
    public async Task Scan_TurnsFirmlinksIntoUnscannedVolumesIntoDiscoveredRoots()
    {
        _fs.Directory("usr/local", EntryAttributes.Firmlink).File("usr/local/tool", 7).File("usr/bin/ls", 3)
            .Identify("usr/local", new FileIdentity(2, 9));
        _volumes.Mount("system", _fs.Root, deviceId: 1);
        _volumes.Mount("data", _fs.PathOf("data"), deviceId: 2);

        using var session = StartScan(_fs.PathOf("usr"));
        await session.WaitAsync();

        Assert.Equal(2, session.Roots.Count);
        var usr = session.Roots[0];
        var discovered = session.Roots[1];
        Assert.False(discovered.IsVolumeRoot);
        Assert.True(discovered.IsDiscovered);
        Assert.Equal("data", discovered.Volume.Id);
        Assert.Equal(_fs.PathOf("usr/local"), discovered.RootPath);
        Assert.Equal(7, discovered.Size);
        Assert.Equal(3, usr.Size);
        Assert.Same(discovered, Assert.IsType<PlaceholderNode>(usr.Child("local")).LinkedRoot);
    }

    /// <summary>
    /// Virtual file systems are listed as placeholders and never read.
    /// </summary>
    [Fact]
    public async Task Scan_SkipsVirtualFileSystems()
    {
        _fs.Directory("proc", EntryAttributes.MountPoint).File("proc/kcore", 1L << 40).File("real.bin", 1);
        _volumes.Mount("root", _fs.Root, deviceId: 1);
        _volumes.Mount("proc", _fs.PathOf("proc"), VolumeKind.Virtual, deviceId: 3);

        using var session = Start();
        await session.WaitAsync();

        Assert.Single(session.Roots);
        Assert.Equal(1, session.Roots[0].Size);
        Assert.Equal(SkipReason.VirtualFileSystem, Assert.IsType<PlaceholderNode>(session.Roots[0].Child("proc")).Reason);
        Assert.DoesNotContain(_fs.PathOf("proc"), _fs.ReadLog);
        Assert.Equal(1, session.Statistics.SkippedCount);
    }

    /// <summary>
    /// Network volumes mounted beneath the scanned path are skipped unless requested.
    /// </summary>
    /// <param name="includeNetwork">Whether the scan includes network volumes.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scan_IncludesNetworkVolumesOnlyWhenRequested(bool includeNetwork)
    {
        _fs.Directory("share", EntryAttributes.MountPoint).File("share/remote.bin", 50);
        _volumes.Mount("root", _fs.Root, deviceId: 1);
        _volumes.Mount("nas", _fs.PathOf("share"), VolumeKind.Network, deviceId: 4);

        using var session = Start(options: new ScanOptions { IncludeNetworkVolumes = includeNetwork, MaxDegreeOfParallelism = 2 });
        await session.WaitAsync();

        Assert.Equal(includeNetwork ? 2 : 1, session.Roots.Count);
        var placeholder = Assert.IsType<PlaceholderNode>(session.Roots[0].Child("share"));
        Assert.Equal(includeNetwork ? SkipReason.SeparateVolume : SkipReason.NetworkVolume, placeholder.Reason);
    }

    /// <summary>
    /// A bind mount of data on a volume that is scanned in full is not counted twice.
    /// </summary>
    [Fact]
    public async Task Scan_SkipsBindMountsOfAlreadyScannedVolumes()
    {
        _fs.File("var/data/big.bin", 500).File("srv/data/big.bin", 500);
        _volumes.Mount("root", _fs.Root, deviceId: 1);
        _volumes.Mount("root", _fs.PathOf("srv/data"), deviceId: 1, isPartial: true);

        using var session = Start();
        await session.WaitAsync();

        var root = Assert.Single(session.Roots);
        Assert.Equal(500, root.Size);
        Assert.Equal(SkipReason.CountedElsewhere, Assert.IsType<PlaceholderNode>(root.Dir("srv").Child("data")).Reason);
    }

    /// <summary>
    /// A change of device that the mount table does not explain (such as a btrfs sub-volume) is traversed normally.
    /// </summary>
    [Fact]
    public async Task Scan_TraversesDeviceChangesUnknownToTheMountTable()
    {
        _fs.Identify(_fs.Root, new FileIdentity(1, 2)).Directory("subvolume", identity: new FileIdentity(99, 5)).File("subvolume/x.bin", 9);

        using var session = Start();
        await session.WaitAsync();

        Assert.Equal(9, session.Roots[0].Size);
        Assert.IsType<DirectoryNode>(session.Roots[0].Child("subvolume"));
    }

    /// <summary>
    /// Each requested path becomes a root, and paths nested inside another requested path are ignored.
    /// </summary>
    [Fact]
    public async Task Scan_CreatesOneRootPerDistinctTarget()
    {
        _fs.File("a/x.bin", 1).File("b/y.bin", 2);

        using var session = StartScan(_fs.PathOf("a"), _fs.PathOf("b"), _fs.PathOf("a/nested"), _fs.PathOf("a") + Path.DirectorySeparatorChar);
        await session.WaitAsync();

        Assert.Equal([_fs.PathOf("a"), _fs.PathOf("b")], session.Roots.Select(root => root.RootPath));
        Assert.Equal(1, session.Roots[0].Size);
        Assert.Equal(2, session.Roots[1].Size);
    }

    /// <summary>
    /// A path that does not exist produces a root with an error rather than failing the scan.
    /// </summary>
    [Fact]
    public async Task Scan_ReportsMissingTargetsAsErrors()
    {
        using var session = StartScan(_fs.PathOf("missing"));

        Assert.Equal(ScanStatus.Completed, await session.WaitAsync());
        Assert.Equal(ScanErrorKind.NotFound, session.Roots[0].Error?.Kind);
    }

    /// <summary>
    /// Large, wide trees produce exact totals under heavy parallelism.
    /// </summary>
    [Fact]
    public async Task Scan_ProducesExactTotalsUnderParallelism()
    {
        var random = new Random(1234);
        long expectedBytes = 0, expectedFiles = 0, expectedDirectories = 0;
        for (var top = 0; top < 40; top++)
        {
            for (var middle = 0; middle < 25; middle++)
            {
                var directory = $"d{top}/m{middle}";
                expectedDirectories += middle == 0 ? 2 : 1;
                for (var file = 0; file < random.Next(0, 12); file++)
                {
                    var size = random.Next(0, 100_000);
                    _fs.File($"{directory}/f{file}.bin", size);
                    expectedBytes += size;
                    expectedFiles++;
                }

                _fs.Directory(directory);
            }
        }

        using var session = Start(parallelism: 16);
        Assert.Equal(ScanStatus.Completed, await session.WaitAsync());

        var root = session.Roots[0];
        Assert.Equal(expectedBytes, root.Size);
        Assert.Equal(expectedFiles, root.FileCount);
        Assert.Equal(expectedDirectories, root.DirectoryCount);
        Assert.True(root.IsComplete);
        Assert.Equal(expectedBytes, root.Children.Sum(child => child.Size));
    }

    /// <summary>
    /// Starts a scan of the given targets.
    /// </summary>
    /// <param name="targets">The paths to scan.</param>
    /// <returns>The running session.</returns>
    private ScanSession StartScan(params string[] targets) => StartCore(targets, null);

    /// <summary>
    /// Starts a scan of the fake root with the given parallelism or options.
    /// </summary>
    /// <param name="parallelism">The number of workers.</param>
    /// <param name="options">The options; overrides <paramref name="parallelism"/> when supplied.</param>
    /// <returns>The running session.</returns>
    private ScanSession Start(int parallelism = 4, ScanOptions? options = null) =>
        StartCore([_fs.Root], options ?? new ScanOptions { MaxDegreeOfParallelism = parallelism });

    /// <summary>
    /// Starts a scan.
    /// </summary>
    /// <param name="targets">The paths to scan.</param>
    /// <param name="options">The options.</param>
    /// <returns>The running session.</returns>
    private ScanSession StartCore(IEnumerable<string> targets, ScanOptions? options) =>
        new DiskScanner(_fs, _volumes, TimeProvider.System).Start(new ScanRequest(targets, options ?? new ScanOptions { MaxDegreeOfParallelism = 4 }));
}
