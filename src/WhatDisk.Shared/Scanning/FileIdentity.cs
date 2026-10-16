namespace WhatDisk.Scanning;

/// <summary>
/// Identifies a file system object: the device (volume) it lives on and its file number within that device
/// (the inode number on Unix-like systems).
/// </summary>
/// <param name="Device">The device or volume number.</param>
/// <param name="FileId">The file number within the device; zero means unknown.</param>
public readonly record struct FileIdentity(ulong Device, ulong FileId)
{
    /// <summary>
    /// Gets an identity that carries no information.
    /// </summary>
    public static FileIdentity Unknown => default;

    /// <summary>
    /// Gets a value indicating whether this identity carries real information.
    /// </summary>
    public bool IsKnown => FileId != 0;
}
