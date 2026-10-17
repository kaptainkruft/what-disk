using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WhatDisk.Volumes;
using WhatDisk.Windows.Interop;

namespace WhatDisk.Windows;

/// <summary>
/// Lists Windows volumes: every local volume with all of its drive letters and mounted folders, mapped network drives,
/// and <c>subst</c> drives (as partial views of the volume they point into). Paths on other network shares are
/// described on demand.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsVolumeService : IVolumeService
{
    /// <summary>The buffer length for volume names, labels and paths.</summary>
    private const int NameLength = 1024;

    /// <inheritdoc/>
    public IReadOnlyList<VolumeInfo> GetSelectableVolumes() => WindowsVolumeFacts.SelectVolumes(GetMountTable().Entries);

    /// <inheritdoc/>
    public MountTable GetMountTable()
    {
        var entries = new List<MountEntry>();
        foreach (var volumeName in EnumerateVolumeNames())
        {
            var paths = GetMountPaths(volumeName);
            if (paths.Count == 0)
            {
                // Hidden volumes such as recovery partitions have no drive letter or folder.
                continue;
            }

            var primary = paths.Find(WindowsPaths.IsDriveRoot) ?? paths[0];
            var volume = Describe(volumeName, primary, volumeName, remotePath: null).ToVolumeInfo();
            entries.AddRange(paths.Select(path => new MountEntry(path, volume)));
        }

        var covered = new HashSet<string>(entries.Select(static entry => entry.MountPoint), StringComparer.OrdinalIgnoreCase);
        foreach (var drive in GetLogicalDrives().Where(drive => !covered.Contains(drive)))
        {
            if (DescribeOtherDrive(drive) is { } entry)
            {
                entries.Add(entry);
            }
        }

        return new MountTable(entries, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Describes the volume holding a path that the mount table does not cover, such as a network share that is not
    /// mapped to a drive letter.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The volume, or <see langword="null"/> when it cannot be described.</returns>
    public VolumeInfo? QueryVolume(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var buffer = stackalloc char[NameLength];
        if (!Kernel32.GetVolumePathName(path, buffer, NameLength))
        {
            return null;
        }

        var root = WindowsPaths.WithTrailingSeparator(new string(buffer));
        var isNetwork = Kernel32.GetDriveType(root) == WindowsVolumeFacts.DriveRemote;
        var volumeName = isNetwork ? null : GetVolumeName(root);
        var id = volumeName ?? $"network:{root.TrimEnd('\\')}";
        var facts = Describe(volumeName ?? root, root, id, remotePath: isNetwork ? root : null);
        return facts.ToVolumeInfo() with { MountPoint = root.Length > 3 ? root.TrimEnd('\\') : root };
    }

    /// <summary>
    /// Lists the volume GUID paths of every volume on the system.
    /// </summary>
    /// <returns>The volume GUID paths.</returns>
    private static List<string> EnumerateVolumeNames()
    {
        var names = new List<string>();
        var buffer = stackalloc char[NameLength];
        var find = Kernel32.FindFirstVolume(buffer, NameLength);
        if (find == -1)
        {
            Trace.TraceWarning("Could not list volumes: error {0}", Marshal.GetLastPInvokeError());
            return names;
        }

        try
        {
            do
            {
                names.Add(new string(buffer));
            }
            while (Kernel32.FindNextVolume(find, buffer, NameLength));
        }
        finally
        {
            Kernel32.FindVolumeClose(find);
        }

        return names;
    }

    /// <summary>
    /// Lists the drive letters and mounted folders of a volume.
    /// </summary>
    /// <param name="volumeName">The volume GUID path.</param>
    /// <returns>The paths, each ending with a backslash.</returns>
    private static List<string> GetMountPaths(string volumeName)
    {
        var length = (uint)NameLength;
        while (true)
        {
            var buffer = new char[length];
            fixed (char* pointer = buffer)
            {
                if (Kernel32.GetVolumePathNamesForVolumeName(volumeName, pointer, length, out var needed))
                {
                    return WindowsPaths.SplitMultiString(buffer);
                }

                if (Marshal.GetLastPInvokeError() != Win32Errors.MoreData || needed <= length)
                {
                    return [];
                }

                length = needed;
            }
        }
    }

    /// <summary>
    /// Lists the drive roots in use, including network and <c>subst</c> drives.
    /// </summary>
    /// <returns>The roots, such as <c>C:\</c>.</returns>
    private static List<string> GetLogicalDrives()
    {
        var buffer = stackalloc char[NameLength];
        var length = Kernel32.GetLogicalDriveStrings(NameLength, buffer);
        return length is > 0 and < NameLength ? WindowsPaths.SplitMultiString(new ReadOnlySpan<char>(buffer, (int)length)) : [];
    }

    /// <summary>
    /// Describes a drive letter that is not a local volume's mount point: a mapped network drive, or a <c>subst</c>
    /// drive (a partial view of a folder on another volume).
    /// </summary>
    /// <param name="drive">The drive root, such as <c>Z:\</c>.</param>
    /// <returns>The mount entry, or <see langword="null"/>.</returns>
    private static MountEntry? DescribeOtherDrive(string drive)
    {
        var driveType = Kernel32.GetDriveType(drive);
        if (driveType == WindowsVolumeFacts.DriveRemote)
        {
            var remote = GetRemotePath(drive);
            var id = $"network:{remote ?? drive.TrimEnd('\\')}";
            return new MountEntry(drive, Describe(drive, drive, id, remote).ToVolumeInfo());
        }

        var buffer = stackalloc char[NameLength];
        var length = Kernel32.QueryDosDevice(drive[..2], buffer, NameLength);
        var target = length > 0 ? new string(buffer) : null;
        if (target is not null && target.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            // A subst drive: the folder it shows belongs to another volume.
            var folder = target[4..];
            var root = Path.GetPathRoot(folder);
            var volumeName = root is null ? null : GetVolumeName(WindowsPaths.WithTrailingSeparator(root));
            if (volumeName is not null)
            {
                return new MountEntry(drive, Describe(volumeName, WindowsPaths.WithTrailingSeparator(root!), volumeName, remotePath: null).ToVolumeInfo(), IsPartial: true);
            }
        }

        return null;
    }

    /// <summary>
    /// Reads what Windows reports about a volume.
    /// </summary>
    /// <param name="root">The root to query: a volume GUID path, drive root or share root, ending with a backslash.</param>
    /// <param name="mountPoint">The mount point to show the volume at.</param>
    /// <param name="id">The volume's identifier.</param>
    /// <param name="remotePath">The network path of a network drive, or <see langword="null"/>.</param>
    /// <returns>The facts.</returns>
    private static WindowsVolumeFacts Describe(string root, string mountPoint, string id, string? remotePath)
    {
        var label = stackalloc char[NameLength];
        var fileSystem = stackalloc char[NameLength];
        string? labelText = null, fileSystemText = null;
        uint serial = 0, flags = 0;
        if (Kernel32.GetVolumeInformation(root, label, NameLength, out serial, out _, out flags, fileSystem, NameLength))
        {
            labelText = new string(label);
            fileSystemText = new string(fileSystem);
        }

        long total = 0, available = 0;
        if (Kernel32.GetDiskFreeSpaceEx(root, out var freeToCaller, out var totalBytes, out _))
        {
            total = (long)Math.Min(totalBytes, long.MaxValue);
            available = (long)Math.Min(freeToCaller, long.MaxValue);
        }

        var mount = mountPoint.Length > 3 ? mountPoint.TrimEnd('\\') : mountPoint;
        return new WindowsVolumeFacts(id, mount, labelText, fileSystemText, Kernel32.GetDriveType(root), flags, serial, total, available, remotePath);
    }

    /// <summary>
    /// Gets the volume GUID path of a mount point.
    /// </summary>
    /// <param name="mountPoint">The mount point, ending with a backslash.</param>
    /// <returns>The volume GUID path, or <see langword="null"/>.</returns>
    private static string? GetVolumeName(string mountPoint)
    {
        var buffer = stackalloc char[NameLength];
        return Kernel32.GetVolumeNameForVolumeMountPoint(mountPoint, buffer, NameLength) ? new string(buffer) : null;
    }

    /// <summary>
    /// Gets the network path a drive letter is mapped to.
    /// </summary>
    /// <param name="drive">The drive root, such as <c>Z:\</c>.</param>
    /// <returns>The network path, such as <c>\\server\share</c>, or <see langword="null"/>.</returns>
    private static string? GetRemotePath(string drive)
    {
        var buffer = stackalloc char[NameLength];
        var length = (uint)NameLength;
        return Ole32.WNetGetConnection(drive[..2], buffer, ref length) == 0 ? new string(buffer) : null;
    }
}
