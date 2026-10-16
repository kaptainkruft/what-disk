using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WhatDisk.MacOS.Interop;

namespace WhatDisk.MacOS;

/// <summary>
/// Loads the Apple frameworks used through the Objective-C runtime.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacFrameworks
{
    /// <summary>The result of the first load attempt.</summary>
    private static readonly Lazy<bool> Loaded = new(LoadCore);

    /// <summary>
    /// Loads AppKit (for <c>NSWorkspace</c>) and UniformTypeIdentifiers (for <c>UTType</c>), once.
    /// </summary>
    /// <returns><see langword="true"/> when both frameworks are available.</returns>
    public static bool Load() => Loaded.Value;

    /// <summary>
    /// Loads the frameworks.
    /// </summary>
    /// <returns><see langword="true"/> when both frameworks are available.</returns>
    private static bool LoadCore()
    {
        try
        {
            NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
            NativeLibrary.Load("/System/Library/Frameworks/UniformTypeIdentifiers.framework/UniformTypeIdentifiers");
            return ObjectiveC.GetClass("NSWorkspace") != 0 && ObjectiveC.GetClass("UTType") != 0;
        }
        catch (DllNotFoundException exception)
        {
            Trace.TraceWarning("macOS frameworks are unavailable: {0}", exception.Message);
            return false;
        }
    }
}
