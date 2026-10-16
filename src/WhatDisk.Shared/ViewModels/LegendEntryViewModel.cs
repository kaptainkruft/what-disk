using CommunityToolkit.Mvvm.ComponentModel;
using WhatDisk.Charting;

namespace WhatDisk.ViewModels;

/// <summary>
/// One line of the pie chart legend. Entries are updated in place so the view does not rebuild them on every refresh.
/// </summary>
public sealed partial class LegendEntryViewModel : ObservableObject
{
    /// <summary>
    /// Gets the colour slot of the slice, or <see cref="PieChartModel.OtherSlot"/>.
    /// </summary>
    [ObservableProperty]
    public partial int ColorSlot { get; private set; }

    /// <summary>
    /// Gets the slice's label.
    /// </summary>
    [ObservableProperty]
    public partial string Label { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the formatted size of the slice.
    /// </summary>
    [ObservableProperty]
    public partial string SizeText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the formatted share of the slice.
    /// </summary>
    [ObservableProperty]
    public partial string PercentText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a longer description of the slice, shown as a tooltip.
    /// </summary>
    [ObservableProperty]
    public partial string Description { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the key of the item the slice represents, or <see langword="null"/> for "other".
    /// </summary>
    [ObservableProperty]
    public partial string? Key { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the entry belongs to the type of the selected item.
    /// </summary>
    [ObservableProperty]
    public partial bool IsHighlighted { get; private set; }

    /// <summary>
    /// Updates the entry from a slice.
    /// </summary>
    /// <param name="slice">The slice.</param>
    /// <param name="sizeText">The formatted size.</param>
    /// <param name="percentText">The formatted share.</param>
    /// <param name="isHighlighted">Whether the slice belongs to the type of the selected item.</param>
    internal void Update(PieSlice slice, string sizeText, string percentText, bool isHighlighted)
    {
        ColorSlot = slice.ColorSlot;
        Label = slice.Label;
        SizeText = sizeText;
        PercentText = percentText;
        Description = slice.Description;
        Key = slice.Key;
        IsHighlighted = isHighlighted;
    }

    /// <summary>
    /// Marks the entry as belonging (or not) to the type of the selected item.
    /// </summary>
    /// <param name="isHighlighted">Whether to highlight the entry.</param>
    internal void SetHighlighted(bool isHighlighted) => IsHighlighted = isHighlighted;
}
