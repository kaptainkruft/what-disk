using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using WhatDisk.ViewModels;

namespace WhatDisk.Views;

/// <summary>
/// Shows duplicate files in a tab: a grid of set number, full path, size, hash and how each file was compared, one row
/// per file, with buttons to compare the files (in the Duplicates tab, a sampled scan, and including smaller files),
/// to hash every set not yet confirmed by hash, and to copy the results to the clipboard as CSV or export them to a
/// CSV file. Double-clicking a row shows the file in the file manager; its context menu hashes the row's set. Closing a
/// tab stops any hashing.
/// </summary>
public partial class DuplicatesView : UserControl
{
    /// <summary>The view model the window is subscribed to.</summary>
    private DuplicateResultsViewModel? _results;

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicatesView"/> class.
    /// </summary>
    public DuplicatesView()
    {
        InitializeComponent();
        ExportCommand = new AsyncRelayCommand(ExportAsync, CanExport);
        ExportButton.Command = ExportCommand;
    }

    /// <summary>
    /// Gets the command that asks where to save the CSV and saves it.
    /// </summary>
    public IAsyncRelayCommand ExportCommand { get; }

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_results is not null)
        {
            _results.PropertyChanged -= OnResultsPropertyChanged;
        }

        _results = DataContext as DuplicateResultsViewModel;
        if (_results is not null)
        {
            _results.PropertyChanged += OnResultsPropertyChanged;
        }

        ExportCommand.NotifyCanExecuteChanged();
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Saving needs the window's storage provider, which is known only once the view is in the window.
        ExportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Gets the window's storage provider, for the save dialog, or <see langword="null"/> outside a window.
    /// </summary>
    private IStorageProvider? StorageProvider => TopLevel.GetTopLevel(this)?.StorageProvider;

    /// <summary>
    /// Re-evaluates Export when the rows change or a scan or hashing starts or stops.
    /// </summary>
    /// <param name="sender">The view model.</param>
    /// <param name="e">The changed property.</param>
    private void OnResultsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DuplicateResultsViewModel.HasRows) or nameof(DuplicateResultsViewModel.IsBusy))
        {
            ExportCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Determines whether there is anything to export, somewhere to save it, and no scan or hashing changing the sets.
    /// </summary>
    /// <returns><see langword="true"/> when Export is enabled.</returns>
    private bool CanExport() => DataContext is DuplicateResultsViewModel { HasRows: true, IsBusy: false } && StorageProvider is { CanSave: true };

    /// <summary>
    /// Asks for a file name with the system's save dialog, starting in Documents (or the Desktop), and saves the CSV.
    /// </summary>
    /// <returns>A task that completes when the file has been saved or the dialog cancelled.</returns>
    private async Task ExportAsync()
    {
        if (DataContext is not DuplicateResultsViewModel results || StorageProvider is not { } storage)
        {
            return;
        }

        var start = await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Documents)
            ?? await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Desktop);
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Duplicates",
            SuggestedFileName = results.SuggestedFileName,
            SuggestedStartLocation = start,
            DefaultExtension = "csv",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType("CSV (comma-separated values)")
                {
                    Patterns = ["*.csv"],
                    AppleUniformTypeIdentifiers = ["public.comma-separated-values-text"],
                    MimeTypes = ["text/csv"],
                },
            ],
        });
        if (file is not null)
        {
            using (file)
            {
                await results.ExportAsync(file.Name, file.OpenWriteAsync);
            }
        }
    }

    /// <summary>
    /// Shows the double-clicked row's file in the file manager.
    /// </summary>
    /// <param name="sender">The list.</param>
    /// <param name="e">The event.</param>
    private void OnRowsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is DuplicateResultsViewModel results
            && (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is DuplicateRowViewModel row
            && results.RevealCommand.CanExecute(row))
        {
            results.RevealCommand.Execute(row);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Points the context menu at the selected row (a right-click selects the row first), or keeps it closed when no
    /// row is selected.
    /// </summary>
    /// <param name="sender">The context menu.</param>
    /// <param name="e">Cancels the menu.</param>
    private void OnRowMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e) => e.Cancel = !PrepareRowMenu();

    /// <summary>
    /// Points the context menu's item at the selected row.
    /// </summary>
    /// <returns><see langword="true"/> when a row is selected.</returns>
    internal bool PrepareRowMenu()
    {
        if (RowsList.SelectedItem is not DuplicateRowViewModel row)
        {
            return false;
        }

        VerifySetItem.CommandParameter = row;
        return true;
    }

}
