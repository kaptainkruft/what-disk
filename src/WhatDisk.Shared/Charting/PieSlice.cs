namespace WhatDisk.Charting;

/// <summary>
/// One slice of the pie chart.
/// </summary>
/// <param name="Key">The key of the item the slice represents, or <see langword="null"/> for the "other" slice.</param>
/// <param name="Label">The short label shown for the slice.</param>
/// <param name="Description">A longer description, shown in tooltips.</param>
/// <param name="Size">The number of bytes the slice represents.</param>
/// <param name="Fraction">The slice's share of the whole, from 0 to 1.</param>
/// <param name="ColorSlot">The categorical colour slot (0-based), or <see cref="PieChartModel.OtherSlot"/>.</param>
/// <param name="StartAngle">Where the slice starts, in degrees clockwise from twelve o'clock.</param>
/// <param name="SweepAngle">How far the slice extends, in degrees.</param>
public sealed record PieSlice(
    string? Key,
    string Label,
    string Description,
    long Size,
    double Fraction,
    int ColorSlot,
    double StartAngle,
    double SweepAngle)
{
    /// <summary>
    /// Gets a value indicating whether this is the "other" slice that groups the remaining items.
    /// </summary>
    public bool IsOther => ColorSlot == PieChartModel.OtherSlot;
}
