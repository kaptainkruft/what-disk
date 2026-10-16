using Avalonia.Layout;
using WhatDisk.ViewModels;

namespace WhatDisk.Views.Controls;

/// <summary>
/// Describes one column of the tree list.
/// </summary>
/// <param name="Header">The header text.</param>
/// <param name="SortColumn">The sort column the header selects, or <see langword="null"/> when it does not sort.</param>
/// <param name="Width">The initial width; ignored for the first column, which fills the remaining space.</param>
/// <param name="MinWidth">The minimum width.</param>
/// <param name="Alignment">How the header text is aligned.</param>
public sealed record TreeColumnDefinition(
    string Header,
    TreeSortColumn? SortColumn,
    double Width,
    double MinWidth,
    HorizontalAlignment Alignment = HorizontalAlignment.Left);
