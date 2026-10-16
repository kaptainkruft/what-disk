using Avalonia.Controls;
using WhatDisk.ViewModels;

namespace WhatDisk.Views;

/// <summary>
/// The single application window: a toolbar to choose what to scan, the results tree, the chart and details panes,
/// and a status bar.
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
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.ScrollIntoViewRequested += OnScrollIntoViewRequested;
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
}
