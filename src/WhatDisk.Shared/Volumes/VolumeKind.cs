namespace WhatDisk.Volumes;

/// <summary>
/// Describes the broad category of storage that backs a volume.
/// </summary>
public enum VolumeKind
{
    /// <summary>The kind could not be determined.</summary>
    Unknown,

    /// <summary>A fixed, internal disk.</summary>
    Fixed,

    /// <summary>A removable disk such as a USB drive or memory card.</summary>
    Removable,

    /// <summary>A network share (SMB, NFS, AFP, WebDAV and similar).</summary>
    Network,

    /// <summary>An optical disc.</summary>
    Optical,

    /// <summary>A RAM-backed file system such as tmpfs.</summary>
    Ram,

    /// <summary>
    /// A virtual or pseudo file system (procfs, devfs, autofs, snapshots and similar) that does not
    /// represent independently stored data and is therefore never traversed implicitly.
    /// </summary>
    Virtual,
}
