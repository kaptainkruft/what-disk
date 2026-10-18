using Avalonia.Controls;

namespace WhatDisk.Views;

/// <summary>
/// The Settings tab: the sections of the settings file down the left, the chosen section's settings on the right.
/// Changes apply at once, so there is no OK or Cancel; the tab closes like any other.
/// </summary>
public partial class SettingsView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsView"/> class.
    /// </summary>
    public SettingsView() => InitializeComponent();
}
