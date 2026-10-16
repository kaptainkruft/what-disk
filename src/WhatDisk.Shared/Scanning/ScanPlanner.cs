using WhatDisk.Platform;
using WhatDisk.Volumes;

namespace WhatDisk.Scanning;

/// <summary>
/// Turns a <see cref="ScanRequest"/> into the initial set of top-level roots: one per requested path, plus one per
/// volume mounted beneath a requested path.
/// </summary>
internal static class ScanPlanner
{
    /// <summary>
    /// Creates the initial roots for a scan.
    /// </summary>
    /// <param name="request">What to scan.</param>
    /// <param name="mountTable">The current mount table.</param>
    /// <param name="enumerator">The enumerator, used to identify directories.</param>
    /// <param name="volumes">The volume service, used for paths the mount table does not describe.</param>
    /// <returns>The roots, requested paths first.</returns>
    public static IReadOnlyList<RootNode> CreateRoots(
        ScanRequest request,
        MountTable mountTable,
        IFileSystemEnumerator enumerator,
        IVolumeService volumes)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(mountTable);
        ArgumentNullException.ThrowIfNull(enumerator);
        ArgumentNullException.ThrowIfNull(volumes);

        var comparison = mountTable.PathComparison;
        var targets = NormalizeTargets(request.Targets, comparison, enumerator.GetCanonicalPath);
        var roots = targets.Select(target => CreateTargetRoot(target, mountTable, enumerator, volumes)).ToList();

        var comparer = StringComparer.FromComparison(comparison);
        foreach (var mount in mountTable.Entries.OrderBy(static entry => entry.MountPoint, comparer))
        {
            if (mount.IsPartial
                || !IsScannedImplicitly(mount.Volume.Kind, request.Options)
                || !targets.Any(target => PathUtilities.IsStrictDescendant(mount.MountPoint, target, comparison))
                || IsBeneathVirtualMount(mount, mountTable, targets)
                || roots.Exists(root => (root.IsVolumeRoot && root.Volume.Id == mount.Volume.Id)
                    || string.Equals(root.RootPath, mount.MountPoint, comparison)))
            {
                continue;
            }

            roots.Add(new RootNode(mount.MountPoint, mount.Volume, enumerator.GetIdentity(mount.MountPoint), isVolumeRoot: true, isDiscovered: true));
        }

        return roots;
    }

    /// <summary>
    /// Normalises the requested paths (resolving symbolic links), removes duplicates and drops any path that lies
    /// inside another requested path.
    /// </summary>
    /// <param name="targets">The requested paths.</param>
    /// <param name="comparison">How paths are compared.</param>
    /// <param name="canonicalize">Resolves symbolic links in a normalised path.</param>
    /// <returns>The distinct, non-overlapping paths in request order.</returns>
    internal static IReadOnlyList<string> NormalizeTargets(IEnumerable<string> targets, StringComparison comparison, Func<string, string> canonicalize)
    {
        List<string> normalized =
        [
            .. targets
                .Select(target => canonicalize(PathUtilities.Normalize(target)))
                .Distinct(StringComparer.FromComparison(comparison)),
        ];
        return [.. normalized.Where(target => !normalized.Exists(other => PathUtilities.IsStrictDescendant(target, other, comparison)))];
    }

    /// <summary>
    /// Creates the root for a requested path, resolving the volume it is stored on.
    /// </summary>
    /// <param name="target">The normalised path.</param>
    /// <param name="mountTable">The current mount table.</param>
    /// <param name="enumerator">The enumerator, used to identify the directory.</param>
    /// <param name="volumes">The volume service, used when the mount table does not describe the path.</param>
    /// <returns>The root.</returns>
    private static RootNode CreateTargetRoot(string target, MountTable mountTable, IFileSystemEnumerator enumerator, IVolumeService volumes)
    {
        var identity = enumerator.GetIdentity(target);
        var mount = mountTable.FindByMountPoint(target);
        if (mount is null && identity.IsKnown)
        {
            mount = mountTable.FindByDevice(identity.Device);
        }

        mount ??= mountTable.FindContaining(target);
        if (mount is not null)
        {
            var isVolumeRoot = !mount.IsPartial && string.Equals(mount.MountPoint, target, mountTable.PathComparison);
            return new RootNode(target, mount.Volume, identity, isVolumeRoot, isDiscovered: false);
        }

        var volume = volumes.QueryVolume(target) ?? VolumeInfo.CreateUnknown(target);
        var isRoot = string.Equals(PathUtilities.TrimTrailingSeparator(volume.MountPoint), target, mountTable.PathComparison);
        return new RootNode(target, volume, identity, isRoot, isDiscovered: false);
    }

    /// <summary>
    /// Determines whether volumes of the given kind are scanned when they are found beneath a requested path.
    /// </summary>
    /// <param name="kind">The kind of volume.</param>
    /// <param name="options">The scan options.</param>
    /// <returns><see langword="true"/> when such volumes are scanned implicitly.</returns>
    private static bool IsScannedImplicitly(VolumeKind kind, ScanOptions options) => kind switch
    {
        VolumeKind.Virtual => false,
        VolumeKind.Network => options.IncludeNetworkVolumes,
        _ => true,
    };

    /// <summary>
    /// Determines whether a mount lies beneath a virtual file system that is itself inside a requested path, such as
    /// <c>/dev/shm</c> beneath <c>/dev</c> when scanning <c>/</c>. Virtual file systems that are requested directly,
    /// or that contain a requested path, do not hide the mounts beneath them.
    /// </summary>
    /// <param name="mount">The mount to test.</param>
    /// <param name="mountTable">The mount table.</param>
    /// <param name="targets">The requested paths.</param>
    /// <returns><see langword="true"/> when such a virtual mount is an ancestor of <paramref name="mount"/>.</returns>
    private static bool IsBeneathVirtualMount(MountEntry mount, MountTable mountTable, IReadOnlyList<string> targets)
    {
        var comparison = mountTable.PathComparison;
        return mountTable.Entries.Any(other => other.Volume.Kind == VolumeKind.Virtual
            && PathUtilities.IsStrictDescendant(mount.MountPoint, other.MountPoint, comparison)
            && targets.Any(target => PathUtilities.IsStrictDescendant(other.MountPoint, target, comparison)));
    }
}
