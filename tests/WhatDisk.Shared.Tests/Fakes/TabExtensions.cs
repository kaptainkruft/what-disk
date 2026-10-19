using WhatDisk.ViewModels;

namespace WhatDisk.Tests.Fakes;

/// <summary>
/// Finds the main window's tabs of a kind, for tests.
/// </summary>
internal static class TabExtensions
{
    /// <summary>
    /// Gets the tabs of the searches of the selection, in order; the Duplicates tab, which is always there, is left
    /// out.
    /// </summary>
    /// <param name="viewModel">The main window's view model.</param>
    /// <returns>The tabs.</returns>
    public static IReadOnlyList<DuplicateResultsViewModel> DuplicateTabs(this MainWindowViewModel viewModel) =>
        [.. viewModel.Tabs.OfType<DuplicateResultsViewModel>().Where(tab => !ReferenceEquals(tab, viewModel.DuplicatesTab))];
}
