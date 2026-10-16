namespace WhatDisk.Scanning;

/// <summary>
/// Explains why a directory was not traversed in place.
/// </summary>
public enum SkipReason
{
    /// <summary>The directory belongs to another volume that is listed as its own top-level item.</summary>
    SeparateVolume,

    /// <summary>The directory exposes data that is already counted elsewhere in this scan (for example a bind mount).</summary>
    CountedElsewhere,

    /// <summary>The directory is a virtual or pseudo file system, or an automount trigger.</summary>
    VirtualFileSystem,

    /// <summary>The directory is a mounted network volume, which is only scanned when selected directly.</summary>
    NetworkVolume,
}
