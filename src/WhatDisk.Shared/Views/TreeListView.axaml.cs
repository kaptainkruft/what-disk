using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhatDisk.FileOperations;
using WhatDisk.Platform;
using WhatDisk.ViewModels;
using WhatDisk.Views.Controls;

namespace WhatDisk.Views;

/// <summary>
/// The results tree: a sortable, resizable column header above a virtualised list of flattened tree rows, with
/// multiple selection, a context menu, keyboard shortcuts and drag and drop to move or copy items between folders.
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

    /// <summary>The in-process data format carrying dragged rows.</summary>
    internal static readonly DataFormat<TreeRowViewModel[]> RowsFormat = DataFormat.CreateInProcessFormat<TreeRowViewModel[]>("WhatDisk.TreeRows");

    /// <summary>How far, in device-independent pixels, the pointer must move with the button down to start a drag.</summary>
    private const double DragThreshold = 4;

    /// <summary>How close, in device-independent pixels, to the top or bottom edge a drag scrolls the list.</summary>
    private const double AutoScrollMargin = 28;

    /// <summary>How long a drag must hover over a collapsed folder before it opens.</summary>
    private static readonly TimeSpan SpringLoadDelay = TimeSpan.FromSeconds(1);

    /// <summary>The view model the view is subscribed to.</summary>
    private MainWindowViewModel? _viewModel;

    /// <summary>The window whose focus changes are tracked.</summary>
    private TopLevel? _topLevel;

    /// <summary>The context menu's Open With submenu, filled when the menu opens.</summary>
    private MenuItem? _openWithItem;

    /// <summary>The context menu items that depend on a platform capability.</summary>
    private readonly List<(MenuItem Item, ShellCapabilities Capability)> _capabilityItems = [];

    /// <summary>The press that may start a drag, or <see langword="null"/>.</summary>
    private PointerPressedEventArgs? _pressArgs;

    /// <summary>Where the press that may start a drag happened, relative to the list.</summary>
    private Point _pressPosition;

    /// <summary>The row pressed, which may start a drag.</summary>
    private TreeRowViewModel? _pressRow;

    /// <summary>
    /// Whether releasing the pointer without dragging selects only the pressed row: pressing a row of a multiple
    /// selection keeps the selection, so that the whole selection can be dragged.
    /// </summary>
    private bool _selectOnRelease;

    /// <summary>The row highlighted as the drop target.</summary>
    private TreeRowViewModel? _dropTarget;

    /// <summary>The row the drag is hovering over, which opens after <see cref="SpringLoadDelay"/>.</summary>
    private TreeRowViewModel? _hoverRow;

    /// <summary>When the drag started hovering over <see cref="_hoverRow"/>.</summary>
    private DateTime _hoverStarted;

    /// <summary>The latest drag position, relative to the list.</summary>
    private Point _dragPosition;

    /// <summary>Opens hovered folders and scrolls the list while a drag is over it.</summary>
    private readonly DispatcherTimer _dragTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };

    /// <summary>
    /// Initializes a new instance of the <see cref="TreeListView"/> class.
    /// </summary>
    public TreeListView()
    {
        InitializeComponent();
        TreeRowPanel.SetColumnLayout(this, _layout);
        BuildHeader();
        RowsList.DoubleTapped += OnRowsDoubleTapped;
        // Tunnelling: the list's scroll viewer would otherwise consume Left and Right (horizontal scrolling) before a
        // bubbling handler could see them. Up and Down are left to the list.
        RowsList.AddHandler(KeyDownEvent, OnRowsKeyDown, RoutingStrategies.Tunnel);
        RowsList.AddHandler(PointerPressedEvent, OnRowsPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        RowsList.AddHandler(PointerMovedEvent, OnRowsPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        RowsList.AddHandler(PointerReleasedEvent, OnRowsPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        // Avalonia raises DragEnter rather than DragOver when the pointer moves onto a different element.
        RowsList.AddHandler(DragDrop.DragEnterEvent, OnRowsDragOver);
        RowsList.AddHandler(DragDrop.DragOverEvent, OnRowsDragOver);
        RowsList.AddHandler(DragDrop.DragLeaveEvent, OnRowsDragLeave);
        RowsList.AddHandler(DragDrop.DropEvent, OnRowsDrop);
        _dragTimer.Tick += OnDragTimerTick;
    }

    /// <summary>
    /// Opens the context menu for the selection, below the focused row (or the first selected row).
    /// </summary>
    /// <returns><see langword="true"/> when the menu opened.</returns>
    public bool OpenContextMenu()
    {
        if (RowsList.ContextMenu is not { } menu || _viewModel?.SelectedRows is not [var first, ..] || !PrepareContextMenu())
        {
            return false;
        }

        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as ListBoxItem;
        var anchor = focused is { DataContext: TreeRowViewModel { } row } && _viewModel.Selection.SelectedItems.Contains(row)
            ? focused
            : RowsList.ContainerFromItem(first);
        // Shown below the row rather than at the pointer; the placement returns to the pointer when the menu closes.
        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.BottomEdgeAlignedLeft;
        menu.Open(RowsList);
        return true;
    }

    /// <summary>
    /// Scrolls a row into view.
    /// </summary>
    /// <param name="row">The row.</param>
    public void ScrollIntoView(TreeRowViewModel row) => RowsList.ScrollIntoView(row);

    /// <summary>
    /// Scrolls a row into view and gives it keyboard focus. When the row's container is not created yet, focusing
    /// waits until the list has been laid out.
    /// </summary>
    /// <param name="row">The row.</param>
    public void FocusRow(TreeRowViewModel row)
    {
        RowsList.ScrollIntoView(row);
        if (RowsList.ContainerFromItem(row) is { } container)
        {
            container.Focus(NavigationMethod.Directional);
            return;
        }

        Dispatcher.UIThread.Post(() => RowsList.ContainerFromItem(row)?.Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
    }

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
            _viewModel.IsTreeActive = RowsList.IsKeyboardFocusWithin;
        }

        RowsList.ContextMenu = _viewModel is null ? null : BuildContextMenu(_viewModel);
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
    /// Creates the context menu for tree items. Its shortcuts are shown for reference; the tree's key handling and the
    /// menu bar carry them out.
    /// </summary>
    /// <param name="viewModel">The view model whose commands the menu runs.</param>
    /// <returns>The menu.</returns>
    private ContextMenu BuildContextMenu(MainWindowViewModel viewModel)
    {
        _capabilityItems.Clear();
        _openWithItem = new MenuItem { Header = "Open With" };
        _capabilityItems.Add((_openWithItem, ShellCapabilities.OpenWith));
        var platform = viewModel.Platform;
        var keys = platform.Shortcuts;
        var menu = new ContextMenu
        {
            Items =
            {
                Item("Open", viewModel.OpenCommand, keys.Open, ShellCapabilities.Open),
                _openWithItem,
                new Separator(),
                Item($"Show in {platform.FileManagerName}", viewModel.RevealCommand, keys.Reveal, ShellCapabilities.Reveal),
                Item("Quick Look", viewModel.QuickLookCommand, keys.QuickLook, ShellCapabilities.QuickLook),
                Item(platform.InfoCommandName, viewModel.GetInfoCommand, keys.GetInfo, ShellCapabilities.ShowInfo),
                new Separator(),
                Item("Copy", viewModel.CopyItemsCommand, keys.Copy, ShellCapabilities.CopyItems),
                Item("Copy Path", viewModel.CopyPathCommand, keys.CopyPath, ShellCapabilities.CopyText),
                new Separator(),
                Item("Expand All Beneath", viewModel.ExpandAllCommand, keys.ExpandAll),
                Item("Collapse All Beneath", viewModel.CollapseAllCommand, keys.CollapseAll),
                Item("Rescan", viewModel.RescanCommand, keys.Rescan),
                new Separator(),
                Item($"Move to {platform.TrashName}", viewModel.MoveToTrashCommand, keys.MoveToTrash, ShellCapabilities.Trash),
                Item($"{platform.PermanentDeleteName}\u2026", viewModel.DeletePermanentlyCommand, keys.DeletePermanently),
            },
        };
        menu.Opening += OnContextMenuOpening;
        menu.Closed += (_, _) =>
        {
            menu.PlacementTarget = null;
            menu.Placement = PlacementMode.Pointer;
        };
        return menu;

        MenuItem Item(string header, ICommand command, KeyGesture? gesture, ShellCapabilities? capability = null)
        {
            var item = new MenuItem { Header = header, Command = command, InputGesture = gesture };
            if (capability is { } required)
            {
                _capabilityItems.Add((item, required));
            }

            return item;
        }
    }

    /// <summary>
    /// Prepares the context menu when it opens from a right-click; it does not open when nothing is selected.
    /// </summary>
    /// <param name="sender">The menu.</param>
    /// <param name="e">Allows the opening to be cancelled.</param>
    private void OnContextMenuOpening(object? sender, CancelEventArgs e) => e.Cancel = !PrepareContextMenu();

    /// <summary>
    /// Prepares the context menu: hides actions the platform lacks and lists the applications for Open With.
    /// </summary>
    /// <returns><see langword="false"/> when nothing is selected, so the menu should not open.</returns>
    private bool PrepareContextMenu()
    {
        if (_viewModel is not { } viewModel || viewModel.Selection.Count == 0)
        {
            return false;
        }

        // Opening the menu moves focus to it; the commands stay enabled because the tree is still the active pane.
        viewModel.IsTreeActive = true;
        foreach (var (item, capability) in _capabilityItems)
        {
            item.IsVisible = (viewModel.ShellCapabilities & capability) == capability;
        }

        if (_openWithItem is { IsVisible: true } openWith)
        {
            openWith.Items.Clear();
            var choices = viewModel.GetOpenWithChoices();
            foreach (var choice in choices)
            {
                openWith.Items.Add(new MenuItem
                {
                    Header = choice.IsDefault ? $"{choice.Name} (default)" : choice.Name,
                    Command = viewModel.OpenWithCommand,
                    CommandParameter = choice,
                });
            }

            if (choices.Count == 0)
            {
                openWith.Items.Add(new MenuItem { Header = "No Applications", IsEnabled = false });
            }
        }

        return true;
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(GotFocusEvent, OnWindowGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _topLevel?.RemoveHandler(GotFocusEvent, OnWindowGotFocus);
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Tracks whether the tree is the active pane as keyboard focus moves around the window: focus in the tree makes it
    /// active, focus in another control (such as the path box) makes it inactive, and focus in a menu (including a
    /// menu bar drawn in the window) leaves it as it was, so the menu's commands still apply to the selection. Focus
    /// in menus, dialogs and other windows outside this window does not reach here, and also leaves it as it was.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event.</param>
    private void OnWindowGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (_viewModel is not { } viewModel || e.Source is not Visual focused)
        {
            return;
        }

        if (focused == RowsList || RowsList.IsVisualAncestorOf(focused))
        {
            viewModel.IsTreeActive = true;
        }
        else if (focused.FindAncestorOfType<Menu>(includeSelf: true) is null)
        {
            viewModel.IsTreeActive = false;
        }
    }

    /// <summary>
    /// Expands or collapses the row that was double-clicked.
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The event.</param>
    private void OnRowsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (RowFrom(e.Source) is { } row && !IsOnExpander(e.Source))
        {
            _viewModel?.ToggleExpansionCommand.Execute(row);
        }
    }

    /// <summary>
    /// Implements the tree's keys: Right expands or moves to the first child, Left collapses or moves to the parent,
    /// Shift+F10 or the Menu key opens the context menu, the platform's extra keys (Space for Quick Look and ⌘↓ on
    /// macOS, numeric-keypad * on Windows) run their commands, and the item shortcuts run their commands when the
    /// platform has no native menu bar to handle them.
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The key event.</param>
    private void OnRowsKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        if (viewModel.Platform.Shortcuts.ContextMenu.Matches(e) || (e.Key == Key.Apps && e.KeyModifiers == KeyModifiers.None))
        {
            e.Handled = OpenContextMenu();
            return;
        }

        if (FindCommand(viewModel, e) is { } command)
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }

            e.Handled = true;
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None || viewModel.SelectedRow is not { } row)
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
        }
    }

    /// <summary>
    /// Finds the command for a key press. The shortcuts shown in the menu bar are left to it where the platform has
    /// one (macOS), so that they run once.
    /// </summary>
    /// <param name="viewModel">The view model.</param>
    /// <param name="e">The key event.</param>
    /// <returns>The command, or <see langword="null"/> when the key has no item command here.</returns>
    private ICommand? FindCommand(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        var keys = viewModel.Platform.Shortcuts;
        if (keys.QuickLook?.Matches(e) == true)
        {
            return viewModel.QuickLookCommand;
        }

        if (keys.OpenAlternate?.Matches(e) == true)
        {
            return viewModel.OpenCommand;
        }

        if (keys.ExpandAllAlternate?.Matches(e) == true)
        {
            return viewModel.ExpandAllCommand;
        }

        if (TopLevel.GetTopLevel(this) is { } topLevel && NativeMenu.GetIsNativeMenuExported(topLevel))
        {
            return null;
        }

        (KeyGesture? Gesture, ICommand Command)[] shortcuts =
        [
            (keys.Open, viewModel.OpenCommand),
            (keys.Reveal, viewModel.RevealCommand),
            (keys.QuickLookMenu, viewModel.QuickLookCommand),
            (keys.GetInfo, viewModel.GetInfoCommand),
            (keys.Copy, viewModel.CopyItemsCommand),
            (keys.CopyPath, viewModel.CopyPathCommand),
            (keys.SelectAll, viewModel.SelectAllCommand),
            (keys.ExpandAll, viewModel.ExpandAllCommand),
            (keys.CollapseAll, viewModel.CollapseAllCommand),
            (keys.Rescan, viewModel.RescanCommand),
            (keys.MoveToTrash, viewModel.MoveToTrashCommand),
            (keys.DeletePermanently, viewModel.DeletePermanentlyCommand),
        ];
        return Array.Find(shortcuts, shortcut => shortcut.Gesture?.Matches(e) == true).Command;
    }

    /// <summary>
    /// Remembers a press that may start a drag. Pressing a row of a multiple selection without modifiers keeps the
    /// selection (the press is handled before the list sees it), so the whole selection can be dragged; releasing
    /// without dragging then selects just that row.
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The event.</param>
    private void OnRowsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ResetPress();
        var point = e.GetCurrentPoint(RowsList);
        if (!point.Properties.IsLeftButtonPressed || RowFrom(e.Source) is not { } row || IsOnExpander(e.Source) || _viewModel is not { } viewModel)
        {
            return;
        }

        _pressArgs = e;
        _pressPosition = point.Position;
        _pressRow = row;
        if (e.KeyModifiers == KeyModifiers.None && e.ClickCount == 1 && viewModel.Selection.Count > 1 && viewModel.Selection.SelectedItems.Contains(row))
        {
            _selectOnRelease = true;
            (RowsList.ContainerFromItem(row) as InputElement)?.Focus(NavigationMethod.Pointer);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Starts dragging the selection once the pointer has moved far enough from the press.
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The event.</param>
    private async void OnRowsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressArgs is not { } press || _pressRow is not { } pressed || _viewModel is not { } viewModel)
        {
            return;
        }

        var point = e.GetCurrentPoint(RowsList);
        if (!point.Properties.IsLeftButtonPressed)
        {
            ResetPress();
            return;
        }

        var delta = point.Position - _pressPosition;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        ResetPress();
        TreeRowViewModel[] rows = viewModel.Selection.SelectedItems.Contains(pressed) ? [.. viewModel.SelectedRows] : [pressed];
        if (!viewModel.CanDrag(rows))
        {
            return;
        }

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(RowsFormat, rows));
        try
        {
            await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move | DragDropEffects.Copy);
        }
        finally
        {
            EndDragFeedback();
        }
    }

    /// <summary>
    /// Selects just the pressed row when a press on a multiple selection did not become a drag.
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The event.</param>
    private void OnRowsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_selectOnRelease && _pressRow is { } row && _viewModel is { } viewModel)
        {
            viewModel.SelectedRow = row;
            e.Handled = true;
        }

        ResetPress();
    }

    /// <summary>
    /// Shows what a drop would do: highlights the folder row under the pointer and reports a move, a copy or a refusal.
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The event.</param>
    private void OnRowsDragOver(object? sender, DragEventArgs e)
    {
        _dragPosition = e.GetPosition(RowsList);
        if (!_dragTimer.IsEnabled)
        {
            _dragTimer.Start();
        }

        var target = RowFrom(e.Source);
        if (!ReferenceEquals(target, _hoverRow))
        {
            _hoverRow = target;
            _hoverStarted = DateTime.UtcNow;
        }

        var mode = e.DataTransfer.TryGetValue(RowsFormat) is { } rows ? GetDropMode(rows, target, e.KeyModifiers) : null;
        e.DragEffects = mode switch
        {
            TransferMode.Move => DragDropEffects.Move,
            TransferMode.Copy => DragDropEffects.Copy,
            _ => DragDropEffects.None,
        };
        SetDropTarget(mode is null ? null : target);
        e.Handled = true;
    }

    /// <summary>
    /// Removes the drop feedback when the drag leaves the list.
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The event.</param>
    private void OnRowsDragLeave(object? sender, DragEventArgs e)
    {
        if (!new Rect(RowsList.Bounds.Size).Contains(e.GetPosition(RowsList)))
        {
            EndDragFeedback();
        }
    }

    /// <summary>
    /// Moves or copies the dragged rows into the folder they were dropped on.
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The event.</param>
    private async void OnRowsDrop(object? sender, DragEventArgs e)
    {
        EndDragFeedback();
        if (_viewModel is not { } viewModel || e.DataTransfer.TryGetValue(RowsFormat) is not { } rows || RowFrom(e.Source) is not { } target
            || GetDropMode(rows, target, e.KeyModifiers) is not { } mode)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = mode == TransferMode.Move ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Handled = true;
        var keys = viewModel.Platform.Shortcuts;
        await viewModel.DropAsync(rows, target, e.KeyModifiers.HasFlag(keys.CopyDragModifier), e.KeyModifiers.HasFlag(keys.MoveDragModifier));
    }

    /// <summary>
    /// While a drag is over the list, opens a collapsed folder hovered for <see cref="SpringLoadDelay"/> and scrolls
    /// when the pointer is near the top or bottom edge.
    /// </summary>
    /// <param name="sender">The timer.</param>
    /// <param name="e">Unused.</param>
    private void OnDragTimerTick(object? sender, EventArgs e)
    {
        if (_hoverRow is { IsExpandable: true, IsExpanded: false } hovered && DateTime.UtcNow - _hoverStarted >= SpringLoadDelay)
        {
            hovered.IsExpanded = true;
        }

        if (RowsList.Scroll is not ScrollViewer scroller)
        {
            return;
        }

        var height = RowsList.Bounds.Height;
        var step = _dragPosition.Y < AutoScrollMargin ? -(AutoScrollMargin - _dragPosition.Y) / 2
            : _dragPosition.Y > height - AutoScrollMargin ? (_dragPosition.Y - (height - AutoScrollMargin)) / 2
            : 0;
        if (step != 0)
        {
            scroller.Offset = scroller.Offset.WithY(Math.Clamp(scroller.Offset.Y + step, 0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height)));
        }
    }

    /// <summary>
    /// Decides what dropping rows onto a row would do, given the modifier keys held.
    /// </summary>
    /// <param name="rows">The dragged rows.</param>
    /// <param name="target">The row under the pointer.</param>
    /// <param name="modifiers">The modifier keys.</param>
    /// <returns>The operation, or <see langword="null"/> when the drop is not allowed.</returns>
    private TransferMode? GetDropMode(TreeRowViewModel[] rows, TreeRowViewModel? target, KeyModifiers modifiers) =>
        _viewModel is { Platform.Shortcuts: var keys } viewModel
            ? viewModel.GetDropMode(rows, target, modifiers.HasFlag(keys.CopyDragModifier), modifiers.HasFlag(keys.MoveDragModifier))
            : null;

    /// <summary>
    /// Highlights the row that would receive a drop.
    /// </summary>
    /// <param name="row">The row, or <see langword="null"/> for none.</param>
    private void SetDropTarget(TreeRowViewModel? row)
    {
        if (ReferenceEquals(row, _dropTarget))
        {
            return;
        }

        _dropTarget?.IsDropTarget = false;
        _dropTarget = row;
        _dropTarget?.IsDropTarget = true;
    }

    /// <summary>
    /// Removes the drop highlight and stops opening and scrolling.
    /// </summary>
    private void EndDragFeedback()
    {
        SetDropTarget(null);
        _hoverRow = null;
        _dragTimer.Stop();
    }

    /// <summary>
    /// Forgets the press that may have started a drag.
    /// </summary>
    private void ResetPress()
    {
        _pressArgs = null;
        _pressRow = null;
        _selectOnRelease = false;
    }

    /// <summary>
    /// Finds the row of the list item containing an element.
    /// </summary>
    /// <param name="source">The element, typically an event's source.</param>
    /// <returns>The row, or <see langword="null"/> when the element is not inside a row.</returns>
    private static TreeRowViewModel? RowFrom(object? source) =>
        (source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as TreeRowViewModel;

    /// <summary>
    /// Determines whether an element is a row's expand button (or inside it).
    /// </summary>
    /// <param name="source">The element.</param>
    /// <returns><see langword="true"/> when the element is an expand button.</returns>
    private static bool IsOnExpander(object? source) =>
        (source as Visual)?.FindAncestorOfType<ToggleButton>(includeSelf: true) is not null;

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
