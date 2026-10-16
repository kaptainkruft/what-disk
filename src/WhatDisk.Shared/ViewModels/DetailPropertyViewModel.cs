using CommunityToolkit.Mvvm.ComponentModel;

namespace WhatDisk.ViewModels;

/// <summary>
/// One labelled value in the selection details pane.
/// </summary>
/// <param name="label">The label.</param>
/// <param name="value">The initial value.</param>
public sealed partial class DetailPropertyViewModel(string label, string value) : ObservableObject
{
    /// <summary>
    /// Gets the label.
    /// </summary>
    public string Label { get; } = label;

    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    [ObservableProperty]
    public partial string Value { get; set; } = value;
}
