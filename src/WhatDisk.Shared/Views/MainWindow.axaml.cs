using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using WhatDisk.Platform;
using WhatDisk.ViewModels;

namespace WhatDisk.Views;

/// <summary>
/// The single application window: a toolbar to choose what to scan, the results tree, the chart and details panes,
/// and a status bar. On macOS its File, Edit and View menus appear in the menu bar.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>The view model the window is subscribed to.</summary>
    private MainWindowViewModel? _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        Picker.VolumeChosen += (_, _) => VolumesButton.Flyout?.Hide();
    }

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.ScrollIntoViewRequested -= OnScrollIntoViewRequested;
            _viewModel.FocusRequested -= OnFocusRequested;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.ScrollIntoViewRequested += OnScrollIntoViewRequested;
            _viewModel.FocusRequested += OnFocusRequested;
        }

        NativeMenu.SetMenu(this, _viewModel is null ? null : BuildMenuBar(_viewModel));
    }

    /// <inheritdoc/>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _viewModel?.LoadVolumesCommand.Execute(null);
        PathBox.Focus();
    }

    /// <summary>
    /// Refreshes the volume list whenever the volume picker opens, so newly mounted volumes appear.
    /// </summary>
    /// <param name="sender">The flyout.</param>
    /// <param name="e">Unused.</param>
    private void OnVolumesFlyoutOpening(object? sender, EventArgs e) => _viewModel?.LoadVolumesCommand.Execute(null);

    /// <summary>
    /// Scrolls the tree to a row the view model wants to show.
    /// </summary>
    /// <param name="sender">The view model.</param>
    /// <param name="row">The row.</param>
    private void OnScrollIntoViewRequested(object? sender, TreeRowViewModel row) => TreeList.ScrollIntoView(row);

    /// <summary>
    /// Gives a tree row keyboard focus when the view model asks (after a scan), so the arrow keys work at once.
    /// </summary>
    /// <param name="sender">The view model.</param>
    /// <param name="row">The row.</param>
    private void OnFocusRequested(object? sender, TreeRowViewModel row) => TreeList.FocusRow(row);

    /// <summary>
    /// Creates the File, Edit and View menus for the menu bar. Their items are enabled only while the tree is the
    /// active pane, so that shortcuts such as ⌘C and ⌘⌫ reach the path box when it has focus.
    /// </summary>
    /// <param name="viewModel">The view model whose commands the menus run.</param>
    /// <returns>The menu bar.</returns>
    private static NativeMenu BuildMenuBar(MainWindowViewModel viewModel)
    {
        var supported = viewModel.ShellCapabilities;
        var openWith = new NativeMenu();
        openWith.NeedsUpdate += (_, _) => FillOpenWith(openWith, viewModel);
        var file = new NativeMenu();
        Add(file, "Open", viewModel.OpenCommand, ItemShortcuts.Open, ShellCapabilities.Open);
        if (supported.HasFlag(ShellCapabilities.OpenWith))
        {
            file.Items.Add(new NativeMenuItem("Open With") { Menu = openWith });
        }

        file.Items.Add(new NativeMenuItemSeparator());
        Add(file, "Show in Finder", viewModel.RevealCommand, ItemShortcuts.Reveal, ShellCapabilities.Reveal);
        Add(file, "Quick Look", viewModel.QuickLookCommand, ItemShortcuts.QuickLookMenu, ShellCapabilities.QuickLook);
        Add(file, "Get Info", viewModel.GetInfoCommand, ItemShortcuts.GetInfo, ShellCapabilities.ShowInfo);
        file.Items.Add(new NativeMenuItemSeparator());
        Add(file, "Rescan", viewModel.RescanCommand, ItemShortcuts.Rescan);
        file.Items.Add(new NativeMenuItemSeparator());
        Add(file, "Move to Trash", viewModel.MoveToTrashCommand, ItemShortcuts.MoveToTrash, ShellCapabilities.Trash);
        Add(file, "Delete Immediately\u2026", viewModel.DeleteImmediatelyCommand, ItemShortcuts.DeleteImmediately);

        var edit = new NativeMenu();
        Add(edit, "Copy", viewModel.CopyItemsCommand, ItemShortcuts.Copy, ShellCapabilities.CopyItems);
        Add(edit, "Copy Path", viewModel.CopyPathCommand, ItemShortcuts.CopyPath, ShellCapabilities.CopyText);
        edit.Items.Add(new NativeMenuItemSeparator());
        Add(edit, "Select All", viewModel.SelectAllCommand, ItemShortcuts.SelectAll);

        var view = new NativeMenu();
        Add(view, "Expand All Beneath", viewModel.ExpandAllCommand, ItemShortcuts.ExpandAll);
        Add(view, "Collapse All Beneath", viewModel.CollapseAllCommand, ItemShortcuts.CollapseAll);

        return new NativeMenu
        {
            Items =
            {
                new NativeMenuItem("File") { Menu = file },
                new NativeMenuItem("Edit") { Menu = edit },
                new NativeMenuItem("View") { Menu = view },
            },
        };

        void Add(NativeMenu menu, string header, ICommand command, KeyGesture gesture, ShellCapabilities? capability = null)
        {
            if (capability is not { } required || supported.HasFlag(required))
            {
                menu.Items.Add(new NativeMenuItem(header) { Command = command, Gesture = gesture });
            }
        }
    }

    /// <summary>
    /// Lists the applications that can open the selection in the menu bar's Open With submenu.
    /// </summary>
    /// <param name="menu">The submenu.</param>
    /// <param name="viewModel">The view model.</param>
    private static void FillOpenWith(NativeMenu menu, MainWindowViewModel viewModel)
    {
        menu.Items.Clear();
        var choices = viewModel.IsTreeActive ? viewModel.GetOpenWithChoices() : [];
        foreach (var choice in choices)
        {
            menu.Items.Add(new NativeMenuItem(choice.IsDefault ? $"{choice.Name} (default)" : choice.Name)
            {
                Command = viewModel.OpenWithCommand,
                CommandParameter = choice,
            });
        }

        if (choices.Count == 0)
        {
            menu.Items.Add(new NativeMenuItem("No Applications") { IsEnabled = false });
        }
    }
}
