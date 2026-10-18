using CommunityToolkit.Mvvm.ComponentModel;
using WhatDisk.Duplicates;
using WhatDisk.Formatting;

namespace WhatDisk.ViewModels;

/// <summary>
/// The Potential duplicate space pane: a size-only estimate, for the folder the chart shows, of the space that copies
/// of large files take beyond one copy each. It follows the scan as it runs.
/// </summary>
/// <param name="sizes">Formats sizes in the units chosen.</param>
public sealed partial class PotentialDuplicatesViewModel(ByteSizeFormatter sizes) : ObservableObject
{
    /// <summary>Formats sizes.</summary>
    private readonly ByteSizeFormatter _sizes = sizes ?? throw new ArgumentNullException(nameof(sizes));

    /// <summary>
    /// Gets the estimated space, such as "3.20 GB", or a dash before a scan.
    /// </summary>
    [ObservableProperty]
    public partial string AmountText { get; private set; } = "—";

    /// <summary>
    /// Gets what the estimate counts, such as "Beyond one copy of each size, among files over 1 MiB in Documents: 12
    /// groups, 30 files."
    /// </summary>
    [ObservableProperty]
    public partial string DetailText { get; private set; } = "Scan a folder to estimate how much space copies of large files take.";

    /// <summary>
    /// Gets how the estimate is made, for the tooltip.
    /// </summary>
    [ObservableProperty]
    public partial string Explanation { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the estimated space as a share of everything in the folder, from 0 to 1, for the bar.
    /// </summary>
    [ObservableProperty]
    public partial double Fraction { get; private set; }

    /// <summary>
    /// Gets <see cref="Fraction"/> as a percentage, such as "33%", shown in the bar.
    /// </summary>
    [ObservableProperty]
    public partial string PercentText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets what the estimate counts and how it is made, for the info button.
    /// </summary>
    [ObservableProperty]
    public partial string InfoText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether there is an estimate to show.
    /// </summary>
    [ObservableProperty]
    public partial bool HasEstimate { get; private set; }

    /// <summary>
    /// Shows an estimate.
    /// </summary>
    /// <param name="estimate">The estimate.</param>
    /// <param name="scopeName">The folder it covers, such as "Documents" or "all scanned volumes".</param>
    /// <param name="minimumText">The length files must exceed, such as "1 MiB".</param>
    /// <param name="scopeBytes">The size of everything in the folder, on disk, for the share the bar shows.</param>
    public void Update(PotentialDuplicateEstimate estimate, string scopeName, string minimumText, long scopeBytes)
    {
        HasEstimate = true;
        Fraction = scopeBytes > 0 ? Math.Clamp((double)estimate.WastedBytes / scopeBytes, 0, 1) : 0;
        PercentText = DisplayFormat.Percent(Fraction);
        AmountText = estimate.Groups == 0 ? "None" : _sizes.Format(estimate.WastedBytes);
        DetailText = estimate.Groups == 0
            ? $"No files over {minimumText} in {scopeName} share a size."
            : $"Beyond one copy of each size, among files over {minimumText} in {scopeName}: "
              + $"{DisplayFormat.Quantity(estimate.Groups, "group", "groups")}, {DisplayFormat.Quantity(estimate.Files, "file", "files")}.";
        Explanation = $"An estimate from file sizes alone: files over {minimumText} that share their size with another file are counted as copies, "
            + "and the space beyond one copy of each size is shown. Files of the same size may still differ; Tools › Find Duplicates compares their contents.";
        InfoText = $"{DetailText} That is {PercentText} of {_sizes.Format(scopeBytes)} in {scopeName}.{Environment.NewLine}{Environment.NewLine}{Explanation}";
    }

    /// <summary>
    /// Clears the estimate, before a scan.
    /// </summary>
    public void Clear()
    {
        HasEstimate = false;
        Fraction = 0;
        PercentText = string.Empty;
        InfoText = string.Empty;
        AmountText = "—";
        DetailText = "Scan a folder to estimate how much space copies of large files take.";
        Explanation = string.Empty;
    }
}
