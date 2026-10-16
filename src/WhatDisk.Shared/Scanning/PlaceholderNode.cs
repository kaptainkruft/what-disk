using WhatDisk.Volumes;

namespace WhatDisk.Scanning;

/// <summary>
/// A directory that was deliberately not traversed in place, such as a mount point whose volume is listed as a
/// separate top-level item, or a virtual file system. It counts zero bytes.
/// </summary>
public sealed class PlaceholderNode : FileSystemNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlaceholderNode"/> class.
    /// </summary>
    /// <param name="name">The directory name.</param>
    /// <param name="parent">The containing directory.</param>
    /// <param name="reason">Why the directory was not traversed in place.</param>
    /// <param name="targetVolume">The volume the directory leads to, when known.</param>
    /// <param name="linkedRoot">The top-level item that lists the directory's contents, when there is one.</param>
    internal PlaceholderNode(string name, DirectoryNode parent, SkipReason reason, VolumeInfo? targetVolume, RootNode? linkedRoot)
        : base(name, parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        Reason = reason;
        TargetVolume = targetVolume;
        LinkedRoot = linkedRoot;
    }

    /// <inheritdoc/>
    public override NodeKind Kind => NodeKind.MountPoint;

    /// <inheritdoc/>
    public override long Size => 0;

    /// <inheritdoc/>
    public override long LogicalSize => 0;

    /// <inheritdoc/>
    public override long FileCount => 0;

    /// <inheritdoc/>
    public override long DirectoryCount => 0;

    /// <inheritdoc/>
    public override VolumeInfo Volume => Parent!.Volume;

    /// <inheritdoc/>
    public override DateTime? LastWriteTimeUtc => null;

    /// <summary>
    /// Gets why the directory was not traversed in place.
    /// </summary>
    public SkipReason Reason { get; }

    /// <summary>
    /// Gets the volume the directory leads to, when known.
    /// </summary>
    public VolumeInfo? TargetVolume { get; }

    /// <summary>
    /// Gets the top-level item that lists the directory's contents, when there is one.
    /// </summary>
    public RootNode? LinkedRoot { get; }
}
