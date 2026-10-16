using WhatDisk.Charting;
using WhatDisk.Scanning;
using WhatDisk.Tests.Fakes;

namespace WhatDisk.Tests.Charting;

/// <summary>
/// Tests the pie chart model.
/// </summary>
public sealed class PieChartModelTests
{
    /// <summary>
    /// Nothing with a size produces an empty chart.
    /// </summary>
    [Fact]
    public void Update_ReturnsAnEmptyChartWhenNothingHasASize()
    {
        var root = TestTree.Root();
        var empty = TestTree.File(root, "empty", 0);

        Assert.Same(PieChartSnapshot.Empty, new PieChartModel().Update(root, [empty]));
        Assert.Same(PieChartSnapshot.Empty, new PieChartModel().Update(root, []));
    }

    /// <summary>
    /// A few items each get a slice; the slices tile the circle.
    /// </summary>
    [Fact]
    public void Update_GivesEachOfAFewItemsASlice()
    {
        var root = TestTree.Root();
        FileSystemNode[] items = [TestTree.File(root, "a", 50), TestTree.File(root, "b", 30), TestTree.File(root, "c", 20)];

        var chart = new PieChartModel().Update(root, items);

        Assert.Equal(100, chart.Total);
        Assert.Equal(["a", "b", "c"], chart.Slices.Select(static slice => slice.Label));
        Assert.Equal([0, 1, 2], chart.Slices.Select(static slice => slice.ColorSlot));
        Assert.Equal(1, chart.Slices.Sum(static slice => slice.Fraction), precision: 9);
        Assert.Equal(0, chart.Slices[0].StartAngle);
        Assert.Equal(180, chart.Slices[1].StartAngle, precision: 9);
        Assert.Equal(360, chart.Slices[^1].StartAngle + chart.Slices[^1].SweepAngle, precision: 9);
    }

    /// <summary>
    /// Beyond five items the rest are grouped into a final "Other" slice.
    /// </summary>
    [Fact]
    public void Update_GroupsTheRestIntoOther()
    {
        var root = TestTree.Root();
        var items = Enumerable.Range(1, 8).Select(index => (FileSystemNode)TestTree.File(root, $"f{index}", index * 10)).ToArray();

        var chart = new PieChartModel().Update(root, items);

        Assert.Equal(6, chart.Slices.Count);
        var other = chart.Slices[^1];
        Assert.True(other.IsOther);
        Assert.Null(other.Node);
        Assert.Equal(10 + 20 + 30, other.Size);
        Assert.Equal("3 other items", other.Label);
        Assert.Equal(["f8", "f7", "f6", "f5", "f4", other.Label], chart.Legend.Select(static slice => slice.Label));
    }

    /// <summary>
    /// The single-pass selection picks the same largest items as a full sort, breaking ties by name.
    /// </summary>
    [Fact]
    public void Update_SelectsTheLargestItemsLikeAFullSort()
    {
        var root = TestTree.Root();
        var random = new Random(11);
        var items = Enumerable.Range(0, 5000)
            .Select(index => (FileSystemNode)TestTree.File(root, $"f{index:D5}", random.Next(0, 300)))
            .ToArray();

        var chart = new PieChartModel().Update(root, items);

        var expected = items.Where(static item => item.Size > 0)
            .OrderByDescending(static item => item.Size)
            .ThenBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(PieChartModel.MaxNamedSlices)
            .Select(static item => item.Name);
        Assert.Equal(expected, chart.Legend.Where(static slice => !slice.IsOther).Select(static slice => slice.Label));
        Assert.Equal(items.Sum(static item => item.Size), chart.Total);
    }

    /// <summary>
    /// Items keep their colours while their ranking changes.
    /// </summary>
    [Fact]
    public void Update_KeepsColoursWithItemsWhenTheyReorder()
    {
        var root = TestTree.Root();
        var a = TestTree.Dir(root, "a");
        var b = TestTree.Dir(root, "b");
        TestTree.Grow(a, 100);
        TestTree.Grow(b, 50);
        var model = new PieChartModel();
        var first = model.Update(root, [a, b]);

        TestTree.Grow(b, 200);
        var second = model.Update(root, [a, b]);

        Assert.Equal(SlotOf(first, a), SlotOf(second, a));
        Assert.Equal(SlotOf(first, b), SlotOf(second, b));
        Assert.Equal("b", second.Legend[0].Label);
        Assert.Equal("a", second.Slices[0].Label);
    }

    /// <summary>
    /// When an item drops out of the named slices, the item replacing it takes over its colour.
    /// </summary>
    [Fact]
    public void Update_ReusesTheColourOfAnItemThatDroppedOut()
    {
        var root = TestTree.Root();
        var directories = Enumerable.Range(0, 6).Select(index => TestTree.Dir(root, $"d{index}")).ToArray();
        for (var index = 0; index < 5; index++)
        {
            TestTree.Grow(directories[index], 100 - index);
        }

        var model = new PieChartModel();
        var before = model.Update(root, directories);
        var droppedSlot = SlotOf(before, directories[4]);

        TestTree.Grow(directories[5], 500);
        var after = model.Update(root, directories);

        Assert.Equal(droppedSlot, SlotOf(after, directories[5]));
        Assert.All(directories.Take(4), directory => Assert.Equal(SlotOf(before, directory), SlotOf(after, directory)));
    }

    /// <summary>
    /// Changing the container starts colour assignment afresh.
    /// </summary>
    [Fact]
    public void Update_ResetsColoursWhenTheContainerChanges()
    {
        var root = TestTree.Root();
        var a = TestTree.File(root, "a", 10);
        var b = TestTree.File(root, "b", 20);
        var model = new PieChartModel();
        model.Update(root, [a]);

        var chart = model.Update(new object(), [b, a]);

        Assert.Equal(0, SlotOf(chart, b));
        Assert.Equal(1, SlotOf(chart, a));
    }

    /// <summary>
    /// Gets the colour slot of an item's slice.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <param name="node">The item.</param>
    /// <returns>The slot.</returns>
    private static int SlotOf(PieChartSnapshot chart, FileSystemNode node) =>
        chart.Slices.Single(slice => ReferenceEquals(slice.Node, node)).ColorSlot;
}
