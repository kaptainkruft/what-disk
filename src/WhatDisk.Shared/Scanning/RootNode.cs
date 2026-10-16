using WhatDisk.Volumes;

namespace WhatDisk.Scanning;

/// <summary>
/// A top-level node of the scan tree: a scanned path, or a volume discovered beneath a scanned path.
/// </summary>
public sealed class RootNode : DirectoryNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RootNode"/> class.
    /// </summary>
    /// <param name="path">The absolute path that is scanned.</param>
    /// <param name="volume">The volume the path is stored on.</param>
    /// <param name="identity">The identity of the directory, when known.</param>
    /// <param name="isVolumeRoot">Whether <paramref name="path"/> is the root of <paramref name="volume"/>.</param>
    /// <param name="isDiscovered">Whether the root was discovered beneath another root rather than selected by the user.</param>
    internal RootNode(string path, VolumeInfo volume, FileIdentity identity, bool isVolumeRoot, bool isDiscovered)
        : base(CreateDisplayName(path, volume, isVolumeRoot), null, volume, identity, null)
    {
        RootPath = path;
        IsVolumeRoot = isVolumeRoot;
        IsDiscovered = isDiscovered;
    }

    /// <inheritdoc/>
    public override NodeKind Kind => IsVolumeRoot ? NodeKind.Volume : NodeKind.Directory;

    /// <summary>
    /// Gets the absolute path that is scanned.
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="RootPath"/> is the root of the volume.
    /// </summary>
    public bool IsVolumeRoot { get; }

    /// <summary>
    /// Gets a value indicating whether the root was discovered beneath another root (a volume mounted inside the
    /// scanned tree) rather than selected by the user.
    /// </summary>
    public bool IsDiscovered { get; }

    /// <inheritdoc/>
    private protected override string TopLevelPath => RootPath;

    /// <summary>
    /// Builds the display name of a root: the volume name for a volume root, otherwise the path itself.
    /// </summary>
    /// <param name="path">The scanned path.</param>
    /// <param name="volume">The volume the path is stored on.</param>
    /// <param name="isVolumeRoot">Whether the path is the root of the volume.</param>
    /// <returns>The display name.</returns>
    private static string CreateDisplayName(string path, VolumeInfo volume, bool isVolumeRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(volume);
        return isVolumeRoot && !string.IsNullOrWhiteSpace(volume.Name) ? volume.Name : path;
    }
}
