using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WhatDisk.Icons;
using WhatDisk.Platform;
using WhatDisk.Scanning;
using WhatDisk.Settings;
using WhatDisk.ViewModels;
using WhatDisk.Views;
using WhatDisk.Volumes;

namespace WhatDisk.Hosting;

/// <summary>
/// Registers WhatDisk's shared services. Platform heads register their own implementations first; anything they do
/// not provide falls back to the portable implementations registered here.
/// </summary>
public static class WhatDiskServiceCollectionExtensions
{
    /// <summary>
    /// Adds the shared services, view models and views.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddWhatDisk(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(PlatformCapabilities.Generic);
        services.TryAddSingleton(static provider => new AppSettings(provider.GetRequiredService<PlatformCapabilities>(), AppSettings.DefaultFilePath()));
        services.TryAddSingleton<IFileSystemEnumerator, ManagedFileSystemEnumerator>();
        services.TryAddSingleton<IVolumeService>(static _ => new PortableVolumeService());
        services.TryAddSingleton<IIconProvider, DefaultIconProvider>();
        services.TryAddSingleton<IFileTypeProvider, DefaultFileTypeProvider>();
        services.TryAddSingleton<IShellService, DefaultShellService>();
        services.TryAddSingleton<IDiskScanner, DiskScanner>();
        services.TryAddSingleton<IconCache>();
        services.TryAddSingleton(static _ => new MainWindow());
        services.TryAddSingleton<IFolderPicker>(static provider => new TopLevelFolderPicker(provider.GetRequiredService<MainWindow>));
        services.TryAddSingleton<IDialogService>(static provider => new WindowDialogService(provider.GetRequiredService<MainWindow>));
        services.TryAddSingleton<MainWindowViewModel>();
        return services;
    }
}
