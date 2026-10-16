using System.Collections.Concurrent;

namespace WhatDisk.Scanning;

/// <summary>
/// Remembers files with several hard links so that their data is counted only once per scan.
/// </summary>
internal sealed class HardLinkRegistry
{
    /// <summary>The identities of the multiply-linked files seen so far.</summary>
    private readonly ConcurrentDictionary<FileIdentity, byte> _seen = new();

    /// <summary>
    /// Gets the number of distinct multiply-linked files registered.
    /// </summary>
    public int Count => _seen.Count;

    /// <summary>
    /// Registers a file and reports whether this is the first time it has been seen.
    /// </summary>
    /// <param name="identity">The file's identity.</param>
    /// <returns><see langword="true"/> for the first link to the file; <see langword="false"/> for later links.</returns>
    public bool TryRegister(FileIdentity identity) => _seen.TryAdd(identity, 0);
}
