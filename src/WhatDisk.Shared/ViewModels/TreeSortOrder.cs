namespace WhatDisk.ViewModels;

/// <summary>
/// The columns the tree can be sorted by.
/// </summary>
public enum TreeSortColumn
{
    /// <summary>Sort by name.</summary>
    Name,

    /// <summary>Sort by counted size (which also orders both percentage columns).</summary>
    Size,

    /// <summary>Sort by number of files.</summary>
    Files,

    /// <summary>Sort by number of folders.</summary>
    Folders,
}

/// <summary>
/// How the siblings at every level of the tree are ordered.
/// </summary>
/// <param name="Column">The column to sort by.</param>
/// <param name="Descending">Whether larger values (or later names) come first.</param>
public readonly record struct TreeSortOrder(TreeSortColumn Column, bool Descending)
{
    /// <summary>
    /// Gets the default order: largest first.
    /// </summary>
    public static TreeSortOrder Default => new(TreeSortColumn.Size, Descending: true);

    /// <summary>
    /// Returns the order that results from clicking a column header: the same column reverses direction; a new column
    /// starts descending for numbers and ascending for names.
    /// </summary>
    /// <param name="column">The column clicked.</param>
    /// <returns>The new order.</returns>
    public TreeSortOrder Toggle(TreeSortColumn column) =>
        column == Column ? this with { Descending = !Descending } : new TreeSortOrder(column, column != TreeSortColumn.Name);
}
