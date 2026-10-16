namespace WhatDisk.Scanning;

/// <summary>
/// Classifies failures encountered while scanning.
/// </summary>
public enum ScanErrorKind
{
    /// <summary>The failure could not be classified.</summary>
    Unknown,

    /// <summary>Permission to read the directory was denied.</summary>
    AccessDenied,

    /// <summary>The directory disappeared before it could be read.</summary>
    NotFound,

    /// <summary>The path exceeds a platform limit.</summary>
    PathTooLong,

    /// <summary>A network share became unavailable or timed out.</summary>
    Network,

    /// <summary>Any other input/output failure.</summary>
    IO,
}
