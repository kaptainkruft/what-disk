namespace WhatDisk.Scanning;

/// <summary>
/// The type of a directory entry as reported by a file system enumerator.
/// </summary>
public enum EntryType : byte
{
    /// <summary>A regular file.</summary>
    File,

    /// <summary>A directory that can be traversed.</summary>
    Directory,

    /// <summary>A symbolic link, junction or other name surrogate. Links are never followed.</summary>
    SymbolicLink,

    /// <summary>Anything else: devices, sockets, pipes and similar special files.</summary>
    Other,
}
