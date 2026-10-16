namespace WhatDisk.ViewModels;

/// <summary>
/// Orders sibling rows by a <see cref="TreeSortOrder"/>, breaking ties by name. Rows are compared by the values they
/// captured during their last refresh, so the order is consistent even while the scan changes the underlying totals.
/// </summary>
/// <param name="order">The sort order.</param>
internal sealed class TreeRowComparer(TreeSortOrder order) : IComparer<TreeRowViewModel>
{
    /// <summary>
    /// Gets the sort order.
    /// </summary>
    public TreeSortOrder Order { get; } = order;

    /// <inheritdoc/>
    public int Compare(TreeRowViewModel? x, TreeRowViewModel? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null || y is null)
        {
            return x is null ? -1 : 1;
        }

        var result = Order.Column switch
        {
            TreeSortColumn.Name => CompareNames(x, y),
            TreeSortColumn.Files => x.FileCount.CompareTo(y.FileCount),
            TreeSortColumn.Folders => x.FolderCount.CompareTo(y.FolderCount),
            _ => x.Size.CompareTo(y.Size),
        };

        if (Order.Descending)
        {
            result = -result;
        }

        return result != 0 ? result : CompareNames(x, y);
    }

    /// <summary>
    /// Compares two rows by name, ignoring case.
    /// </summary>
    /// <param name="x">The first row.</param>
    /// <param name="y">The second row.</param>
    /// <returns>The comparison result.</returns>
    private static int CompareNames(TreeRowViewModel x, TreeRowViewModel y) =>
        StringComparer.CurrentCultureIgnoreCase.Compare(x.Name, y.Name);
}
