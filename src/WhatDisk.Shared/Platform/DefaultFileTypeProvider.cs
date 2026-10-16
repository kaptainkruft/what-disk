using WhatDisk.Scanning;

namespace WhatDisk.Platform;

/// <summary>
/// A file type provider that treats every directory as a directory and describes types by their extension. Platform
/// providers derive from it.
/// </summary>
public class DefaultFileTypeProvider : IFileTypeProvider
{
    /// <inheritdoc/>
    public virtual string? GetPackageType(DirectoryNode directory) => null;

    /// <inheritdoc/>
    public virtual string Describe(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Length == 0 ? "Files without an extension" : $"{type[1..].ToUpperInvariant()} file";
    }
}
