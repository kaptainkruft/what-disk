using Avalonia;
using Microsoft.Extensions.DependencyInjection;

namespace WhatDisk.Hosting;

/// <summary>
/// Builds the Avalonia application for a platform head. The head adds its platform services and its Avalonia
/// backend (windowing, rendering and text shaping) to the returned builder.
/// </summary>
public static class WhatDiskApp
{
    /// <summary>
    /// Creates the application builder.
    /// </summary>
    /// <param name="registerPlatformServices">Registers the head's platform-specific services.</param>
    /// <returns>The builder, still to be configured with an Avalonia backend.</returns>
    public static AppBuilder Configure(Action<IServiceCollection> registerPlatformServices)
    {
        ArgumentNullException.ThrowIfNull(registerPlatformServices);
        return AppBuilder.Configure(() => new App(CreateServices(registerPlatformServices))).LogToTrace();
    }

    /// <summary>
    /// Builds the service provider: platform services first, then the shared defaults.
    /// </summary>
    /// <param name="registerPlatformServices">Registers the head's platform-specific services.</param>
    /// <returns>The service provider.</returns>
    public static ServiceProvider CreateServices(Action<IServiceCollection> registerPlatformServices)
    {
        ArgumentNullException.ThrowIfNull(registerPlatformServices);
        var services = new ServiceCollection();
        registerPlatformServices(services);
        services.AddWhatDisk();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
