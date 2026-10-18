using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Input;
using WhatDisk.Platform;
using WhatDisk.ViewModels;

namespace WhatDisk.Views;

/// <summary>
/// The single application window: a toolbar to choose what to scan, the results tree, the chart and details panes,
/// and a status bar. On macOS its File, Edit, View and Tools menus appear in the menu bar.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>The view model the window is subscribed to.</summary>
    private MainWindowViewModel? _viewModel;

    /// <summary>The window-wide Settings shortcut, where the menu bar is drawn in the window.</summary>
    private KeyBinding? _settingsBinding;

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

        // A menu bar drawn in the window does not handle its shortcuts, so Settings gets a window-wide one; on macOS the
        // application menu handles ⌘,.
        if (_settingsBinding is not null)
        {
            KeyBindings.Remove(_settingsBinding);
        }

        _settingsBinding = _viewModel is { Platform.HasApplicationMenu: false } model
            ? new KeyBinding { Gesture = model.Platform.Shortcuts.Settings, Command = model.OpenSettingsCommand }
            : null;
        if (_settingsBinding is not null)
        {
            KeyBindings.Add(_settingsBinding);
        }
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
    /// Creates the File, Edit, View and Tools menus (and, without an application menu, Help) for the menu bar: the
    /// macOS menu bar, or a menu bar at the top of the window elsewhere. The item commands are enabled only while the
    /// tree is the active pane, so that shortcuts such as ⌘C and ⌘⌫ reach the path box when it has focus; Find
    /// Duplicates works on the whole scan and is enabled whenever a scan has finished.
    /// </summary>
    /// <param name="viewModel">The view model whose commands the menus run.</param>
    /// <returns>The menu bar.</returns>
    private NativeMenu BuildMenuBar(MainWindowViewModel viewModel)
    {
        var platform = viewModel.Platform;
        var keys = platform.Shortcuts;
        var supported = viewModel.ShellCapabilities;
        var openWith = new NativeMenu();
        openWith.NeedsUpdate += (_, _) => FillOpenWith(openWith, viewModel);
        openWith.Opening += (_, _) => FillOpenWith(openWith, viewModel);
        var file = new NativeMenu();
        Add(file, "Open", viewModel.OpenCommand, keys.Open, ShellCapabilities.Open);
        if (supported.HasFlag(ShellCapabilities.OpenWith))
        {
            file.Items.Add(new NativeMenuItem("Open With") { Menu = openWith });
        }

        file.Items.Add(new NativeMenuItemSeparator());
        Add(file, $"Show in {platform.FileManagerName}", viewModel.RevealCommand, keys.Reveal, ShellCapabilities.Reveal);
        Add(file, "Quick Look", viewModel.QuickLookCommand, keys.QuickLookMenu, ShellCapabilities.QuickLook);
        Add(file, platform.InfoCommandName, viewModel.GetInfoCommand, keys.GetInfo, ShellCapabilities.ShowInfo);
        file.Items.Add(new NativeMenuItemSeparator());
        Add(file, "Rescan", viewModel.RescanCommand, keys.Rescan);
        file.Items.Add(new NativeMenuItemSeparator());
        Add(file, $"Move to {platform.TrashName}", viewModel.MoveToTrashCommand, keys.MoveToTrash, ShellCapabilities.Trash);
        Add(file, $"{platform.PermanentDeleteName}\u2026", viewModel.DeletePermanentlyCommand, keys.DeletePermanently);
        if (!platform.HasApplicationMenu)
        {
            file.Items.Add(new NativeMenuItemSeparator());
            file.Items.Add(new NativeMenuItem("E_xit") { Command = new RelayCommand(Close) });
        }

        var edit = new NativeMenu();
        Add(edit, "Copy", viewModel.CopyItemsCommand, keys.Copy, ShellCapabilities.CopyItems);
        Add(edit, "Copy Path", viewModel.CopyPathCommand, keys.CopyPath, ShellCapabilities.CopyText);
        edit.Items.Add(new NativeMenuItemSeparator());
        Add(edit, "Select All", viewModel.SelectAllCommand, keys.SelectAll);

        var view = new NativeMenu();
        Add(view, "Expand All Beneath", viewModel.ExpandAllCommand, keys.ExpandAll);
        Add(view, "Collapse All Beneath", viewModel.CollapseAllCommand, keys.CollapseAll);

        var tools = new NativeMenu();
        Add(tools, "Find Duplicates", viewModel.FindDuplicatesCommand, null);
        if (!platform.HasApplicationMenu)
        {
            // macOS has Settings in the application menu instead.
            tools.Items.Add(new NativeMenuItemSeparator());
            Add(tools, "Settings\u2026", viewModel.OpenSettingsCommand, keys.Settings);
        }

        // Where the menu bar is drawn in the window (Windows), the titles get access keys (Alt+F and so on) and About
        // goes in a Help menu; macOS has About in the application menu instead.
        var accessKey = platform.HasApplicationMenu ? string.Empty : "_";
        var bar = new NativeMenu
        {
            Items =
            {
                new NativeMenuItem($"{accessKey}File") { Menu = file },
                new NativeMenuItem($"{accessKey}Edit") { Menu = edit },
                new NativeMenuItem($"{accessKey}View") { Menu = view },
                new NativeMenuItem($"{accessKey}Tools") { Menu = tools },
            },
        };
        if (!platform.HasApplicationMenu)
        {
            var help = new NativeMenu { Items = { new NativeMenuItem("_About WhatDisk") { Command = new AsyncRelayCommand(() => AboutWindow.ShowAsync(this)) } } };
            bar.Items.Add(new NativeMenuItem("_Help") { Menu = help });
        }

        return bar;

        void Add(NativeMenu menu, string header, ICommand command, KeyGesture? gesture, ShellCapabilities? capability = null)
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
