namespace WhatDisk.ViewModels;

/// <summary>
/// A list that supports inserting and removing ranges as single operations, so that bound views receive one change
/// notification per range rather than one per item.
/// </summary>
/// <typeparam name="T">The type of item.</typeparam>
public interface IRangeList<T> : IReadOnlyList<T>
{
    /// <summary>
    /// Inserts items at a position.
    /// </summary>
    /// <param name="index">The position of the first inserted item.</param>
    /// <param name="items">The items to insert.</param>
    void InsertRange(int index, IReadOnlyList<T> items);

    /// <summary>
    /// Removes a contiguous range of items.
    /// </summary>
    /// <param name="index">The position of the first item to remove.</param>
    /// <param name="count">The number of items to remove.</param>
    void RemoveRange(int index, int count);
}
