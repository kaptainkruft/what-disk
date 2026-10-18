using System.Globalization;
using WhatDisk.Duplicates;
using WhatDisk.Formatting;
using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;

namespace WhatDisk.Tests.Duplicates;

/// <summary>
/// Tests the size-only estimate of duplicate space on trees built directly.
/// </summary>
public sealed class PotentialDuplicateAggregatorTests
{
    /// <summary>One mebibyte.</summary>
    private const long MiB = 1024 * 1024;

    /// <summary>
    /// Files over the minimum that share their length form groups; the space beyond the smallest copy of each group
    /// counts, on disk.
    /// </summary>
    [Fact]
    public void Estimate_CountsTheSpaceBeyondOneCopyOfEachLength()
    {
        var root = TestTree.Root();
        TestTree.Publish(
            root,
            File(root, "a", 3 * MiB, 3 * MiB),
            File(root, "b", 3 * MiB, (3 * MiB) + 4096),
            File(root, "c", 3 * MiB, 3 * MiB),
            File(root, "d", 2 * MiB, 2 * MiB),
            File(root, "e", 2 * MiB, 2 * MiB),
            File(root, "unique", 5 * MiB, 5 * MiB));

        var estimate = new PotentialDuplicateAggregator(MiB).Estimate([root], TestContext.Current.CancellationToken);

        Assert.Equal(new PotentialDuplicateEstimate((6 * MiB) + 4096 + (2 * MiB), 2, 5), estimate);
    }

    /// <summary>
    /// Files no longer than the minimum, extra hard links, cloud-only files and links are left out.
    /// </summary>
    [Fact]
    public void Estimate_LeavesOutSmallFilesHardLinksCloudFilesAndLinks()
    {
        var root = TestTree.Root();
        TestTree.Publish(
            root,
            File(root, "exactly-a", MiB, MiB),
            File(root, "exactly-b", MiB, MiB),
            File(root, "big", 2 * MiB, 2 * MiB),
            new FileNode("hardlink", root, NodeKind.File, 0, 2 * MiB, DateTime.MinValue, EntryAttributes.None, isHardLinkDuplicate: true),
            new FileNode("cloud", root, NodeKind.File, 0, 2 * MiB, DateTime.MinValue, EntryAttributes.Offline, false),
            new FileNode("link", root, NodeKind.SymbolicLink, 2 * MiB, 2 * MiB, DateTime.MinValue, EntryAttributes.None, false));

        Assert.Equal(default, new PotentialDuplicateAggregator(MiB).Estimate([root], TestContext.Current.CancellationToken));
        Assert.Equal(new PotentialDuplicateEstimate(MiB, 1, 2), new PotentialDuplicateAggregator(MiB - 1).Estimate([root], TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The estimate covers only the nodes given, so copies in two folders count only when both are included; the
    /// cached groups of a completed folder are forgotten when its contents change.
    /// </summary>
    [Fact]
    public void Estimate_CoversTheScopeAndFollowsChanges()
    {
        var root = TestTree.Root();
        var left = TestTree.Dir(root, "left");
        var right = TestTree.Dir(root, "right");
        var files = Enumerable.Range(0, PotentialDuplicateAggregator.CacheThreshold).Select(index => File(left, $"small{index}", 10, 10)).ToList();
        var copy = File(left, "copy", 4 * MiB, 4 * MiB);
        TestTree.Publish(left, [.. files, copy]);
        TestTree.Publish(right, File(right, "original", 4 * MiB, 4 * MiB));
        TestTree.Publish(root, left, right);
        var aggregator = new PotentialDuplicateAggregator(MiB);

        Assert.Equal(default, aggregator.Estimate([left], TestContext.Current.CancellationToken));
        Assert.Equal(new PotentialDuplicateEstimate(4 * MiB, 1, 2), aggregator.Estimate([root], TestContext.Current.CancellationToken));

        left.RemoveChild(copy);
        aggregator.Invalidate(left);

        Assert.Equal(default, aggregator.Estimate([root], TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The pane shows the estimate, its share of the folder for the bar, and what it counts for the info button, or
    /// says no large files share a size.
    /// </summary>
    [Fact]
    public void Pane_DescribesTheEstimate()
    {
        var pane = new PotentialDuplicatesViewModel(new ByteSizeFormatter(ByteUnitConvention.PowersOf1000, CultureInfo.InvariantCulture));
        Assert.Equal("—", pane.AmountText);

        pane.Update(new PotentialDuplicateEstimate(3_200_000_000, 12, 30), "Documents", "1 MiB", 9_600_000_000);

        Assert.Equal("3.20 GB", pane.AmountText);
        Assert.Equal(1.0 / 3, pane.Fraction, 6);
        Assert.Equal(DisplayFormat.Percent(1.0 / 3), pane.PercentText);
        Assert.Equal("Beyond one copy of each size, among files over 1 MiB in Documents: 12 groups, 30 files.", pane.DetailText);
        Assert.Contains("Find Duplicates", pane.Explanation, StringComparison.Ordinal);
        Assert.StartsWith(pane.DetailText + $" That is {pane.PercentText} of 9.60 GB in Documents.", pane.InfoText, StringComparison.Ordinal);
        Assert.EndsWith(pane.Explanation, pane.InfoText, StringComparison.Ordinal);

        pane.Update(default, "Documents", "1 MiB", 0);
        Assert.Equal(0, pane.Fraction);
        Assert.Equal("None", pane.AmountText);
        Assert.Equal("No files over 1 MiB in Documents share a size.", pane.DetailText);

        pane.Clear();
        Assert.False(pane.HasEstimate);
    }

    /// <summary>
    /// Creates a file with a length and a size on disk.
    /// </summary>
    /// <param name="parent">The folder.</param>
    /// <param name="name">The name.</param>
    /// <param name="length">The length.</param>
    /// <param name="size">The size on disk.</param>
    /// <returns>The file.</returns>
    private static FileNode File(DirectoryNode parent, string name, long length, long size) =>
        new(name, parent, NodeKind.File, size, length, DateTime.MinValue, EntryAttributes.None, false);
}
