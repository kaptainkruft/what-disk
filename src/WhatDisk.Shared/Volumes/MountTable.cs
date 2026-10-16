using System.Collections.Frozen;
using WhatDisk.Platform;

namespace WhatDisk.Volumes;

/// <summary>
/// An immutable snapshot of the mounted file systems with fast lookups by mount point and by device.
/// </summary>
public sealed class MountTable
{
    /// <summary>The entries indexed by their (normalised) mount point.</summary>
    private readonly FrozenDictionary<string, MountEntry> _byMountPoint;

    /// <summary>The entries indexed by device number; full mounts are preferred over partial (bind) mounts.</summary>
    private readonly FrozenDictionary<ulong, MountEntry> _byDevice;

    /// <summary>The entries ordered by descending mount point length, for longest-prefix searches.</summary>
    private readonly MountEntry[] _longestFirst;

    /// <summary>
    /// Initializes a new instance of the <see cref="MountTable"/> class.
    /// </summary>
    /// <param name="entries">The mount entries, in mount order (later entries shadow earlier ones).</param>
    /// <param name="pathComparison">How paths are compared on this platform.</param>
    public MountTable(IEnumerable<MountEntry> entries, StringComparison pathComparison)
    {
        ArgumentNullException.ThrowIfNull(entries);
        PathComparison = pathComparison;
        MountEntry[] normalized = [.. entries.Select(static entry => entry with { MountPoint = PathUtilities.TrimTrailingSeparator(entry.MountPoint) })];
        Entries = normalized;

        var byMountPoint = new Dictionary<string, MountEntry>(StringComparer.FromComparison(pathComparison));
        var byDevice = new Dictionary<ulong, MountEntry>();
        foreach (var entry in normalized)
        {
            byMountPoint[entry.MountPoint] = entry;
            if (entry.Volume.DeviceId is { } device
                && (!byDevice.TryGetValue(device, out var existing) || (existing.IsPartial && !entry.IsPartial)))
            {
                byDevice[device] = entry;
            }
        }

        _byMountPoint = byMountPoint.ToFrozenDictionary(byMountPoint.Comparer);
        _byDevice = byDevice.ToFrozenDictionary();
        _longestFirst = [.. normalized.OrderByDescending(static entry => entry.MountPoint.Length)];
    }

    /// <summary>
    /// Gets an empty mount table that uses ordinal path comparison.
    /// </summary>
    public static MountTable Empty { get; } = new([], StringComparison.Ordinal);

    /// <summary>
    /// Gets every entry in the table, in mount order.
    /// </summary>
    public IReadOnlyList<MountEntry> Entries { get; }

    /// <summary>
    /// Gets how paths are compared on this platform.
    /// </summary>
    public StringComparison PathComparison { get; }

    /// <summary>
    /// Determines whether a file system is mounted at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">An absolute path.</param>
    /// <returns><see langword="true"/> when <paramref name="path"/> is a mount point.</returns>
    public bool IsMountPoint(string path) => _byMountPoint.ContainsKey(PathUtilities.TrimTrailingSeparator(path));

    /// <summary>
    /// Finds the entry mounted exactly at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">An absolute path.</param>
    /// <returns>The entry, or <see langword="null"/> when nothing is mounted there.</returns>
    public MountEntry? FindByMountPoint(string path) =>
        _byMountPoint.GetValueOrDefault(PathUtilities.TrimTrailingSeparator(path));

    /// <summary>
    /// Finds the entry for the file system with the given device number, preferring a full mount over a partial one.
    /// </summary>
    /// <param name="deviceId">The platform device number.</param>
    /// <returns>The entry, or <see langword="null"/> when no mounted file system has that device number.</returns>
    public MountEntry? FindByDevice(ulong deviceId) => _byDevice.GetValueOrDefault(deviceId);

    /// <summary>
    /// Finds the entry whose mount point is the longest prefix of <paramref name="path"/>, which is the file system
    /// containing the path when no links redirect it.
    /// </summary>
    /// <param name="path">An absolute path.</param>
    /// <returns>The containing entry, or <see langword="null"/> when none matches.</returns>
    public MountEntry? FindContaining(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        foreach (var entry in _longestFirst)
        {
            if (PathUtilities.IsSameOrDescendant(path, entry.MountPoint, PathComparison))
            {
                return entry;
            }
        }

        return null;
    }
}
