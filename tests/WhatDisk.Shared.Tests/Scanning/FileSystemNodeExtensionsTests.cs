using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;

namespace WhatDisk.Tests.Scanning;

/// <summary>
/// Tests the node navigation extension members.
/// </summary>
public sealed class FileSystemNodeExtensionsTests
{
    /// <summary>
    /// A node's ancestry runs from the node to its root.
    /// </summary>
    [Fact]
    public void SelfAndAncestors_RunsFromTheNodeToTheRoot()
    {
        var root = TestTree.Root();
        var child = TestTree.Dir(root, "child");
        var file = TestTree.File(child, "file", 1);

        Assert.Equal(["file", "child", "/test"], file.SelfAndAncestors.Select(static node => node.Name));
        Assert.Same(root, file.TopLevel);
        Assert.Same(root, root.TopLevel);
    }
}
