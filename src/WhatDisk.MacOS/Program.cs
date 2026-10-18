using System.Runtime.Versioning;
using Avalonia;
using WhatDisk.Hosting;
using WhatDisk.Settings;

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
    /// Avalonia designer. When the settings turn the macOS menu bar off, Avalonia's <c>DisableNativeMenus</c> option
    /// leaves the menu bar empty and the window draws its menus itself.
    /// </summary>
    /// <returns>The application builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
    {
        var useMenuBar = new AppSettings(MacPlatformServiceCollectionExtensions.Capabilities, AppSettings.DefaultFilePath())
            .Get(SettingsCatalog.General.UseMenuBar);
        return WhatDiskApp.Configure(services => services.AddMacOSPlatform(useMenuBar))
            .UseAvaloniaNative()
            .With(new MacOSPlatformOptions { DisableNativeMenus = !useMenuBar })
            .UseSkia()
            .UseHarfBuzz();
    }
}
