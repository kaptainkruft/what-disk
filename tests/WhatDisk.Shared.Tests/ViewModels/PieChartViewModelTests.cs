using System.Globalization;
using WhatDisk.Formatting;
using WhatDisk.Tests.Fakes;
using WhatDisk.ViewModels;

namespace WhatDisk.Tests.ViewModels;

/// <summary>
/// Tests the chart pane view model.
/// </summary>
public sealed class PieChartViewModelTests
{
    /// <summary>The view model under test.</summary>
    private readonly PieChartViewModel _chart = new(new ByteSizeFormatter(ByteUnitConvention.PowersOf1000, CultureInfo.InvariantCulture));

    /// <summary>
    /// The chart shows the container's name, total and one legend entry per slice.
    /// </summary>
    [Fact]
    public void Update_PopulatesTitleTotalSlicesAndLegend()
    {
        var root = TestTree.Root();
        var big = TestTree.File(root, "big", 3000);
        var small = TestTree.File(root, "small", 1000);

        _chart.Update("Documents", root, [small, big], highlighted: small);

        Assert.Equal("Documents", _chart.Title);
        Assert.Equal("4.00 KB", _chart.TotalText);
        Assert.True(_chart.HasData);
        Assert.Equal(2, _chart.Slices.Count);
        Assert.Equal("3.00 KB", _chart.Slices[0].SizeText);
        Assert.Equal(["big", "small"], _chart.Legend.Select(static entry => entry.Label));
        Assert.Equal("75.0%", _chart.Legend[0].PercentText);
        Assert.True(_chart.Legend[1].IsHighlighted);
        Assert.Same(small, _chart.HighlightedNode);
    }

    /// <summary>
    /// Legend entries are updated in place, and unchanged slices are not replaced.
    /// </summary>
    [Fact]
    public void Update_ReusesLegendEntriesAndUnchangedSlices()
    {
        var root = TestTree.Root();
        var a = TestTree.Dir(root, "a");
        TestTree.Grow(a, 100);
        _chart.Update("root", root, [a], null);
        var entry = _chart.Legend[0];
        var slices = _chart.Slices;

        _chart.Update("root", root, [a], null);
        Assert.Same(slices, _chart.Slices);

        TestTree.Grow(a, 900);
        _chart.Update("root", root, [a], null);
        Assert.Same(entry, _chart.Legend[0]);
        Assert.Equal("1.00 KB", entry.SizeText);
    }

    /// <summary>
    /// Clearing empties the chart.
    /// </summary>
    [Fact]
    public void Clear_EmptiesTheChart()
    {
        var root = TestTree.Root();
        _chart.Update("root", root, [TestTree.File(root, "x", 5)], null);

        _chart.Clear();

        Assert.False(_chart.HasData);
        Assert.Empty(_chart.Legend);
        Assert.Empty(_chart.Slices);
        Assert.Equal(string.Empty, _chart.TotalText);
    }
}
