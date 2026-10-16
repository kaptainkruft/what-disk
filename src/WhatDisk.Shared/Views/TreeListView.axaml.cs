using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using WhatDisk.ViewModels;
using WhatDisk.Views.Controls;

namespace WhatDisk.Views;

/// <summary>
/// The results tree: a sortable, resizable column header above a virtualised list of flattened tree rows.
/// </summary>
public partial class TreeListView : UserControl
{
    /// <summary>
    /// The columns, in display order. The first column fills the space the others leave.
    /// </summary>
    private static readonly TreeColumnDefinition[] Columns =
    [
        new("Name", TreeSortColumn.Name, 0, 220),
        new("% of Disk", TreeSortColumn.Size, 150, 90),
        new("% of Parent", TreeSortColumn.Size, 150, 90),
        new("Size", TreeSortColumn.Size, 92, 64, HorizontalAlignment.Right),
        new("Files", TreeSortColumn.Files, 86, 52, HorizontalAlignment.Right),
        new("Folders", TreeSortColumn.Folders, 78, 52, HorizontalAlignment.Right),
    ];

    /// <summary>The widths shared by the header and every row.</summary>
    private readonly TreeColumnLayout _layout = new(Columns);

    /// <summary>The sort arrows, one per column header; only the sorted column's arrow is visible.</summary>
    private readonly List<Avalonia.Controls.Shapes.Path> _sortArrows = [];

    /// <summary>The view model the view is subscribed to.</summary>
    private MainWindowViewModel? _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="TreeListView"/> class.
    /// </summary>
    public TreeListView()
    {
        InitializeComponent();
        TreeRowPanel.SetColumnLayout(this, _layout);
        BuildHeader();
        RowsList.DoubleTapped += OnRowsDoubleTapped;
        RowsList.KeyDown += OnRowsKeyDown;
    }

    /// <summary>
    /// Scrolls a row into view.
    /// </summary>
    /// <param name="row">The row.</param>
    public void ScrollIntoView(TreeRowViewModel row) => RowsList.ScrollIntoView(row);

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateSortIndicators();
    }

    /// <summary>
    /// Creates the header cells (sort buttons) and the resize grips between them.
    /// </summary>
    private void BuildHeader()
    {
        foreach (var column in Columns)
        {
            var label = new TextBlock
            {
                Text = column.Header,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var arrow = new Avalonia.Controls.Shapes.Path
            {
                Width = 8,
                Height = 5,
                Stretch = Avalonia.Media.Stretch.Fill,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = false,
            };
            arrow.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, arrow.GetResourceObservable("TextSecondaryBrush"));
            _sortArrows.Add(arrow);
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children = { label, arrow },
            };
            var button = new Button
            {
                Content = content,
                HorizontalContentAlignment = column.Alignment,
                Classes = { "column-header" },
            };
            ToolTip.SetTip(button, column.SortColumn is null ? null : $"Sort by {column.Header.ToLowerInvariant()}");
            button.Click += (_, _) =>
            {
                if (column.SortColumn is { } sortColumn)
                {
                    _viewModel?.SortCommand.Execute(sortColumn);
                }
            };
            HeaderPanel.Children.Add(button);
        }

        for (var index = 1; index < Columns.Length; index++)
        {
            var column = index;
            var grip = new Thumb { Classes = { "column-grip" } };
            TreeRowPanel.SetResizeColumn(grip, column);
            grip.DragDelta += (_, e) => _layout.SetWidth(column, _layout.GetWidth(column) - e.Vector.X);
            HeaderPanel.Children.Add(grip);
        }
    }

    /// <summary>
    /// Shows an arrow on the header of the sorted column, pointing down for descending order.
    /// </summary>
    private void UpdateSortIndicators()
    {
        var order = _viewModel?.SortOrder ?? TreeSortOrder.Default;
        var geometry = Avalonia.Media.Geometry.Parse(order.Descending ? "M0,0 L8,0 L4,5 Z" : "M0,5 L8,5 L4,0 Z");
        for (var index = 0; index < Columns.Length; index++)
        {
            var column = Columns[index];
            var isSorted = column.SortColumn == order.Column && (order.Column != TreeSortColumn.Size || index == 3);
            _sortArrows[index].IsVisible = isSorted;
            _sortArrows[index].Data = geometry;
        }
    }

    /// <summary>
    /// Updates the sort indicators when the view model's sort order changes.
    /// </summary>
    /// <param name="sender">The view model.</param>
    /// <param name="e">The property change.</param>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SortOrder))
        {
            UpdateSortIndicators();
        }
    }

    /// <summary>
    /// Expands or collapses the row that was double-clicked.
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The event.</param>
    private void OnRowsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is TreeRowViewModel row)
        {
            _viewModel?.ToggleExpansionCommand.Execute(row);
        }
    }

    /// <summary>
    /// Implements tree keyboard navigation: Right expands or moves to the first child, Left collapses or moves to the
    /// parent, and Enter toggles expansion.
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The key event.</param>
    private void OnRowsKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel?.SelectedRow is not { } row)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Right when row is { IsExpandable: true, IsExpanded: false }:
                row.IsExpanded = true;
                e.Handled = true;
                break;
            case Key.Right when row is { IsExpanded: true, ChildRows: [var first, ..] }:
                Select(first);
                e.Handled = true;
                break;
            case Key.Left when row.IsExpanded:
                row.IsExpanded = false;
                e.Handled = true;
                break;
            case Key.Left when row.Parent is { } parent:
                Select(parent);
                e.Handled = true;
                break;
            case Key.Enter:
                _viewModel.ToggleExpansionCommand.Execute(row);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Selects a row and brings it into view.
    /// </summary>
    /// <param name="row">The row.</param>
    private void Select(TreeRowViewModel row)
    {
        _viewModel?.SelectedRow = row;
        RowsList.ScrollIntoView(row);
        (RowsList.ContainerFromItem(row) as ListBoxItem)?.Focus();
    }
}
