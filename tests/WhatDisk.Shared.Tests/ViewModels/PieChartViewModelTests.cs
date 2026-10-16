using System.Globalization;
using WhatDisk.Charting;
using WhatDisk.Formatting;
using WhatDisk.ViewModels;

namespace WhatDisk.Tests.ViewModels;

/// <summary>
/// Tests the chart pane view model.
/// </summary>
public sealed class PieChartViewModelTests
{
    /// <summary>The view model under test.</summary>
    private readonly PieChartViewModel _chart = new(new ByteSizeFormatter(ByteUnitConvention.PowersOf1000, CultureInfo.InvariantCulture));

    /// <summary>The container being charted.</summary>
    private readonly object _container = new();

    /// <summary>
    /// The chart shows its title, total and one legend entry per slice, with descriptions and the highlight.
    /// </summary>
    [Fact]
    public void Update_PopulatesTitleTotalSlicesAndLegend()
    {
        _chart.Update("File types in Documents", _container, [Item(".txt", 1000), Item(".pdf", 3000)], highlightedKey: ".txt");

        Assert.Equal("File types in Documents", _chart.Title);
        Assert.Equal("4.00 KB", _chart.TotalText);
        Assert.True(_chart.HasData);
        Assert.Equal(2, _chart.Slices.Count);
        Assert.Equal("3.00 KB", _chart.Slices[0].SizeText);
        Assert.Equal([".pdf", ".txt"], _chart.Legend.Select(static entry => entry.Label));
        Assert.Equal("PDF document", _chart.Legend[0].Description);
        Assert.Equal("75.0%", _chart.Legend[0].PercentText);
        Assert.True(_chart.Legend[1].IsHighlighted);
        Assert.Equal(".txt", _chart.HighlightedKey);
    }

    /// <summary>
    /// The highlight can move without recomputing the chart.
    /// </summary>
    [Fact]
    public void Highlight_MovesTheEmphasis()
    {
        _chart.Update("t", _container, [Item(".txt", 1000), Item(".pdf", 3000)], highlightedKey: null);

        _chart.Highlight(".pdf");

        Assert.Equal([true, false], _chart.Legend.Select(static entry => entry.IsHighlighted));
        Assert.Equal(".pdf", _chart.HighlightedKey);
    }

    /// <summary>
    /// Legend entries are updated in place, and unchanged slices are not replaced.
    /// </summary>
    [Fact]
    public void Update_ReusesLegendEntriesAndUnchangedSlices()
    {
        _chart.Update("t", _container, [Item(".a", 100)], null);
        var entry = _chart.Legend[0];
        var slices = _chart.Slices;

        _chart.Update("t", _container, [Item(".a", 100)], null);
        Assert.Same(slices, _chart.Slices);

        _chart.Update("t", _container, [Item(".a", 1000)], null);
        Assert.Same(entry, _chart.Legend[0]);
        Assert.Equal("1.00 KB", entry.SizeText);
    }

    /// <summary>
    /// Clearing empties the chart.
    /// </summary>
    [Fact]
    public void Clear_EmptiesTheChart()
    {
        _chart.Update("t", _container, [Item(".x", 5)], null);

        _chart.Clear();

        Assert.False(_chart.HasData);
        Assert.Empty(_chart.Legend);
        Assert.Empty(_chart.Slices);
        Assert.Equal(string.Empty, _chart.TotalText);
    }

    /// <summary>
    /// Creates a chart item.
    /// </summary>
    /// <param name="type">The type key.</param>
    /// <param name="size">The size.</param>
    /// <returns>The item.</returns>
    private static ChartItem Item(string type, long size) =>
        new(type, type, type == ".pdf" ? "PDF document" : $"{type} file", size);
}
