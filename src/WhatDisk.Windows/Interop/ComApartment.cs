using System.Runtime.Versioning;

namespace WhatDisk.Windows.Interop;

/// <summary>
/// Makes sure COM is initialised on the calling thread for the lifetime of a scope, as the shell functions require.
/// A thread that is already in an apartment keeps it.
/// </summary>
[SupportedOSPlatform("windows")]
internal readonly struct ComApartment : IDisposable
{
    /// <summary>Whether this scope initialised COM and must uninitialise it.</summary>
    private readonly bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComApartment"/> struct.
    /// </summary>
    /// <param name="initialized">Whether COM was initialised by this scope.</param>
    private ComApartment(bool initialized) => _initialized = initialized;

    /// <summary>
    /// Enters COM: joins the multi-threaded apartment unless the thread already has one.
    /// </summary>
    /// <returns>The scope, to be disposed on the same thread.</returns>
    public static ComApartment Enter()
    {
        var result = Ole32.CoInitializeEx(0, Ole32.MultiThreaded);
        return new ComApartment(result >= 0);
    }

    /// <summary>
    /// Leaves COM if this scope entered it.
    /// </summary>
    public void Dispose()
    {
        if (_initialized)
        {
            Ole32.CoUninitialize();
        }
    }
}
