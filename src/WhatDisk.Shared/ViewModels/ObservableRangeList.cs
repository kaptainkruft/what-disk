using Avalonia.Collections;

namespace WhatDisk.ViewModels;

/// <summary>
/// An observable list whose range operations raise a single collection change notification.
/// </summary>
/// <typeparam name="T">The type of item.</typeparam>
public sealed class ObservableRangeList<T> : AvaloniaList<T>, IRangeList<T>
{
    /// <inheritdoc/>
    void IRangeList<T>.InsertRange(int index, IReadOnlyList<T> items) => InsertRange(index, items);
}
