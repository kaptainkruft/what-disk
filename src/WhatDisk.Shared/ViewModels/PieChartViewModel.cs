using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WhatDisk.Charting;
using WhatDisk.Formatting;

namespace WhatDisk.ViewModels;

/// <summary>
/// The pie chart pane: how the space in the focused folder divides among its largest file types.
/// </summary>
/// <param name="sizes">Formats byte counts.</param>
public sealed partial class PieChartViewModel(ByteSizeFormatter sizes) : ObservableObject
{
    /// <summary>Computes slices with stable colours.</summary>
    private readonly PieChartModel _model = new();

    /// <summary>Formats byte counts.</summary>
    private readonly ByteSizeFormatter _sizes = sizes ?? throw new ArgumentNullException(nameof(sizes));

    /// <summary>
    /// Gets the legend entries, largest first and "other" last.
    /// </summary>
    public ObservableCollection<LegendEntryViewModel> Legend { get; } = [];

    /// <summary>
    /// Gets the chart's title, naming the folder whose contents are charted.
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
    /// Gets the key of the slice that is emphasised, such as the type of the selected file.
    /// </summary>
    [ObservableProperty]
    public partial string? HighlightedKey { get; private set; }

    /// <summary>
    /// Gets a value indicating whether there is anything to chart.
    /// </summary>
    [ObservableProperty]
    public partial bool HasData { get; private set; }

    /// <summary>
    /// Recomputes the chart.
    /// </summary>
    /// <param name="title">The chart's title.</param>
    /// <param name="container">Identifies what is charted; colours are kept while it is unchanged.</param>
    /// <param name="items">The items to chart.</param>
    /// <param name="highlightedKey">The key of the item to emphasise, if any.</param>
    public void Update(string title, object? container, IReadOnlyList<ChartItem> items, string? highlightedKey)
    {
        ArgumentNullException.ThrowIfNull(items);
        var snapshot = _model.Update(container, items);
        Title = title;
        TotalText = snapshot.Total > 0 ? _sizes.Format(snapshot.Total) : string.Empty;
        HasData = snapshot.Slices.Count > 0;
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
            Legend[index].Update(slice, _sizes.Format(slice.Size), DisplayFormat.Percent(slice.Fraction), false);
        }

        while (Legend.Count > snapshot.Legend.Count)
        {
            Legend.RemoveAt(Legend.Count - 1);
        }

        Highlight(highlightedKey);
    }

    /// <summary>
    /// Emphasises the slice and legend entry of an item without recomputing the chart.
    /// </summary>
    /// <param name="key">The key to emphasise, or <see langword="null"/> for none.</param>
    public void Highlight(string? key)
    {
        HighlightedKey = key;
        foreach (var entry in Legend)
        {
            entry.SetHighlighted(key is not null && entry.Key == key);
        }
    }

    /// <summary>
    /// Clears the chart.
    /// </summary>
    public void Clear() => Update(string.Empty, null, [], null);
}
