using System.Collections.Frozen;

namespace WhatDisk.MacOS;

/// <summary>
/// Recognises directories that macOS presents as single items (packages), such as applications and Photos libraries.
/// </summary>
internal static class MacPackages
{
    /// <summary>Directory extensions that macOS presents as packages.</summary>
    private static readonly FrozenSet<string> Extensions = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        ".app", ".appex", ".bundle", ".framework", ".plugin", ".kext", ".prefpane", ".qlgenerator", ".saver", ".xpc",
        ".mdimporter", ".photoslibrary", ".musiclibrary", ".tvlibrary", ".imovielibrary", ".fcpbundle", ".logicx",
        ".band", ".pkg", ".mpkg", ".xcodeproj", ".xcworkspace", ".playground", ".rtfd", ".pages", ".numbers", ".key",
        ".scptd", ".docarchive", ".photolibrary", ".aplibrary", ".theater");

    /// <summary>
    /// Determines whether a directory name denotes a package.
    /// </summary>
    /// <param name="name">The directory name.</param>
    /// <returns><see langword="true"/> for package extensions.</returns>
    public static bool IsPackageName(string name) => Extensions.Contains(Path.GetExtension(name));
}
