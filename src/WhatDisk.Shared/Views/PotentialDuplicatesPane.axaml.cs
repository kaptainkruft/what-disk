using Avalonia.Controls;

namespace WhatDisk.Views;

/// <summary>
/// The Potential duplicate space pane, between the chart and the details: for the folder the chart shows, the space
/// that files over 1 MiB sharing their size with another file take beyond one copy of each size, estimated from the
/// scan alone and updated while it runs.
/// </summary>
public partial class PotentialDuplicatesPane : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PotentialDuplicatesPane"/> class.
    /// </summary>
    public PotentialDuplicatesPane() => InitializeComponent();
}
