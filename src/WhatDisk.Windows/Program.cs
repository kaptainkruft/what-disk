using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using WhatDisk.Hosting;

[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]

namespace WhatDisk.Windows;

/// <summary>
/// The Windows entry point.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class Program
{
    /// <summary>The <c>AttachConsole</c> argument for the parent process's console.</summary>
    private const uint ParentProcess = unchecked((uint)-1);

    /// <summary>The <c>GetStdHandle</c> argument for standard output.</summary>
    private const int StandardOutput = -11;

    /// <summary>
    /// Starts the application.
    /// </summary>
    /// <param name="args">The command-line arguments: an optional path to scan and <c>--exit-when-done</c>.</param>
    /// <returns>The exit code.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Contains(StartupOptions.ExitWhenDoneOption, StringComparer.Ordinal) && GetStdHandle(StandardOutput) is 0 or -1)
        {
            // A Windows application has no console of its own; show the scan summary in the console it was started
            // from. Redirected output (to a file or pipe) already has a handle and is left alone.
            AttachConsole(ParentProcess);
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Configures Avalonia with the Win32 backend, Skia rendering and the Windows platform services.
    /// </summary>
    /// <returns>The application builder.</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        WhatDiskApp.Configure(static services => services.AddWindowsPlatform())
            .UseWin32()
            .UseSkia()
            .UseHarfBuzz();

    /// <summary>
    /// Gets a standard handle.
    /// </summary>
    /// <param name="handle">Which handle.</param>
    /// <returns>The handle, or zero or -1 when there is none.</returns>
    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int handle);

    /// <summary>
    /// Attaches the process to a console.
    /// </summary>
    /// <param name="processId">The process whose console to use.</param>
    /// <returns><see langword="true"/> on success.</returns>
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint processId);
}
