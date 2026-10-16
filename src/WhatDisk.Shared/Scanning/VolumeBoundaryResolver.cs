using WhatDisk.Volumes;

namespace WhatDisk.Scanning;

/// <summary>
/// Detects directories that cross into another file system and decides how the scan treats them, so that every
/// volume is counted once, under its own top-level item.
/// </summary>
/// <param name="mountTable">The mount table captured when the scan started.</param>
/// <param name="enumerator">The enumerator, used to identify the target of a boundary.</param>
/// <param name="options">The scan options.</param>
internal sealed class VolumeBoundaryResolver(MountTable mountTable, IFileSystemEnumerator enumerator, ScanOptions options)
{
    /// <summary>Entry attributes that always mark a boundary.</summary>
    private const EntryAttributes BoundaryAttributes =
        EntryAttributes.MountPoint | EntryAttributes.Firmlink | EntryAttributes.AutomountTrigger;

    /// <summary>
    /// Determines whether a directory entry leads into another file system.
    /// </summary>
    /// <param name="entry">The directory entry.</param>
    /// <param name="parentIdentity">The identity of the directory containing the entry.</param>
    /// <param name="childPath">The absolute path of the entry.</param>
    /// <returns><see langword="true"/> when the entry is a mount point, firmlink or device change.</returns>
    public bool IsBoundary(in FileSystemEntryInfo entry, FileIdentity parentIdentity, string childPath)
    {
        if ((entry.Attributes & BoundaryAttributes) != 0)
        {
            return true;
        }

        if (entry.Identity.IsKnown && parentIdentity.IsKnown && entry.Identity.Device != parentIdentity.Device)
        {
            return true;
        }

        return mountTable.IsMountPoint(childPath);
    }

    /// <summary>
    /// Decides how to treat a directory that <see cref="IsBoundary"/> identified as a boundary.
    /// </summary>
    /// <param name="entry">The directory entry.</param>
    /// <param name="childPath">The absolute path of the entry.</param>
    /// <param name="parentVolume">The volume of the directory containing the entry.</param>
    /// <param name="findFullRoot">Finds the top-level root that covers an entire volume, by volume identifier.</param>
    /// <returns>The decision.</returns>
    public BoundaryDecision Resolve(
        in FileSystemEntryInfo entry,
        string childPath,
        VolumeInfo parentVolume,
        Func<string, RootNode?> findFullRoot)
    {
        ArgumentNullException.ThrowIfNull(parentVolume);
        ArgumentNullException.ThrowIfNull(findFullRoot);
        if ((entry.Attributes & EntryAttributes.AutomountTrigger) != 0)
        {
            return BoundaryDecision.Skip(SkipReason.VirtualFileSystem, null);
        }

        var mount = mountTable.FindByMountPoint(childPath);
        if (mount is null)
        {
            var identity = enumerator.GetIdentity(childPath);
            if (identity.IsKnown)
            {
                mount = mountTable.FindByDevice(identity.Device);
            }
        }

        if (mount is null)
        {
            // Nothing in the mount table describes the target (for example a btrfs sub-volume), so it is treated as
            // part of the parent's file system.
            return BoundaryDecision.Traverse;
        }

        var volume = mount.Volume;
        if (volume.Kind == VolumeKind.Virtual)
        {
            return BoundaryDecision.Skip(SkipReason.VirtualFileSystem, volume);
        }

        if (volume.Kind == VolumeKind.Network && !options.IncludeNetworkVolumes)
        {
            return BoundaryDecision.Skip(SkipReason.NetworkVolume, volume);
        }

        if (findFullRoot(volume.Id) is { } root)
        {
            var reason = volume.Id == parentVolume.Id ? SkipReason.CountedElsewhere : SkipReason.SeparateVolume;
            return BoundaryDecision.Skip(reason, volume, root);
        }

        var isVolumeRoot = !mount.IsPartial && string.Equals(mount.MountPoint, childPath, mountTable.PathComparison);
        return BoundaryDecision.NewRoot(volume, isVolumeRoot);
    }
}
