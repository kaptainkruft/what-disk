using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(WhatDisk.Tests.Views.HeadlessTestApp))]

namespace WhatDisk.Tests.Views;

/// <summary>
/// Configures the headless Avalonia platform used by UI tests. Skia rendering is enabled so tests can capture
/// rendered frames.
/// </summary>
public static class HeadlessTestApp
{
    /// <summary>
    /// Builds the application for headless tests.
    /// </summary>
    /// <returns>The application builder.</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
