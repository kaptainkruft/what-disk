using WhatDisk.Formatting;

namespace WhatDisk.Platform;

/// <summary>
/// Describes platform conventions that shape the user interface.
/// </summary>
public sealed record PlatformCapabilities
{
    /// <summary>
    /// Gets the name of the platform, for example <c>Windows</c>.
    /// </summary>
    public required string PlatformName { get; init; }

    /// <summary>
    /// Gets a value indicating whether several volumes can be selected for one scan (as with drive letters on Windows).
    /// </summary>
    public bool SupportsMultipleVolumeSelection { get; init; }

    /// <summary>
    /// Gets the hint shown in the empty path box, with examples of paths the platform accepts.
    /// </summary>
    public required string PathPlaceholder { get; init; }

    /// <summary>
    /// Gets how paths are compared on this platform.
    /// </summary>
    public StringComparison PathComparison { get; init; } = StringComparison.Ordinal;

    /// <summary>
    /// Gets the platform's convention for byte units.
    /// </summary>
    public ByteUnitConvention ByteUnits { get; init; } = ByteUnitConvention.PowersOf1000;

    /// <summary>
    /// Gets the platform's word for a directory, for example <c>Folder</c>.
    /// </summary>
    public string DirectoryNoun { get; init; } = "Folder";

    /// <summary>
    /// Gets capabilities suitable for an unidentified platform.
    /// </summary>
    public static PlatformCapabilities Generic { get; } = new()
    {
        PlatformName = "Generic",
        PathPlaceholder = "Enter a path to scan",
    };
}
