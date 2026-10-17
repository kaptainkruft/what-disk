using Avalonia.Controls;
using Avalonia.Interactivity;
using WhatDisk.Hosting;

namespace WhatDisk.Views;

/// <summary>
/// Shows the application's name, version and release time.
/// </summary>
public partial class AboutWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AboutWindow"/> class.
    /// </summary>
    public AboutWindow()
    {
        InitializeComponent();
        RuntimeLine.Text = AppVersionInfo.RuntimeText;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutWindow"/> class showing the given version information.
    /// </summary>
    /// <param name="info">The version information.</param>
    public AboutWindow(AppVersionInfo info)
        : this() => DataContext = info;

    /// <summary>
    /// Closes the window.
    /// </summary>
    /// <param name="sender">The OK button.</param>
    /// <param name="e">Unused.</param>
    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
