using System.Runtime.InteropServices;

namespace WhatDisk.Scanning;

/// <summary>
/// Describes one entry of a directory, as produced by an <see cref="IFileSystemEnumerator"/>.
/// </summary>
/// <param name="Name">The entry's file name (not a path).</param>
/// <param name="Type">The kind of entry.</param>
/// <param name="LogicalSize">The length of the entry's data in bytes.</param>
/// <param name="AllocatedSize">
/// The number of bytes the entry occupies on disk, or <see cref="FileSystemEntryInfo.UnknownSize"/> when the
/// enumerator cannot determine it.
/// </param>
/// <param name="LastWriteTimeUtc">When the entry was last modified, in UTC; <see cref="DateTime.MinValue"/> if unknown.</param>
/// <param name="Attributes">Additional facts about the entry.</param>
/// <param name="Identity">The entry's device and file number, when known.</param>
/// <param name="LinkCount">The number of hard links to the entry, or zero when unknown.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct FileSystemEntryInfo(
    string Name,
    EntryType Type,
    long LogicalSize,
    long AllocatedSize,
    DateTime LastWriteTimeUtc,
    EntryAttributes Attributes = EntryAttributes.None,
    FileIdentity Identity = default,
    uint LinkCount = 0)
{
    /// <summary>
    /// The value of <see cref="AllocatedSize"/> when the allocated size is not known.
    /// </summary>
    public const long UnknownSize = -1;

    /// <summary>
    /// Gets the number of bytes the entry should count towards disk usage: the allocated size when known,
    /// otherwise the logical size.
    /// </summary>
    public long EffectiveSize => AllocatedSize >= 0 ? AllocatedSize : Math.Max(0, LogicalSize);
}
