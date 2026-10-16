using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WhatDisk.Views;

/// <summary>
/// The contents of the volume flyout: the mounted volumes with their capacity, as check boxes where several can be
/// scanned together and as buttons elsewhere.
/// </summary>
public partial class VolumePicker : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VolumePicker"/> class.
    /// </summary>
    public VolumePicker() => InitializeComponent();

    /// <summary>
    /// Raised when the user has chosen what to scan, so the flyout can close.
    /// </summary>
    public event EventHandler? VolumeChosen;

    /// <summary>
    /// Signals that a scan was started from the picker.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">Unused.</param>
    private void OnVolumeButtonClick(object? sender, RoutedEventArgs e) => VolumeChosen?.Invoke(this, EventArgs.Empty);
}
