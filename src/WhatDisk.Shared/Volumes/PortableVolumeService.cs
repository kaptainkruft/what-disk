using System.Diagnostics;

namespace WhatDisk.Volumes;

/// <summary>
/// A volume service built on <see cref="DriveInfo"/>. It works everywhere .NET runs but knows nothing about device
/// numbers, so platforms supply richer implementations; it remains the fallback.
/// </summary>
/// <param name="pathComparison">How paths are compared on this platform.</param>
public class PortableVolumeService(StringComparison pathComparison = StringComparison.Ordinal) : IVolumeService
{
    /// <summary>
    /// Gets how paths are compared on this platform.
    /// </summary>
    protected StringComparison PathComparison { get; } = pathComparison;

    /// <inheritdoc/>
    public virtual IReadOnlyList<VolumeInfo> GetSelectableVolumes() =>
        [.. GetMountTable().Entries
            .Select(static entry => entry.Volume)
            .Where(static volume => volume.Kind is not (VolumeKind.Virtual or VolumeKind.Unknown) && volume.TotalBytes > 0)];

    /// <inheritdoc/>
    public virtual MountTable GetMountTable()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Could not list drives: {0}", exception.Message);
            return new MountTable([], PathComparison);
        }

        return new MountTable(drives.Select(static drive => new MountEntry(drive.Name, CreateVolume(drive))), PathComparison);
    }

    /// <inheritdoc/>
    public virtual VolumeInfo? QueryVolume(string path)
    {
        try
        {
            var drive = new DriveInfo(path);
            return drive.IsReady ? CreateVolume(drive) : null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Describes a drive, tolerating drives that are not ready or cannot be queried.
    /// </summary>
    /// <param name="drive">The drive.</param>
    /// <returns>The volume description.</returns>
    protected static VolumeInfo CreateVolume(DriveInfo drive)
    {
        ArgumentNullException.ThrowIfNull(drive);
        string? format = null, label = null;
        long total = 0, available = 0;
        try
        {
            if (drive.IsReady)
            {
                format = drive.DriveFormat;
                label = drive.VolumeLabel;
                total = drive.TotalSize;
                available = drive.AvailableFreeSpace;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Could not query the drive {0}: {1}", drive.Name, exception.Message);
        }

        var kind = FileSystemClassifier.Classify(format) ?? drive.DriveType switch
        {
            DriveType.Fixed => VolumeKind.Fixed,
            DriveType.Removable => VolumeKind.Removable,
            DriveType.Network => VolumeKind.Network,
            DriveType.CDRom => VolumeKind.Optical,
            DriveType.Ram => VolumeKind.Ram,
            _ => VolumeKind.Unknown,
        };

        return new VolumeInfo
        {
            Id = drive.Name,
            MountPoint = drive.Name,
            Name = string.IsNullOrWhiteSpace(label) || label == drive.Name ? drive.Name : $"{label} ({drive.Name})",
            FileSystem = format,
            TotalBytes = total,
            AvailableBytes = available,
            Kind = kind,
        };
    }
}
