namespace WhatDisk.Volumes;

/// <summary>
/// Provides information about the volumes mounted on the machine. Each platform supplies its own implementation.
/// </summary>
public interface IVolumeService
{
    /// <summary>
    /// Gets the volumes a user would reasonably want to scan, in the order they should be displayed.
    /// </summary>
    /// <returns>The selectable volumes; never <see langword="null"/>.</returns>
    IReadOnlyList<VolumeInfo> GetSelectableVolumes();

    /// <summary>
    /// Captures a snapshot of every mounted file system, including virtual ones, for use while scanning.
    /// </summary>
    /// <returns>The current mount table.</returns>
    MountTable GetMountTable();

    /// <summary>
    /// Queries the volume that contains <paramref name="path"/> directly. Used for paths that the mount table does
    /// not describe, such as Windows UNC shares.
    /// </summary>
    /// <param name="path">An absolute path.</param>
    /// <returns>The volume, or <see langword="null"/> when it cannot be determined.</returns>
    VolumeInfo? QueryVolume(string path);
}
