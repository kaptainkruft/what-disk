namespace WhatDisk.Scanning;

/// <summary>
/// The kind of a node in the scan tree.
/// </summary>
public enum NodeKind
{
    /// <summary>A regular file.</summary>
    File,

    /// <summary>A directory.</summary>
    Directory,

    /// <summary>The root of a volume.</summary>
    Volume,

    /// <summary>A symbolic link or junction; links are listed but never followed.</summary>
    SymbolicLink,

    /// <summary>
    /// A directory where another volume is mounted or linked. Its contents are either listed under a separate
    /// top-level item or deliberately not scanned.
    /// </summary>
    MountPoint,

    /// <summary>A device, socket, pipe or other special file.</summary>
    Special,
}
