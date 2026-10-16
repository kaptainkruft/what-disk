using WhatDisk.Charting;

namespace WhatDisk.Tests.Charting;

/// <summary>
/// Tests the pie chart model.
/// </summary>
public sealed class PieChartModelTests
{
    /// <summary>The container used by most tests.</summary>
    private readonly object _container = new();

    /// <summary>
    /// Nothing with a size produces an empty chart.
    /// </summary>
    [Fact]
    public void Update_ReturnsAnEmptyChartWhenNothingHasASize()
    {
        Assert.Same(PieChartSnapshot.Empty, new PieChartModel().Update(_container, [Item(".txt", 0)]));
        Assert.Same(PieChartSnapshot.Empty, new PieChartModel().Update(_container, []));
    }

    /// <summary>
    /// A few items each get a slice; the slices tile the circle.
    /// </summary>
    [Fact]
    public void Update_GivesEachOfAFewItemsASlice()
    {
        var chart = new PieChartModel().Update(_container, [Item(".jpg", 50), Item(".mp4", 30), Item(".txt", 20)]);

        Assert.Equal(100, chart.Total);
        Assert.Equal([".jpg", ".mp4", ".txt"], chart.Slices.Select(static slice => slice.Label));
        Assert.Equal([0, 1, 2], chart.Slices.Select(static slice => slice.ColorSlot));
        Assert.Equal("JPG files", chart.Slices[0].Description);
        Assert.Equal(1, chart.Slices.Sum(static slice => slice.Fraction), precision: 9);
        Assert.Equal(0, chart.Slices[0].StartAngle);
        Assert.Equal(180, chart.Slices[1].StartAngle, precision: 9);
        Assert.Equal(360, chart.Slices[^1].StartAngle + chart.Slices[^1].SweepAngle, precision: 9);
    }

    /// <summary>
    /// Beyond five items the rest are grouped into a final "other" slice that names them.
    /// </summary>
    [Fact]
    public void Update_GroupsTheRestIntoOther()
    {
        var items = Enumerable.Range(1, 8).Select(index => Item($".e{index}", index * 10)).ToArray();

        var chart = new PieChartModel().Update(_container, items);

        Assert.Equal(6, chart.Slices.Count);
        var other = chart.Slices[^1];
        Assert.True(other.IsOther);
        Assert.Null(other.Key);
        Assert.Equal(10 + 20 + 30, other.Size);
        Assert.Equal("3 other types", other.Label);
        Assert.Equal(".e3, .e2, .e1", other.Description);
        Assert.Equal([".e8", ".e7", ".e6", ".e5", ".e4", other.Label], chart.Legend.Select(static slice => slice.Label));
    }

    /// <summary>
    /// The single-pass selection picks the same largest items as a full sort, breaking ties by label.
    /// </summary>
    [Fact]
    public void Update_SelectsTheLargestItemsLikeAFullSort()
    {
        var random = new Random(11);
        var items = Enumerable.Range(0, 5000).Select(index => Item($".x{index:D5}", random.Next(0, 300))).ToArray();

        var chart = new PieChartModel().Update(_container, items);

        var expected = items.Where(static item => item.Size > 0)
            .OrderByDescending(static item => item.Size)
            .ThenBy(static item => item.Label, StringComparer.CurrentCultureIgnoreCase)
            .Take(PieChartModel.MaxNamedSlices)
            .Select(static item => item.Label);
        Assert.Equal(expected, chart.Legend.Where(static slice => !slice.IsOther).Select(static slice => slice.Label));
        Assert.Equal(items.Sum(static item => item.Size), chart.Total);
    }

    /// <summary>
    /// Items keep their colours while their ranking changes.
    /// </summary>
    [Fact]
    public void Update_KeepsColoursWithItemsWhenTheyReorder()
    {
        var model = new PieChartModel();
        var first = model.Update(_container, [Item(".a", 100), Item(".b", 50)]);

        var second = model.Update(_container, [Item(".a", 100), Item(".b", 250)]);

        Assert.Equal(SlotOf(first, ".a"), SlotOf(second, ".a"));
        Assert.Equal(SlotOf(first, ".b"), SlotOf(second, ".b"));
        Assert.Equal(".b", second.Legend[0].Label);
        Assert.Equal(".a", second.Slices[0].Label);
    }

    /// <summary>
    /// When an item drops out of the named slices, the item replacing it takes over its colour.
    /// </summary>
    [Fact]
    public void Update_ReusesTheColourOfAnItemThatDroppedOut()
    {
        var model = new PieChartModel();
        var before = model.Update(_container, [.. Enumerable.Range(0, 5).Select(index => Item($".d{index}", 100 - index)), Item(".d5", 0)]);
        var droppedSlot = SlotOf(before, ".d4");

        var after = model.Update(_container, [.. Enumerable.Range(0, 5).Select(index => Item($".d{index}", 100 - index)), Item(".d5", 500)]);

        Assert.Equal(droppedSlot, SlotOf(after, ".d5"));
        Assert.All(Enumerable.Range(0, 4), index => Assert.Equal(SlotOf(before, $".d{index}"), SlotOf(after, $".d{index}")));
    }

    /// <summary>
    /// Changing the container starts colour assignment afresh.
    /// </summary>
    [Fact]
    public void Update_ResetsColoursWhenTheContainerChanges()
    {
        var model = new PieChartModel();
        model.Update(_container, [Item(".a", 10)]);

        var chart = model.Update(new object(), [Item(".b", 20), Item(".a", 10)]);

        Assert.Equal(0, SlotOf(chart, ".b"));
        Assert.Equal(1, SlotOf(chart, ".a"));
    }

    /// <summary>
    /// Creates a chart item for a file type.
    /// </summary>
    /// <param name="type">The type key.</param>
    /// <param name="size">The size.</param>
    /// <returns>The item.</returns>
    private static ChartItem Item(string type, long size) => new(type, type, $"{type[1..].ToUpperInvariant()} files", size);

    /// <summary>
    /// Gets the colour slot of an item's slice.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <param name="key">The item's key.</param>
    /// <returns>The slot.</returns>
    private static int SlotOf(PieChartSnapshot chart, string key) =>
        chart.Slices.Single(slice => slice.Key == key).ColorSlot;
}
