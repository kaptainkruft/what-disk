namespace WhatDisk.Scanning;

/// <summary>
/// Describes what to scan.
/// </summary>
public sealed class ScanRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScanRequest"/> class.
    /// </summary>
    /// <param name="targets">The paths to scan: volume roots, directories or network shares.</param>
    /// <param name="options">How to scan; <see langword="null"/> for <see cref="ScanOptions.Default"/>.</param>
    public ScanRequest(IEnumerable<string> targets, ScanOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        Targets = [.. targets.Where(static target => !string.IsNullOrWhiteSpace(target))];
        if (Targets.Count == 0)
        {
            throw new ArgumentException("At least one path to scan is required.", nameof(targets));
        }

        Options = options ?? ScanOptions.Default;
    }

    /// <summary>
    /// Gets the paths to scan.
    /// </summary>
    public IReadOnlyList<string> Targets { get; }

    /// <summary>
    /// Gets how to scan.
    /// </summary>
    public ScanOptions Options { get; }
}
