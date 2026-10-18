using Avalonia.Interactivity;
using WhatDisk.Hosting;

namespace WhatDisk.Views;

/// <summary>
/// Shows the application's name, version and release time, inside the main window.
/// </summary>
public partial class AboutView : InWindowDialog
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AboutView"/> class.
    /// </summary>
    public AboutView()
    {
        InitializeComponent();
        RuntimeLine.Text = AppVersionInfo.RuntimeText;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutView"/> class showing the given version information.
    /// </summary>
    /// <param name="info">The version information.</param>
    public AboutView(AppVersionInfo info)
        : this() => DataContext = info;

    /// <summary>
    /// Creates the About box for the running application.
    /// </summary>
    /// <returns>The About box.</returns>
    public static AboutView ForRunningApp() =>
        new(AppVersionInfo.FromAssembly(System.Reflection.Assembly.GetEntryAssembly() ?? typeof(AboutView).Assembly));

    /// <inheritdoc/>
    public override void FocusDefault() => OkButton.Focus();

    /// <summary>
    /// Closes the About box.
    /// </summary>
    /// <param name="sender">The OK button.</param>
    /// <param name="e">Unused.</param>
    private void OnOkClick(object? sender, RoutedEventArgs e) => Complete(null);
}
