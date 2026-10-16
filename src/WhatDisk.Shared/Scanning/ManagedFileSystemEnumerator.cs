using System.IO.Enumeration;
using WhatDisk.Platform;

namespace WhatDisk.Scanning;

/// <summary>
/// A portable enumerator built on <see cref="FileSystemEnumerable{TResult}"/>, which avoids allocating
/// <see cref="FileSystemInfo"/> objects. It reports logical sizes only and cannot identify files, so it is used where
/// no faster platform-specific enumerator is available.
/// </summary>
public sealed class ManagedFileSystemEnumerator : IFileSystemEnumerator
{
    /// <summary>The number of entries read between cancellation checks.</summary>
    private const int CancellationCheckInterval = 1024;

    /// <summary>Options that return every entry of a single directory and surface access errors.</summary>
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
        BufferSize = 64 * 1024,
    };

    /// <inheritdoc/>
    public string Name => "System.IO enumeration";

    /// <inheritdoc/>
    public FileIdentity GetIdentity(string path) => FileIdentity.Unknown;

    /// <inheritdoc/>
    public string GetCanonicalPath(string path) => PathUtilities.ResolveLinks(path);

    /// <inheritdoc/>
    public FileIdentity ReadDirectory(string path, ICollection<FileSystemEntryInfo> entries, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(entries);
        cancellationToken.ThrowIfCancellationRequested();

        var count = 0;
        foreach (var entry in new FileSystemEnumerable<FileSystemEntryInfo>(path, Transform, Options))
        {
            entries.Add(entry);
            if (++count % CancellationCheckInterval == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        return FileIdentity.Unknown;
    }

    /// <summary>
    /// Converts the enumerator's transient entry into a <see cref="FileSystemEntryInfo"/>.
    /// </summary>
    /// <param name="entry">The current entry.</param>
    /// <returns>The converted entry.</returns>
    private static FileSystemEntryInfo Transform(ref FileSystemEntry entry)
    {
        var attributes = entry.Attributes;
        var isLink = (attributes & FileAttributes.ReparsePoint) != 0;
        var type = isLink ? EntryType.SymbolicLink : entry.IsDirectory ? EntryType.Directory : EntryType.File;
        var flags = EntryAttributes.None;
        if ((attributes & FileAttributes.Hidden) != 0)
        {
            flags |= EntryAttributes.Hidden;
        }

        if ((attributes & FileAttributes.System) != 0)
        {
            flags |= EntryAttributes.System;
        }

        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            flags |= EntryAttributes.ReadOnly;
        }

        if ((attributes & FileAttributes.Compressed) != 0)
        {
            flags |= EntryAttributes.Compressed;
        }

        if ((attributes & FileAttributes.SparseFile) != 0)
        {
            flags |= EntryAttributes.Sparse;
        }

        if ((attributes & FileAttributes.Offline) != 0)
        {
            flags |= EntryAttributes.Offline;
        }

        return new FileSystemEntryInfo(
            entry.FileName.ToString(),
            type,
            type == EntryType.Directory ? 0 : entry.Length,
            FileSystemEntryInfo.UnknownSize,
            entry.LastWriteTimeUtc.UtcDateTime,
            flags);
    }
}
