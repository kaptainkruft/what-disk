using WhatDisk.Scanning;
using WhatDisk.Volumes;

namespace WhatDisk.Tests.Scanning;

/// <summary>
/// Tests the scan tree node types.
/// </summary>
public sealed class NodeTests
{
    /// <summary>A volume used by every node in these tests.</summary>
    private static readonly VolumeInfo Volume = VolumeInfo.CreateUnknown("/test");

    /// <summary>
    /// Totals added to a directory are added to every ancestor.
    /// </summary>
    [Fact]
    public void AddToTotals_PropagatesToEveryAncestor()
    {
        var root = CreateRoot();
        var child = new DirectoryNode("child", root, Volume, default, null);
        var grandchild = new DirectoryNode("grandchild", child, Volume, default, null);

        grandchild.AddToTotals(size: 10, logicalSize: 8, files: 2, directories: 1);

        Assert.All(new DirectoryNode[] { root, child, grandchild }, node =>
        {
            Assert.Equal(10, node.Size);
            Assert.Equal(8, node.LogicalSize);
            Assert.Equal(2, node.FileCount);
            Assert.Equal(1, node.DirectoryCount);
        });
    }

    /// <summary>
    /// A directory completes only when it and all of its pending children have completed, and completion cascades up.
    /// </summary>
    [Fact]
    public void CompleteOne_CompletesDirectoriesBottomUp()
    {
        var root = CreateRoot();
        var child = new DirectoryNode("child", root, Volume, default, null);
        root.PublishChildren([child]);
        root.AddPendingChildren(1);
        root.CompleteOne();

        Assert.True(root.IsEnumerated);
        Assert.False(root.IsComplete);

        child.PublishChildren([]);
        child.CompleteOne();

        Assert.True(child.IsComplete);
        Assert.True(root.IsComplete);
    }

    /// <summary>
    /// Full paths are rebuilt from the root path and the names of the ancestors.
    /// </summary>
    [Fact]
    public void FullPath_JoinsTheRootPathAndNames()
    {
        var root = CreateRoot();
        var child = new DirectoryNode("child", root, Volume, default, null);
        var file = new FileNode("file.txt", child, NodeKind.File, 1, 1, DateTime.MinValue, EntryAttributes.None, false);

        Assert.Equal(Path.Join(root.RootPath, "child", "file.txt"), file.FullPath);
        Assert.Equal(2, file.Depth);
        Assert.Null(file.LastWriteTimeUtc);
        Assert.Same(Volume, file.Volume);
    }

    /// <summary>
    /// A filesystem root path that already ends with a separator is not doubled.
    /// </summary>
    [Fact]
    public void FullPath_DoesNotDoubleTheSeparatorAfterAFileSystemRoot()
    {
        var systemRoot = Path.GetPathRoot(Path.GetTempPath())!;
        var root = new RootNode(systemRoot, Volume, default, isVolumeRoot: true, isDiscovered: false);
        var child = new DirectoryNode("child", root, Volume, default, null);

        Assert.Equal(systemRoot + "child", child.FullPath);
    }

    /// <summary>
    /// A volume root is named after its volume; any other root after its path.
    /// </summary>
    [Fact]
    public void RootNode_UsesTheVolumeNameOnlyForVolumeRoots()
    {
        var volume = new VolumeInfo { Id = "v", MountPoint = "/Volumes/Backup", Name = "Backup" };

        Assert.Equal("Backup", new RootNode("/Volumes/Backup", volume, default, isVolumeRoot: true, isDiscovered: false).Name);
        Assert.Equal("/Volumes/Backup/x", new RootNode("/Volumes/Backup/x", volume, default, isVolumeRoot: false, isDiscovered: false).Name);
        Assert.Equal(NodeKind.Volume, new RootNode("/Volumes/Backup", volume, default, isVolumeRoot: true, isDiscovered: false).Kind);
    }

    /// <summary>
    /// Placeholders count nothing.
    /// </summary>
    [Fact]
    public void PlaceholderNode_CountsNothing()
    {
        var placeholder = new PlaceholderNode("mnt", CreateRoot(), SkipReason.VirtualFileSystem, null, null);

        Assert.Equal(0, placeholder.Size + placeholder.LogicalSize + placeholder.FileCount + placeholder.DirectoryCount);
        Assert.Equal(NodeKind.MountPoint, placeholder.Kind);
        Assert.False(placeholder.IsContainer);
    }

    /// <summary>
    /// The effective size of an entry falls back to the logical size when the allocation is unknown.
    /// </summary>
    /// <param name="logical">The logical size.</param>
    /// <param name="allocated">The allocated size.</param>
    /// <param name="expected">The expected effective size.</param>
    [Theory]
    [InlineData(100, -1, 100)]
    [InlineData(100, 4096, 4096)]
    [InlineData(-5, -1, 0)]
    public void EffectiveSize_FallsBackToLogicalSize(long logical, long allocated, long expected) =>
        Assert.Equal(expected, new FileSystemEntryInfo("x", EntryType.File, logical, allocated, DateTime.MinValue).EffectiveSize);

    /// <summary>
    /// Creates a root node for tests.
    /// </summary>
    /// <returns>The root.</returns>
    private static RootNode CreateRoot() =>
        new(Path.Combine(Path.GetTempPath(), "root"), Volume, default, isVolumeRoot: false, isDiscovered: false);
}
