namespace WhatDisk.Scanning;

/// <summary>
/// Reads directory contents. Implementations use the most efficient mechanism available on their platform; all of
/// them must be safe to call concurrently from multiple threads.
/// </summary>
public interface IFileSystemEnumerator
{
    /// <summary>
    /// Gets a short name for the implementation, used in diagnostics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the identity of the object at <paramref name="path"/>, following any mount point or firmlink at the end of
    /// the path to the object that is actually reached.
    /// </summary>
    /// <param name="path">An absolute path.</param>
    /// <returns>The identity, or <see cref="FileIdentity.Unknown"/> when the platform does not expose one or the call fails.</returns>
    FileIdentity GetIdentity(string path);

    /// <summary>
    /// Gets the canonical form of an absolute path, with symbolic links resolved, so that it can be compared with the
    /// mount points the operating system reports. The default implementation returns the path unchanged.
    /// </summary>
    /// <param name="path">An absolute, normalised path.</param>
    /// <returns>The canonical path.</returns>
    string GetCanonicalPath(string path) => path;

    /// <summary>
    /// Reads the immediate entries of a directory (excluding <c>.</c> and <c>..</c>) and adds them to
    /// <paramref name="entries"/>. Entries read before a failure remain in the collection.
    /// </summary>
    /// <param name="path">The absolute path of the directory.</param>
    /// <param name="entries">Receives the entries.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The identity of the directory that was opened, or <see cref="FileIdentity.Unknown"/>.</returns>
    /// <exception cref="UnauthorizedAccessException">Access to the directory was denied.</exception>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    /// <exception cref="PathTooLongException">The path exceeds a platform limit.</exception>
    /// <exception cref="IOException">Any other I/O failure.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    FileIdentity ReadDirectory(string path, ICollection<FileSystemEntryInfo> entries, CancellationToken cancellationToken);
}
