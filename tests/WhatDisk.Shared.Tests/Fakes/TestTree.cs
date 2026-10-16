using WhatDisk.Scanning;
using WhatDisk.Volumes;

namespace WhatDisk.Tests.Fakes;

/// <summary>
/// Builds scan trees directly, for tests that need precise control over sizes and when children are published.
/// </summary>
internal static class TestTree
{
    /// <summary>
    /// Gets a volume with a capacity of 1000 bytes.
    /// </summary>
    public static VolumeInfo Volume { get; } = new()
    {
        Id = "test",
        MountPoint = "/",
        Name = "Test Disk",
        TotalBytes = 1000,
        AvailableBytes = 400,
        FileSystem = "testfs",
    };

    /// <summary>
    /// Creates a root.
    /// </summary>
    /// <param name="path">The root path.</param>
    /// <param name="isVolumeRoot">Whether the root is a whole volume.</param>
    /// <returns>The root.</returns>
    public static RootNode Root(string path = "/test", bool isVolumeRoot = false) =>
        new(path, Volume, default, isVolumeRoot, isDiscovered: false);

    /// <summary>
    /// Creates an unread directory.
    /// </summary>
    /// <param name="parent">The parent.</param>
    /// <param name="name">The name.</param>
    /// <returns>The directory.</returns>
    public static DirectoryNode Dir(DirectoryNode parent, string name) => new(name, parent, parent.Volume, default, null);

    /// <summary>
    /// Creates a file.
    /// </summary>
    /// <param name="parent">The parent.</param>
    /// <param name="name">The name.</param>
    /// <param name="size">The size.</param>
    /// <returns>The file.</returns>
    public static FileNode File(DirectoryNode parent, string name, long size) =>
        new(name, parent, NodeKind.File, size, size, new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc), EntryAttributes.None, false);

    /// <summary>
    /// Publishes a directory's children and adds the sizes of its files to its totals and its ancestors'.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="children">The children.</param>
    /// <returns>The directory.</returns>
    public static DirectoryNode Publish(DirectoryNode directory, params FileSystemNode[] children)
    {
        directory.PublishChildren(children);
        var files = children.OfType<FileNode>().ToArray();
        directory.AddToTotals(files.Sum(static file => file.Size), files.Sum(static file => file.LogicalSize), files.Length, children.Count(static child => child is DirectoryNode));
        return directory;
    }

    /// <summary>
    /// Grows a directory (and its ancestors) as if more data had been found inside it.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="bytes">The bytes to add.</param>
    public static void Grow(DirectoryNode directory, long bytes) => directory.AddToTotals(bytes, bytes, 1, 0);
}
