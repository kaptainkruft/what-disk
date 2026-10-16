using CommunityToolkit.Mvvm.ComponentModel;
using WhatDisk.Formatting;
using WhatDisk.Volumes;

namespace WhatDisk.ViewModels;

/// <summary>
/// A volume offered in the volume picker.
/// </summary>
/// <param name="volume">The volume.</param>
/// <param name="sizes">Formats byte counts.</param>
public sealed partial class VolumeItemViewModel(VolumeInfo volume, ByteSizeFormatter sizes) : ObservableObject
{
    /// <summary>
    /// Gets the volume.
    /// </summary>
    public VolumeInfo Volume { get; } = volume ?? throw new ArgumentNullException(nameof(volume));

    /// <summary>
    /// Gets the volume's display name.
    /// </summary>
    public string Name => Volume.Name;

    /// <summary>
    /// Gets the volume's mount point.
    /// </summary>
    public string MountPoint => Volume.MountPoint;

    /// <summary>
    /// Gets a summary of free and total space, or the file system name when the capacity is unknown.
    /// </summary>
    public string CapacityText => Volume.TotalBytes > 0
        ? $"{sizes.Format(Volume.AvailableBytes)} free of {sizes.Format(Volume.TotalBytes)}"
        : Volume.FileSystem ?? string.Empty;

    /// <summary>
    /// Gets the fraction of the volume in use, from 0 to 1.
    /// </summary>
    public double UsedFraction => Volume.UsedFraction;

    /// <summary>
    /// Gets the formatted fraction of the volume in use.
    /// </summary>
    public string UsedPercentText => Volume.TotalBytes > 0 ? DisplayFormat.Percent(UsedFraction) : string.Empty;

    /// <summary>
    /// Gets a value indicating whether the volume is a network share.
    /// </summary>
    public bool IsNetwork => Volume.Kind == VolumeKind.Network;

    /// <summary>
    /// Gets or sets a value indicating whether the volume is selected for a multi-volume scan.
    /// </summary>
    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}
