using WhatDisk.Charting;

namespace WhatDisk.ViewModels;

/// <summary>
/// A pie slice together with its formatted values, for drawing and tooltips.
/// </summary>
/// <param name="Slice">The slice geometry and data.</param>
/// <param name="SizeText">The formatted size.</param>
/// <param name="PercentText">The formatted share.</param>
public sealed record ChartSlice(PieSlice Slice, string SizeText, string PercentText);
