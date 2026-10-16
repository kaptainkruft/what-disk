namespace WhatDisk.Scanning;

/// <summary>
/// Starts scans.
/// </summary>
public interface IDiskScanner
{
    /// <summary>
    /// Starts scanning in the background.
    /// </summary>
    /// <param name="request">What to scan.</param>
    /// <returns>The running scan session.</returns>
    ScanSession Start(ScanRequest request);
}
