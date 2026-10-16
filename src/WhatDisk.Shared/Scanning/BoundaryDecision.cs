using WhatDisk.Volumes;

namespace WhatDisk.Scanning;

/// <summary>
/// What the scanner does with a directory that crosses into another file system.
/// </summary>
internal enum BoundaryAction
{
    /// <summary>Traverse the directory in place, as an ordinary directory.</summary>
    Traverse,

    /// <summary>List the directory as a placeholder without traversing it.</summary>
    Skip,

    /// <summary>List the directory as a placeholder and scan it as a new top-level root.</summary>
    NewRoot,
}

/// <summary>
/// The outcome of resolving a file system boundary.
/// </summary>
/// <param name="Action">What to do with the directory.</param>
/// <param name="Reason">Why the directory is not traversed in place (ignored for <see cref="BoundaryAction.Traverse"/>).</param>
/// <param name="Volume">The volume the directory leads to, when known.</param>
/// <param name="LinkedRoot">The existing top-level root that lists the directory's contents, if any.</param>
/// <param name="IsVolumeRoot">For <see cref="BoundaryAction.NewRoot"/>, whether the directory is the root of <paramref name="Volume"/>.</param>
internal readonly record struct BoundaryDecision(
    BoundaryAction Action,
    SkipReason Reason = SkipReason.SeparateVolume,
    VolumeInfo? Volume = null,
    RootNode? LinkedRoot = null,
    bool IsVolumeRoot = false)
{
    /// <summary>
    /// Gets a decision to traverse the directory in place.
    /// </summary>
    public static BoundaryDecision Traverse => new(BoundaryAction.Traverse);

    /// <summary>
    /// Creates a decision to list the directory as a placeholder.
    /// </summary>
    /// <param name="reason">Why the directory is skipped.</param>
    /// <param name="volume">The volume the directory leads to, when known.</param>
    /// <param name="linkedRoot">The top-level root that lists the directory's contents, if any.</param>
    /// <returns>The decision.</returns>
    public static BoundaryDecision Skip(SkipReason reason, VolumeInfo? volume, RootNode? linkedRoot = null) =>
        new(BoundaryAction.Skip, reason, volume, linkedRoot);

    /// <summary>
    /// Creates a decision to scan the directory as a new top-level root.
    /// </summary>
    /// <param name="volume">The volume the directory belongs to.</param>
    /// <param name="isVolumeRoot">Whether the directory is the root of <paramref name="volume"/>.</param>
    /// <returns>The decision.</returns>
    public static BoundaryDecision NewRoot(VolumeInfo volume, bool isVolumeRoot) =>
        new(BoundaryAction.NewRoot, SkipReason.SeparateVolume, volume, null, isVolumeRoot);
}
