using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WhatDisk.Charting;
using WhatDisk.Formatting;
using WhatDisk.Scanning;

namespace WhatDisk.ViewModels;

/// <summary>
/// The pie chart pane: how the focused container's size is divided among its largest children.
/// </summary>
/// <param name="sizes">Formats byte counts.</param>
public sealed partial class PieChartViewModel(ByteSizeFormatter sizes) : ObservableObject
{
    /// <summary>Computes slices with stable colours.</summary>
    private readonly PieChartModel _model = new();

    /// <summary>Formats byte counts.</summary>
    private readonly ByteSizeFormatter _sizes = sizes ?? throw new ArgumentNullException(nameof(sizes));

    /// <summary>
    /// Gets the legend entries, largest first and "Other" last.
    /// </summary>
    public ObservableCollection<LegendEntryViewModel> Legend { get; } = [];

    /// <summary>
    /// Gets the name of the container whose contents are charted.
    /// </summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the formatted total size of the charted items.
    /// </summary>
    [ObservableProperty]
    public partial string TotalText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the slices in drawing order.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<ChartSlice> Slices { get; private set; } = [];

    /// <summary>
    /// Gets the item whose slice is emphasised, when the selection is one of the charted items.
    /// </summary>
    [ObservableProperty]
    public partial FileSystemNode? HighlightedNode { get; private set; }

    /// <summary>
    /// Gets a value indicating whether there is anything to chart.
    /// </summary>
    [ObservableProperty]
    public partial bool HasData { get; private set; }

    /// <summary>
    /// Recomputes the chart.
    /// </summary>
    /// <param name="title">The name of the container.</param>
    /// <param name="container">Identifies the container; colours are kept while it is unchanged.</param>
    /// <param name="items">The items to chart.</param>
    /// <param name="highlighted">The item to emphasise, if any.</param>
    public void Update(string title, object? container, IReadOnlyList<FileSystemNode> items, FileSystemNode? highlighted)
    {
        ArgumentNullException.ThrowIfNull(items);
        var snapshot = _model.Update(container, items);
        Title = title;
        TotalText = snapshot.Total > 0 ? _sizes.Format(snapshot.Total) : string.Empty;
        HasData = snapshot.Slices.Count > 0;
        HighlightedNode = highlighted;
        ChartSlice[] slices = [.. snapshot.Slices.Select(slice => new ChartSlice(slice, _sizes.Format(slice.Size), DisplayFormat.Percent(slice.Fraction)))];
        if (!Slices.SequenceEqual(slices))
        {
            Slices = slices;
        }

        for (var index = 0; index < snapshot.Legend.Count; index++)
        {
            if (index == Legend.Count)
            {
                Legend.Add(new LegendEntryViewModel());
            }

            var slice = snapshot.Legend[index];
            var isHighlighted = highlighted is not null && ReferenceEquals(slice.Node, highlighted);
            Legend[index].Update(slice, _sizes.Format(slice.Size), DisplayFormat.Percent(slice.Fraction), isHighlighted);
        }

        while (Legend.Count > snapshot.Legend.Count)
        {
            Legend.RemoveAt(Legend.Count - 1);
        }
    }

    /// <summary>
    /// Clears the chart.
    /// </summary>
    public void Clear() => Update(string.Empty, null, [], null);
}
