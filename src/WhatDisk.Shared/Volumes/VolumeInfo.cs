namespace WhatDisk.Volumes;

/// <summary>
/// Describes a mounted volume (file system) and its capacity.
/// </summary>
public sealed record VolumeInfo
{
    /// <summary>
    /// Gets a stable identifier for the volume that is unique among the volumes currently mounted.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the path at which the root of the volume is mounted, for example <c>C:\</c> or <c>/Volumes/Backup</c>.
    /// </summary>
    public required string MountPoint { get; init; }

    /// <summary>
    /// Gets the user-facing name of the volume, typically its label formatted the way the platform shows it.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the name of the file system format (for example <c>apfs</c>, <c>NTFS</c> or <c>ext4</c>), when known.
    /// </summary>
    public string? FileSystem { get; init; }

    /// <summary>
    /// Gets the total capacity of the volume in bytes, or zero when unknown.
    /// </summary>
    public long TotalBytes { get; init; }

    /// <summary>
    /// Gets the number of bytes available to the current user, or zero when unknown.
    /// </summary>
    public long AvailableBytes { get; init; }

    /// <summary>
    /// Gets the kind of storage that backs the volume.
    /// </summary>
    public VolumeKind Kind { get; init; }

    /// <summary>
    /// Gets a value indicating whether the volume is mounted read-only.
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// Gets the platform device number of the volume, when the platform exposes one.
    /// </summary>
    public ulong? DeviceId { get; init; }

    /// <summary>
    /// Gets the number of bytes in use, derived from the capacity and the available space.
    /// </summary>
    public long UsedBytes => Math.Max(0, TotalBytes - AvailableBytes);

    /// <summary>
    /// Gets the fraction (0 to 1) of the capacity that is in use, or zero when the capacity is unknown.
    /// </summary>
    public double UsedFraction => TotalBytes > 0 ? Math.Clamp((double)UsedBytes / TotalBytes, 0, 1) : 0;

    /// <summary>
    /// Creates a placeholder description for a volume whose details could not be determined.
    /// </summary>
    /// <param name="path">The path whose volume is unknown.</param>
    /// <returns>A volume description with no capacity information.</returns>
    public static VolumeInfo CreateUnknown(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new VolumeInfo { Id = $"unknown:{path}", MountPoint = path, Name = path, Kind = VolumeKind.Unknown };
    }
}
