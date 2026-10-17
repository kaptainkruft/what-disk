using WhatDisk.Volumes;

namespace WhatDisk.Windows;

/// <summary>
/// What Windows reports about one volume, before it becomes a <see cref="VolumeInfo"/>.
/// </summary>
/// <param name="Id">A stable identifier: the volume GUID path, or <c>network:</c> and the share for network drives.</param>
/// <param name="MountPoint">The drive root, mounted folder or share root that the volume is shown at.</param>
/// <param name="Label">The volume label, or <see langword="null"/>.</param>
/// <param name="FileSystem">The file system name, for example <c>NTFS</c>.</param>
/// <param name="DriveType">The <c>GetDriveType</c> result.</param>
/// <param name="Flags">The <c>GetVolumeInformation</c> file system flags.</param>
/// <param name="SerialNumber">The volume serial number, or zero when unknown.</param>
/// <param name="TotalBytes">The capacity.</param>
/// <param name="AvailableBytes">The free space available to the user.</param>
/// <param name="RemotePath">The network path a drive letter is mapped to, or <see langword="null"/>.</param>
internal sealed record WindowsVolumeFacts(
    string Id,
    string MountPoint,
    string? Label,
    string? FileSystem,
    uint DriveType,
    uint Flags,
    uint SerialNumber,
    long TotalBytes,
    long AvailableBytes,
    string? RemotePath = null)
{
    /// <summary>A drive of unknown type.</summary>
    public const uint DriveUnknown = 0;

    /// <summary>A removable drive, such as a USB stick or card reader.</summary>
    public const uint DriveRemovable = 2;

    /// <summary>A fixed disk.</summary>
    public const uint DriveFixed = 3;

    /// <summary>A network drive.</summary>
    public const uint DriveRemote = 4;

    /// <summary>An optical drive.</summary>
    public const uint DriveOptical = 5;

    /// <summary>A RAM disk.</summary>
    public const uint DriveRamDisk = 6;

    /// <summary>The <c>GetVolumeInformation</c> flag of read-only volumes.</summary>
    public const uint ReadOnlyVolumeFlag = 0x00080000;

    /// <summary>
    /// Gets the kind of volume.
    /// </summary>
    public VolumeKind Kind => DriveType switch
    {
        DriveFixed => VolumeKind.Fixed,
        DriveRemovable => VolumeKind.Removable,
        DriveRemote => VolumeKind.Network,
        DriveOptical => VolumeKind.Optical,
        DriveRamDisk => VolumeKind.Ram,
        _ => FileSystemClassifier.Classify(FileSystem) ?? VolumeKind.Unknown,
    };

    /// <summary>
    /// Gets the name File Explorer would show, for example "Local Disk (C:)", "Data (D:)" or
    /// "share (\\server) (Z:)".
    /// </summary>
    public string DisplayName
    {
        get
        {
            var location = WindowsPaths.IsDriveRoot(MountPoint) ? MountPoint[..2] : TrimSeparator(MountPoint);
            if (Kind == VolumeKind.Network && ParseShare(RemotePath ?? MountPoint) is var (server, share))
            {
                var network = $"{share} (\\\\{server})";
                return WindowsPaths.IsDriveRoot(MountPoint) ? $"{network} ({location})" : network;
            }

            var name = string.IsNullOrWhiteSpace(Label) ? DefaultLabel(Kind) : Label.Trim();
            return $"{name} ({location})";
        }
    }

    /// <summary>
    /// Creates the volume description.
    /// </summary>
    /// <returns>The volume.</returns>
    public VolumeInfo ToVolumeInfo() => new()
    {
        Id = Id,
        MountPoint = MountPoint,
        Name = DisplayName,
        FileSystem = string.IsNullOrEmpty(FileSystem) ? null : FileSystem,
        TotalBytes = TotalBytes,
        AvailableBytes = AvailableBytes,
        Kind = Kind,
        IsReadOnly = (Flags & ReadOnlyVolumeFlag) != 0,
        DeviceId = SerialNumber == 0 ? null : SerialNumber,
    };

    /// <summary>
    /// Chooses the volumes offered in the volume list: one entry per volume at its drive letter (or share), with a
    /// capacity, in drive-letter order. Volumes reachable only through mounted folders are scanned as part of the
    /// folder they are mounted in.
    /// </summary>
    /// <param name="entries">The mount table entries.</param>
    /// <returns>The volumes.</returns>
    public static IReadOnlyList<VolumeInfo> SelectVolumes(IEnumerable<MountEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return
        [
            .. entries
                .Where(static entry => !entry.IsPartial && (WindowsPaths.IsDriveRoot(entry.MountPoint) || entry.Volume.Kind == VolumeKind.Network))
                .Select(static entry => entry.Volume)
                .Where(static volume => volume.Kind is not (VolumeKind.Unknown or VolumeKind.Virtual) && volume.TotalBytes > 0)
                .DistinctBy(static volume => volume.Id, StringComparer.OrdinalIgnoreCase)
                .OrderBy(static volume => volume.MountPoint, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>
    /// Splits a network path into its server and share.
    /// </summary>
    /// <param name="path">A path such as <c>\\server\share\folder</c>.</param>
    /// <returns>The server and share, or <see langword="null"/> when the path is not a network path.</returns>
    public static (string Server, string Share)? ParseShare(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var unc = WindowsPaths.FromExtended(path);
        if (!unc.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = unc[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? (parts[0], parts[1]) : null;
    }

    /// <summary>
    /// Gets File Explorer's name for an unlabelled volume.
    /// </summary>
    /// <param name="kind">The kind of volume.</param>
    /// <returns>The name.</returns>
    private static string DefaultLabel(VolumeKind kind) => kind switch
    {
        VolumeKind.Fixed => "Local Disk",
        VolumeKind.Removable => "USB Drive",
        VolumeKind.Optical => "CD Drive",
        VolumeKind.Network => "Network Drive",
        VolumeKind.Ram => "RAM Disk",
        _ => "Drive",
    };

    /// <summary>
    /// Removes a trailing backslash from a mounted folder.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The path without the trailing backslash.</returns>
    private static string TrimSeparator(string path) => path.Length > 3 ? path.TrimEnd('\\') : path;
}
