namespace WhatDisk.Scanning;

/// <summary>
/// Additional facts about a directory entry. Enumerators set the flags they can determine cheaply.
/// </summary>
[Flags]
public enum EntryAttributes : ushort
{
    /// <summary>No additional attributes.</summary>
    None = 0,

    /// <summary>The entry is hidden from normal directory listings.</summary>
    Hidden = 1 << 0,

    /// <summary>The entry is marked as an operating system file.</summary>
    System = 1 << 1,

    /// <summary>The entry is read-only.</summary>
    ReadOnly = 1 << 2,

    /// <summary>The entry is a Windows reparse point that is not a link (for example a cloud-synchronised folder).</summary>
    ReparsePoint = 1 << 3,

    /// <summary>The entry is stored compressed.</summary>
    Compressed = 1 << 4,

    /// <summary>The entry is a sparse file.</summary>
    Sparse = 1 << 5,

    /// <summary>The entry's data is not stored locally (a cloud placeholder or dataless file).</summary>
    Offline = 1 << 6,

    /// <summary>Another file system is mounted on this directory.</summary>
    MountPoint = 1 << 7,

    /// <summary>The directory is a macOS firmlink into another volume of the same volume group.</summary>
    Firmlink = 1 << 8,

    /// <summary>The directory triggers an automount when accessed and must not be traversed implicitly.</summary>
    AutomountTrigger = 1 << 9,
}
