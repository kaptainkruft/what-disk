using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using WhatDisk.Hosting;
using WhatDisk.Icons;
using WhatDisk.ViewModels;
using WhatDisk.Views;

namespace WhatDisk;

/// <summary>
/// The WhatDisk application: loads the theme and shows the main window, built from the service provider supplied by
/// the platform head.
/// </summary>
public partial class App : Application
{
    /// <summary>The services built by the platform head, or <see langword="null"/> at design time.</summary>
    private readonly IServiceProvider? _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class without services, for designers and tests.
    /// </summary>
    public App()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class.
    /// </summary>
    /// <param name="services">The application's services.</param>
    public App(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    /// <inheritdoc/>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc/>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && _services is not null)
        {
            Resources[IconCache.ResourceKey] = _services.GetRequiredService<IconCache>();
            var window = _services.GetRequiredService<MainWindow>();
            var viewModel = _services.GetRequiredService<MainWindowViewModel>();
            window.DataContext = viewModel;
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => (_services as IDisposable)?.Dispose();

            var options = StartupOptions.Parse(desktop.Args);
            if (options.ScanPath is { } path)
            {
                window.Opened += async (_, _) =>
                {
                    viewModel.PathText = path;
                    await viewModel.StartScanCommand.ExecuteAsync(null);
                    if (options.ExitWhenDone)
                    {
                        Console.WriteLine(viewModel.StatusText);
                        desktop.Shutdown(viewModel.Session?.Status == Scanning.ScanStatus.Completed ? 0 : 1);
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
