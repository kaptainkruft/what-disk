namespace WhatDisk.ViewModels;

/// <summary>
/// What the Duplicates tab, which always covers the whole scan, needs from the main window: whether a search can start,
/// running and stopping a sampled scan of every scanned file, and whether files no longer than the minimum length are
/// included.
/// </summary>
public interface IDuplicateSearchHost
{
    /// <summary>
    /// Gets a value indicating whether files can be compared now: a scan has finished (or was stopped) with results,
    /// and nothing else runs.
    /// </summary>
    bool CanSearch { get; }

    /// <summary>
    /// Gets the length, in bytes, that files must exceed to be listed unless smaller files are included; zero lists
    /// every non-empty file.
    /// </summary>
    long SmallFileLimit { get; }

    /// <summary>
    /// Gets a value indicating whether files no longer than <see cref="SmallFileLimit"/> are listed too.
    /// </summary>
    bool IncludesSmallFiles { get; }

    /// <summary>
    /// Compares the files that share a size across the whole scan (smaller files by hash, larger ones by samples) and
    /// shows the result in the tab.
    /// </summary>
    /// <returns>A task that completes when the search has finished or was stopped.</returns>
    Task SampledScanAsync();

    /// <summary>
    /// Stops the running sampled scan.
    /// </summary>
    void StopSearch();

    /// <summary>
    /// Includes or leaves out files no longer than <see cref="SmallFileLimit"/>, asking first before including them,
    /// and lists the files that share a size again.
    /// </summary>
    /// <param name="include">Whether to include them.</param>
    /// <returns>A task that completes when the choice has been made and applied.</returns>
    Task SetIncludeSmallFilesAsync(bool include);
}
