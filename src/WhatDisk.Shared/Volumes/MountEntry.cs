namespace WhatDisk.Volumes;

/// <summary>
/// Describes one entry of the mount table: a volume mounted at a directory.
/// </summary>
/// <param name="MountPoint">The directory on which the file system is mounted.</param>
/// <param name="Volume">The volume that is mounted there.</param>
/// <param name="IsPartial">
/// <see langword="true"/> when the mount exposes only part of the file system (for example a Linux bind mount of a
/// sub-directory) rather than its root.
/// </param>
public sealed record MountEntry(string MountPoint, VolumeInfo Volume, bool IsPartial = false);
