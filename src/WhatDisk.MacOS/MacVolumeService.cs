using System.Diagnostics;
using System.Runtime.Versioning;
using WhatDisk.MacOS.Interop;
using WhatDisk.Volumes;

namespace WhatDisk.MacOS;

/// <summary>
/// Describes macOS volumes. The mount list comes from <see cref="DriveInfo"/>; each mount point is then queried with
/// <c>getattrlist</c> for its physical device number, its volume name and its mount flags, which tell system volumes
/// (hidden by the Finder), snapshots and network shares apart.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacVolumeService : IVolumeService
{
    /// <summary>The mount point of the data volume of the startup disk.</summary>
    private const string DataVolumeMountPoint = "/System/Volumes/Data";

    /// <inheritdoc/>
    public IReadOnlyList<VolumeInfo> GetSelectableVolumes()
    {
        var table = GetMountTable();
        return
        [
            .. table.Entries
                .Select(static entry => entry.Volume)
                .Where(IsSelectable)
                .OrderBy(static volume => volume.MountPoint == "/" ? 0 : 1)
                .ThenBy(static volume => volume.Name, StringComparer.CurrentCultureIgnoreCase),
        ];
    }

    /// <inheritdoc/>
    public MountTable GetMountTable()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Could not list mounted volumes: {0}", exception.Message);
            return new MountTable([], StringComparison.OrdinalIgnoreCase);
        }

        var described = drives.Select(static drive => (Drive: drive, Facts: QueryVolumeFacts(drive.Name))).ToArray();
        var rootName = described.FirstOrDefault(static item => item.Drive.Name == "/").Facts.Name ?? "Macintosh HD";
        return new MountTable(
            described.Select(item => new MountEntry(item.Drive.Name, Describe(item.Drive, item.Facts, rootName))),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public VolumeInfo? QueryVolume(string path) => null;

    /// <summary>
    /// Determines whether a volume belongs in the volume picker: the startup disk and anything the Finder would show.
    /// </summary>
    /// <param name="volume">The volume.</param>
    /// <returns><see langword="true"/> for user-facing volumes.</returns>
    private static bool IsSelectable(VolumeInfo volume)
    {
        if (volume.MountPoint == "/")
        {
            return true;
        }

        return volume.Kind is not (VolumeKind.Virtual or VolumeKind.Unknown)
            && !volume.Id.EndsWith(":hidden", StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the description of one mounted volume.
    /// </summary>
    /// <param name="drive">The drive information.</param>
    /// <param name="facts">The facts read with <c>getattrlist</c>.</param>
    /// <param name="rootName">The name of the startup volume, used to name its data volume.</param>
    /// <returns>The volume description.</returns>
    private static VolumeInfo Describe(DriveInfo drive, MacVolumeFacts facts, string rootName)
    {
        var mountPoint = drive.Name.Length > 1 ? drive.Name.TrimEnd('/') : drive.Name;
        string? format = null;
        long total = 0, available = 0;
        try
        {
            format = drive.DriveFormat;
            if (FileSystemClassifier.Classify(format) != VolumeKind.Virtual && drive.IsReady)
            {
                total = drive.TotalSize;
                available = drive.AvailableFreeSpace;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Could not query the volume at {0}: {1}", mountPoint, exception.Message);
        }

        var name = mountPoint == DataVolumeMountPoint
            ? $"{rootName} – Data"
            : facts.Name ?? (mountPoint == "/" ? rootName : Path.GetFileName(mountPoint));
        var isHidden = facts.MountFlags is { } flags && (flags & AttributeConstants.MountDontBrowse) != 0;
        var device = facts.DeviceId;
        return new VolumeInfo
        {
            Id = $"{(device is { } id ? $"dev:{id:x8}" : $"mnt:{mountPoint}")}{(isHidden ? ":hidden" : string.Empty)}",
            MountPoint = mountPoint,
            Name = string.IsNullOrWhiteSpace(name) ? mountPoint : name,
            FileSystem = format,
            TotalBytes = total,
            AvailableBytes = available,
            Kind = MacVolumeFacts.Classify(mountPoint, format, facts.MountFlags),
            IsReadOnly = facts.MountFlags is { } mountFlags && (mountFlags & AttributeConstants.MountReadOnly) != 0,
            DeviceId = device,
        };
    }

    /// <summary>
    /// Reads a volume's physical device number, name and mount flags.
    /// </summary>
    /// <param name="mountPoint">The mount point.</param>
    /// <returns>The facts that could be read.</returns>
    private static MacVolumeFacts QueryVolumeFacts(string mountPoint)
    {
        var request = new AttrList
        {
            BitmapCount = AttributeConstants.BitmapCount,
            CommonAttr = AttributeConstants.CommonDeviceId,
            VolAttr = AttributeConstants.VolumeInfo | AttributeConstants.VolumeName | AttributeConstants.VolumeMountFlags,
        };
        const int Size = 1024;
        var buffer = stackalloc byte[Size];
        var options = AttributeConstants.OptionNoFollow | AttributeConstants.OptionReturnRealDevice;
        if (LibC.GetAttrList(mountPoint, &request, buffer, Size, options) != 0)
        {
            return default;
        }

        return MacVolumeFacts.Parse(new ReadOnlySpan<byte>(buffer, Size));
    }
}
