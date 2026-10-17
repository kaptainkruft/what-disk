using WhatDisk.Charting;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;

namespace WhatDisk.Tests.Scanning;

/// <summary>
/// Tests changing a finished scan tree after files are moved, copied, deleted or rescanned.
/// </summary>
public sealed class TreeEditingTests
{
    /// <summary>The in-memory file system.</summary>
    private readonly FakeFileSystem _fs = new();

    /// <summary>The volume service.</summary>
    private readonly FakeVolumeService _volumes = new();

    /// <summary>
    /// Removing an item subtracts its size and counts from every ancestor; inserting it adds them back.
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [Fact]
    public async Task RemoveAndInsert_KeepEveryAncestorExact()
    {
        _fs.File("a/b/big.bin", 1000).File("a/b/small.bin", 10).File("a/c/x.bin", 5).File("top.bin", 1);
        var root = await ScanAsync();
        var a = root.Dir("a");
        var b = a.Dir("b");

        Assert.True(b.RemoveChild(b.Child("big.bin")));
        Assert.Equal(16, root.Size);
        Assert.Equal(3, root.FileCount);

        Assert.True(a.RemoveChild(b));
        Assert.Equal(6, root.Size);
        Assert.Equal(2, root.FileCount);
        Assert.Equal(2, root.DirectoryCount);
        Assert.Equal(5, a.Size);
        Assert.DoesNotContain(b, a.Children);
        Assert.False(a.RemoveChild(b));

        a.InsertChild(b);
        Assert.Equal(16, root.Size);
        Assert.Equal(3, root.FileCount);
        Assert.Equal(3, root.DirectoryCount);
    }

    /// <summary>
    /// Moving an item within its volume re-parents it, keeping the root total and rebuilding paths.
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [Fact]
    public async Task Reparenting_MovesTotalsBetweenFolders()
    {
        _fs.File("from/doc.txt", 300).Directory("to");
        var root = await ScanAsync();
        var from = root.Dir("from");
        var to = root.Dir("to");
        var doc = from.Child("doc.txt");

        from.RemoveChild(doc);
        doc.Parent = to;
        to.InsertChild(doc);

        Assert.Equal(300, root.Size);
        Assert.Equal(0, from.Size);
        Assert.Equal(300, to.Size);
        Assert.Equal(Path.Join(_fs.PathOf("to"), "doc.txt"), doc.FullPath);
    }

    /// <summary>
    /// A folder scanned into a finished tree adds its totals to every ancestor and completes on its own.
    /// </summary>
    /// <returns>A task that completes when both scans have finished.</returns>
    [Fact]
    public async Task StartInto_AddsAFolderToAFinishedTree()
    {
        _fs.File("existing/a.bin", 100).Directory("dest");
        var root = await ScanAsync();
        var dest = root.Dir("dest");
        _fs.File("dest/copied/one.bin", 40).File("dest/copied/deep/two.bin", 2);

        using var session = new DiskScanner(_fs, _volumes, TimeProvider.System).StartInto(dest, "copied", [root]);
        Assert.Equal(ScanStatus.Completed, await session.WaitAsync());

        var copied = Assert.IsType<DirectoryNode>(session.GraftedDirectory);
        Assert.Same(copied, dest.Child("copied"));
        Assert.True(copied.IsComplete);
        Assert.Equal(42, copied.Size);
        Assert.Equal(142, root.Size);
        Assert.Equal(3, root.FileCount);
        Assert.Equal(4, root.DirectoryCount);
        Assert.True(root.IsComplete);
    }

    /// <summary>
    /// A folder scanned into the tree links volumes mounted inside it to the existing roots instead of adding roots.
    /// </summary>
    /// <returns>A task that completes when both scans have finished.</returns>
    [Fact]
    public async Task StartInto_LinksMountedVolumesToExistingRoots()
    {
        _fs.Directory("folder/mnt", EntryAttributes.MountPoint).File("folder/mnt/inside.bin", 9).File("folder/x.bin", 1);
        _volumes.Mount("root", _fs.Root, deviceId: 1);
        _volumes.Mount("ext", _fs.PathOf("folder/mnt"), deviceId: 2);
        var session = new DiskScanner(_fs, _volumes, TimeProvider.System).Start(new ScanRequest([_fs.Root]));
        await session.WaitAsync();
        var root = session.Roots[0];
        var folder = root.Dir("folder");
        root.RemoveChild(folder);

        using var rescan = new DiskScanner(_fs, _volumes, TimeProvider.System).StartInto(root, "folder", session.Roots);
        await rescan.WaitAsync();

        var placeholder = Assert.IsType<PlaceholderNode>(rescan.GraftedDirectory!.Child("mnt"));
        Assert.Same(session.Roots[1], placeholder.LinkedRoot);
        Assert.Empty(rescan.Roots);
        Assert.Equal(1, root.Size);
    }

    /// <summary>
    /// The file-type totals of a changed folder (and its ancestors) are recomputed after invalidation.
    /// </summary>
    /// <returns>A task that completes when the scan has finished.</returns>
    [Fact]
    public async Task Invalidate_RefreshesCachedFileTypeTotals()
    {
        for (var index = 0; index <= FileTypeAggregator.CacheThreshold; index++)
        {
            _fs.File($"big/f{index}.bin", 1);
        }

        var root = await ScanAsync();
        var big = root.Dir("big");
        var aggregator = new FileTypeAggregator(new DefaultFileTypeProvider());
        var before = aggregator.Aggregate([root], TestContext.Current.CancellationToken)[".bin"];

        big.RemoveChild(big.Children[0]);
        Assert.Equal(before, aggregator.Aggregate([root], TestContext.Current.CancellationToken)[".bin"]);

        aggregator.Invalidate(big);
        Assert.Equal(before - 1, aggregator.Aggregate([root], TestContext.Current.CancellationToken)[".bin"]);
    }

    /// <summary>
    /// Scans the fake root.
    /// </summary>
    /// <returns>The root.</returns>
    private async Task<RootNode> ScanAsync()
    {
        using var session = new DiskScanner(_fs, _volumes, TimeProvider.System).Start(new ScanRequest([_fs.Root]));
        Assert.Equal(ScanStatus.Completed, await session.WaitAsync());
        return session.Roots[0];
    }
}
