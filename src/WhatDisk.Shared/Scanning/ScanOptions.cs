namespace WhatDisk.Scanning;

/// <summary>
/// Tunes how a scan runs.
/// </summary>
public sealed record ScanOptions
{
    /// <summary>
    /// Gets the default number of worker threads: the processor count, clamped to a range that keeps local disks
    /// busy without thrashing them.
    /// </summary>
    public static int DefaultParallelism { get; } = Math.Clamp(Environment.ProcessorCount, 4, 16);

    /// <summary>
    /// Gets the default options.
    /// </summary>
    public static ScanOptions Default { get; } = new();

    /// <summary>
    /// Gets the number of worker threads that read directories concurrently (1 to 256).
    /// </summary>
    public int MaxDegreeOfParallelism
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 256);
            field = value;
        }
    } = DefaultParallelism;

    /// <summary>
    /// Gets a value indicating whether network volumes mounted beneath a scanned path are scanned too. They are
    /// skipped by default because they are usually slow and rarely what the user meant.
    /// </summary>
    public bool IncludeNetworkVolumes { get; init; }

    /// <summary>
    /// Gets the maximum number of individual errors that are recorded; further errors are only counted.
    /// </summary>
    public int MaxRecordedErrors
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 1000;
}
