using WhatDisk.Volumes;

namespace WhatDisk.Scanning;

/// <summary>
/// A leaf of the scan tree: a file, symbolic link or special file.
/// </summary>
public sealed class FileNode : FileSystemNode
{
    /// <summary>The time of the last modification, in UTC ticks, or zero when unknown.</summary>
    private readonly long _lastWriteTicks;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileNode"/> class.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <param name="parent">The containing directory.</param>
    /// <param name="kind">The kind of leaf: <see cref="NodeKind.File"/>, <see cref="NodeKind.SymbolicLink"/> or <see cref="NodeKind.Special"/>.</param>
    /// <param name="size">The counted size in bytes (zero for an additional hard link to an already counted file).</param>
    /// <param name="logicalSize">The logical size in bytes.</param>
    /// <param name="lastWriteTimeUtc">When the file was last modified, in UTC; <see cref="DateTime.MinValue"/> if unknown.</param>
    /// <param name="attributes">Additional facts about the file.</param>
    /// <param name="isHardLinkDuplicate">Whether the file is another hard link to data already counted elsewhere.</param>
    internal FileNode(
        string name,
        DirectoryNode parent,
        NodeKind kind,
        long size,
        long logicalSize,
        DateTime lastWriteTimeUtc,
        EntryAttributes attributes,
        bool isHardLinkDuplicate)
        : base(name, parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        Kind = kind;
        Size = size;
        LogicalSize = logicalSize;
        _lastWriteTicks = lastWriteTimeUtc == DateTime.MinValue ? 0 : lastWriteTimeUtc.Ticks;
        Attributes = attributes;
        IsHardLinkDuplicate = isHardLinkDuplicate;
    }

    /// <inheritdoc/>
    public override NodeKind Kind { get; }

    /// <inheritdoc/>
    public override long Size { get; }

    /// <inheritdoc/>
    public override long LogicalSize { get; }

    /// <inheritdoc/>
    public override long FileCount => 1;

    /// <inheritdoc/>
    public override long DirectoryCount => 0;

    /// <inheritdoc/>
    public override VolumeInfo Volume => Parent!.Volume;

    /// <inheritdoc/>
    public override DateTime? LastWriteTimeUtc => _lastWriteTicks == 0 ? null : new DateTime(_lastWriteTicks, DateTimeKind.Utc);

    /// <summary>
    /// Gets additional facts about the file.
    /// </summary>
    public EntryAttributes Attributes { get; }

    /// <summary>
    /// Gets a value indicating whether the file is another hard link to data that is already counted elsewhere in the
    /// scan; such files count zero bytes.
    /// </summary>
    public bool IsHardLinkDuplicate { get; }
}
