namespace WhatDisk.Charting;

/// <summary>
/// One quantity to chart, such as the total size of one file type.
/// </summary>
/// <param name="Key">Identifies the item; its colour stays with this key.</param>
/// <param name="Label">The short label shown in the legend.</param>
/// <param name="Description">A longer description, shown in tooltips.</param>
/// <param name="Size">The number of bytes.</param>
public sealed record ChartItem(string Key, string Label, string Description, long Size);
