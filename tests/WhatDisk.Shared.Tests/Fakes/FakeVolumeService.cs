using WhatDisk.Volumes;

namespace WhatDisk.Tests.Fakes;

/// <summary>
/// A volume service backed by a fixed list of mount entries.
/// </summary>
internal sealed class FakeVolumeService : IVolumeService
{
    /// <summary>
    /// Gets the mount entries returned by <see cref="GetMountTable"/>.
    /// </summary>
    public List<MountEntry> Mounts { get; } = [];

    /// <summary>
    /// Gets the volumes returned by <see cref="GetSelectableVolumes"/>.
    /// </summary>
    public List<VolumeInfo> Selectable { get; } = [];

    /// <summary>
    /// Gets or sets the volume returned by <see cref="QueryVolume"/>.
    /// </summary>
    public VolumeInfo? QueriedVolume { get; set; }

    /// <summary>
    /// Gets the number of times <see cref="GetSelectableVolumes"/> was called.
    /// </summary>
    public int SelectableCalls { get; private set; }

    /// <summary>
    /// Creates a volume description for tests.
    /// </summary>
    /// <param name="id">The volume identifier.</param>
    /// <param name="mountPoint">The mount point.</param>
    /// <param name="kind">The kind of volume.</param>
    /// <param name="totalBytes">The capacity.</param>
    /// <param name="deviceId">The device number.</param>
    /// <returns>The volume.</returns>
    public static VolumeInfo CreateVolume(string id, string mountPoint, VolumeKind kind = VolumeKind.Fixed, long totalBytes = 1_000_000, ulong? deviceId = null) =>
        new()
        {
            Id = id,
            MountPoint = mountPoint,
            Name = $"Volume {id}",
            Kind = kind,
            TotalBytes = totalBytes,
            AvailableBytes = totalBytes / 2,
            DeviceId = deviceId,
            FileSystem = "fakefs",
        };

    /// <summary>
    /// Adds a mount entry for a new volume.
    /// </summary>
    /// <param name="id">The volume identifier.</param>
    /// <param name="mountPoint">The mount point.</param>
    /// <param name="kind">The kind of volume.</param>
    /// <param name="deviceId">The device number.</param>
    /// <param name="isPartial">Whether the mount exposes only part of the file system.</param>
    /// <returns>The volume that was added.</returns>
    public VolumeInfo Mount(string id, string mountPoint, VolumeKind kind = VolumeKind.Fixed, ulong? deviceId = null, bool isPartial = false)
    {
        var volume = CreateVolume(id, mountPoint, kind, deviceId: deviceId);
        Mounts.Add(new MountEntry(mountPoint, volume, isPartial));
        return volume;
    }

    /// <inheritdoc/>
    public IReadOnlyList<VolumeInfo> GetSelectableVolumes()
    {
        SelectableCalls++;
        return Selectable;
    }

    /// <inheritdoc/>
    public MountTable GetMountTable() => new(Mounts, StringComparison.Ordinal);

    /// <inheritdoc/>
    public VolumeInfo? QueryVolume(string path) => QueriedVolume;
}
