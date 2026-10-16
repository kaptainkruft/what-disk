using WhatDisk.Scanning;

namespace WhatDisk.Platform;

/// <summary>
/// Supplies the platform's view of file types: which directories are really single items (such as macOS application
/// bundles) and how a type is described to the user.
/// </summary>
public interface IFileTypeProvider
{
    /// <summary>
    /// Gets the file type of a directory that the platform presents as a single item, such as a macOS package.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns>The type key (see <see cref="FileTypes"/>), or <see langword="null"/> for an ordinary directory.</returns>
    string? GetPackageType(DirectoryNode directory);

    /// <summary>
    /// Describes a file type in words, for example "PDF document". May be called on any thread.
    /// </summary>
    /// <param name="type">The type key (see <see cref="FileTypes"/>).</param>
    /// <returns>The description.</returns>
    string Describe(string type);
}
