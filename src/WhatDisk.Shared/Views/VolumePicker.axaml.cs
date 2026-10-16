using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

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
    /// Signals that a scan was started from the picker. The signal is posted rather than raised at once: the button
    /// runs its command only after its Click handlers return, and closing the flyout first would detach the button
    /// and clear its command binding, so the scan would never start.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">Unused.</param>
    private void OnVolumeButtonClick(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(() => VolumeChosen?.Invoke(this, EventArgs.Empty));
}
