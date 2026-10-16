using System.Collections.Frozen;

namespace WhatDisk.Volumes;

/// <summary>
/// Classifies file system format names (as reported by mount tables) into broad <see cref="VolumeKind"/> categories.
/// </summary>
public static class FileSystemClassifier
{
    /// <summary>File systems that expose kernel or runtime state, or duplicate data stored elsewhere.</summary>
    private static readonly FrozenSet<string> VirtualFileSystems = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "proc", "sysfs", "devtmpfs", "devpts", "devfs", "fdesc", "cgroup", "cgroup2", "securityfs", "pstore",
        "debugfs", "tracefs", "configfs", "fusectl", "mqueue", "hugetlbfs", "bpf", "autofs", "binfmt_misc",
        "efivarfs", "rpc_pipefs", "nsfs", "selinuxfs", "overlay", "squashfs", "nullfs", "fuse.portal",
        "fuse.lxcfs", "kernfs");

    /// <summary>File systems that are served over a network.</summary>
    private static readonly FrozenSet<string> NetworkFileSystems = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "nfs", "nfs4", "cifs", "smb3", "smbfs", "afpfs", "webdav", "ftp", "sshfs", "fuse.sshfs", "fuse.rclone",
        "9p", "ceph", "glusterfs", "fuse.glusterfs", "afs", "davfs", "fuse.gvfsd-fuse", "fuse.s3fs", "lustre");

    /// <summary>File systems whose contents live in memory.</summary>
    private static readonly FrozenSet<string> RamFileSystems = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "tmpfs", "ramfs");

    /// <summary>File systems used on optical media.</summary>
    private static readonly FrozenSet<string> OpticalFileSystems = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase, "iso9660", "udf", "cd9660", "cddafs");

    /// <summary>
    /// Classifies a file system format name.
    /// </summary>
    /// <param name="fileSystem">The format name, such as <c>ext4</c>, <c>smbfs</c> or <c>proc</c>.</param>
    /// <returns>
    /// The kind implied by the format, or <see langword="null"/> when the format is an ordinary disk file system whose
    /// kind (fixed or removable) must be determined some other way.
    /// </returns>
    public static VolumeKind? Classify(string? fileSystem)
    {
        if (string.IsNullOrEmpty(fileSystem))
        {
            return null;
        }

        if (VirtualFileSystems.Contains(fileSystem))
        {
            return VolumeKind.Virtual;
        }

        if (NetworkFileSystems.Contains(fileSystem))
        {
            return VolumeKind.Network;
        }

        if (RamFileSystems.Contains(fileSystem))
        {
            return VolumeKind.Ram;
        }

        return OpticalFileSystems.Contains(fileSystem) ? VolumeKind.Optical : null;
    }
}
