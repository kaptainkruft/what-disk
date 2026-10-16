using System.Runtime.Versioning;
using Avalonia;
using WhatDisk.Hosting;

namespace WhatDisk.MacOS;

/// <summary>
/// The macOS entry point.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class Program
{
    /// <summary>
    /// Starts the application.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The process exit code.</returns>
    [STAThread]
    public static int Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Configures the application with the macOS services and Avalonia's native Cocoa backend. Also used by the
    /// Avalonia designer.
    /// </summary>
    /// <returns>The application builder.</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        WhatDiskApp.Configure(static services => services.AddMacOSPlatform())
            .UseAvaloniaNative()
            .UseSkia()
            .UseHarfBuzz();
}
