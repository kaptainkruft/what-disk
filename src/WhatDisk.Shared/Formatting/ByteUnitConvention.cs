namespace WhatDisk.Formatting;

/// <summary>
/// How byte counts are scaled into larger units, following the convention of the host platform.
/// </summary>
public enum ByteUnitConvention
{
    /// <summary>Powers of 1000, as used by macOS Finder and most Linux desktops (1 KB = 1000 bytes).</summary>
    PowersOf1000,

    /// <summary>Powers of 1024 labelled KB, MB, GB, as used by Windows Explorer (1 KB = 1024 bytes).</summary>
    PowersOf1024,
}
