using Avalonia.Controls;
using Avalonia.Media;
using WhatDisk.Charting;

namespace WhatDisk.Views.Controls;

/// <summary>
/// Looks up the chart palette from theme resources, so light and dark themes each use their own validated colours.
/// </summary>
internal static class ChartBrushes
{
    /// <summary>The number of categorical colour slots defined in the palette resources.</summary>
    public const int SlotCount = PieChartModel.MaxNamedSlices;

    /// <summary>
    /// Gets the brush for a colour slot.
    /// </summary>
    /// <param name="control">The control whose theme variant and resources are used.</param>
    /// <param name="slot">The colour slot, or <see cref="PieChartModel.OtherSlot"/>.</param>
    /// <returns>The brush.</returns>
    public static IBrush ForSlot(Control control, int slot)
    {
        var key = slot is >= 0 and < SlotCount ? $"ChartSeries{slot}Brush" : "ChartOtherBrush";
        return Find(control, key) ?? Brushes.Gray;
    }

    /// <summary>
    /// Finds a brush resource for the control's current theme variant.
    /// </summary>
    /// <param name="control">The control.</param>
    /// <param name="key">The resource key.</param>
    /// <returns>The brush, or <see langword="null"/> when the resource is missing.</returns>
    public static IBrush? Find(Control control, string key) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var value) ? value as IBrush : null;
}
