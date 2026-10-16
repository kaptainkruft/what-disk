namespace WhatDisk.Scanning;

/// <summary>
/// The state of a scan session.
/// </summary>
public enum ScanStatus
{
    /// <summary>The scan is in progress.</summary>
    Running,

    /// <summary>Every reachable directory has been read.</summary>
    Completed,

    /// <summary>The scan was stopped before it finished.</summary>
    Cancelled,

    /// <summary>The scan stopped because of an unexpected internal failure.</summary>
    Failed,
}
