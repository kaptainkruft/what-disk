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
    /// Shows the About window for the running application, modally over an owner window when there is one.
    /// </summary>
    /// <param name="owner">The window that owns the About window, or <see langword="null"/>.</param>
    /// <returns>A task that completes when the About window has closed (or, without an owner, has opened).</returns>
    public static async Task ShowAsync(Window? owner)
    {
        var about = new AboutWindow(AppVersionInfo.FromAssembly(System.Reflection.Assembly.GetEntryAssembly() ?? typeof(AboutWindow).Assembly));
        if (owner is not null)
        {
            await about.ShowDialog(owner);
        }
        else
        {
            about.Show();
        }
    }

    /// <summary>
    /// Closes the window.
    /// </summary>
    /// <param name="sender">The OK button.</param>
    /// <param name="e">Unused.</param>
    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
