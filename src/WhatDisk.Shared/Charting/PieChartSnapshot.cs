namespace WhatDisk.Charting;

/// <summary>
/// The computed contents of the pie chart at one moment.
/// </summary>
/// <param name="Total">The total number of bytes represented.</param>
/// <param name="Slices">The slices in drawing order (by colour slot, "Other" last).</param>
/// <param name="Legend">The same slices ordered for the legend (largest first, "Other" last).</param>
public sealed record PieChartSnapshot(long Total, IReadOnlyList<PieSlice> Slices, IReadOnlyList<PieSlice> Legend)
{
    /// <summary>
    /// Gets an empty chart.
    /// </summary>
    public static PieChartSnapshot Empty { get; } = new(0, [], []);
}
