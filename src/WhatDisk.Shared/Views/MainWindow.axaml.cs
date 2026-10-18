using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using WhatDisk.ViewModels;

namespace WhatDisk.Views;

/// <summary>
/// The single application window. Its tabs hold the scan (a toolbar to choose what to scan, the results tree, the chart
/// and details panes), the results of each duplicate search and the settings; a status bar runs along the bottom, and
/// questions and the About box appear over the window rather than in windows of their own. Keeping everything in one
/// window means the macOS menu bar never switches between windows' menus, which Avalonia does unreliably on macOS 27.
/// On macOS its menus appear in the menu bar.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Completes when the last in-window dialog asked for has closed, so dialogs show one at a time.</summary>
    private Task _lastDialog = Task.CompletedTask;

    /// <summary>The window-wide shortcuts, where the menu bar is drawn in the window.</summary>
    private readonly List<KeyBinding> _windowBindings = [];

    /// <summary>The view model the window is subscribed to.</summary>
    private MainWindowViewModel? _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        Picker.VolumeChosen += (_, _) => VolumesButton.Flyout?.Hide();
        Activated += (_, _) => _viewModel?.OnWindowActivated();
    }

    /// <summary>
    /// Raised when an in-window dialog starts or stops showing.
    /// </summary>
    public event EventHandler? DialogShowingChanged;

    /// <summary>
    /// Gets a value indicating whether an in-window dialog is showing.
    /// </summary>
    public bool IsShowingDialog => DialogLayer.IsVisible;

    /// <summary>
    /// Shows a dialog over the window, dimming and disabling everything else (the in-window menu bar too), and waits
    /// for its result. Dialogs asked for meanwhile wait their turn. Keyboard focus returns where it was afterwards.
    /// </summary>
    /// <param name="dialog">The dialog.</param>
    /// <returns>The dialog's result.</returns>
    public async Task<string?> ShowDialogAsync(InWindowDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var previous = _lastDialog;
        var closed = new TaskCompletionSource();
        _lastDialog = closed.Task;
        await previous;
        var previousFocus = FocusManager?.GetFocusedElement();
        try
        {
            Frame.IsEnabled = false;
            DialogCard.Child = dialog;
            DialogLayer.IsVisible = true;
            DialogShowingChanged?.Invoke(this, EventArgs.Empty);
            Dispatcher.UIThread.Post(dialog.FocusDefault, DispatcherPriority.Loaded);
            return await dialog.Completion;
        }
        finally
        {
            DialogLayer.IsVisible = false;
            DialogCard.Child = null;
            Frame.IsEnabled = true;
            DialogShowingChanged?.Invoke(this, EventArgs.Empty);
            (previousFocus as InputElement)?.Focus();
            closed.SetResult();
        }
    }

    /// <summary>
    /// Shows the About box over the window.
    /// </summary>
    /// <returns>A task that completes when the About box has been closed.</returns>
    public Task ShowAboutAsync() => ShowDialogAsync(AboutView.ForRunningApp());

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.ScrollIntoViewRequested -= OnScrollIntoViewRequested;
            _viewModel.FocusRequested -= OnFocusRequested;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.ScrollIntoViewRequested += OnScrollIntoViewRequested;
            _viewModel.FocusRequested += OnFocusRequested;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        // A menu bar drawn in the window does not handle its shortcuts, so the commands that do not depend on the tree
        // having focus get window-wide ones (the tree handles the others); on macOS the menu bar handles them.
        foreach (var binding in _windowBindings)
        {
            KeyBindings.Remove(binding);
        }

        _windowBindings.Clear();
        if (_viewModel is { Platform.HasApplicationMenu: false } model)
        {
            var keys = model.Platform.Shortcuts;
            _windowBindings.Add(new KeyBinding { Gesture = keys.Settings, Command = model.OpenSettingsCommand });
            _windowBindings.Add(new KeyBinding { Gesture = keys.FindDuplicatesInScan, Command = model.FindDuplicatesInScanCommand });
            _windowBindings.Add(new KeyBinding { Gesture = keys.CloseTab, Command = model.CloseTabCommand });
            if (keys.Quit is { } quit)
            {
                _windowBindings.Add(new KeyBinding { Gesture = quit, Command = new RelayCommand(Close) });
            }
            KeyBindings.AddRange(_windowBindings);
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
    /// Gives keyboard focus back to the tree (or the path box before a scan) when the Scan tab is shown again, so the
    /// tree is the active pane and its shortcuts work at once.
    /// </summary>
    /// <param name="sender">The view model.</param>
    /// <param name="e">The changed property.</param>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsScanTabSelected) && _viewModel is { IsScanTabSelected: true } viewModel)
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (viewModel.SelectedRow is { } row)
                    {
                        TreeList.FocusRow(row);
                    }
                    else
                    {
                        PathBox.Focus();
                    }
                },
                DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// Quits from File › Exit, on platforms whose menus are drawn in the window.
    /// </summary>
    /// <param name="sender">The menu item.</param>
    /// <param name="e">Unused.</param>
    private void OnExitClicked(object? sender, EventArgs e) => Close();

    /// <summary>
    /// Shows the About box from Help › About, on platforms whose menus are drawn in the window.
    /// </summary>
    /// <param name="sender">The menu item.</param>
    /// <param name="e">Unused.</param>
    private async void OnAboutClicked(object? sender, EventArgs e) => await ShowAboutAsync();
}
