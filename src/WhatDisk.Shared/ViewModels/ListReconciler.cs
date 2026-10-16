namespace WhatDisk.ViewModels;

/// <summary>
/// Turns the current contents of a list into a desired sequence with few range operations.
/// </summary>
/// <remarks>
/// The longest run of items that are already in the right relative order is kept in place (a longest increasing
/// subsequence of their current positions); every other item is removed and re-inserted at its new position. When a
/// directory moves past a sibling, only that directory's rows move instead of everything in between.
/// </remarks>
public static class ListReconciler
{
    /// <summary>
    /// Applies the changes needed to make <paramref name="target"/> equal to <paramref name="desired"/>.
    /// </summary>
    /// <typeparam name="T">The type of item; items are compared by reference and must be unique.</typeparam>
    /// <param name="target">The list to change.</param>
    /// <param name="desired">The sequence the list should contain afterwards.</param>
    /// <returns><see langword="true"/> if the list was changed.</returns>
    public static bool Apply<T>(IRangeList<T> target, IReadOnlyList<T> desired)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(desired);
        if (SequenceEqual(target, desired))
        {
            return false;
        }

        var currentIndex = new Dictionary<T, int>(target.Count, ReferenceEqualityComparer.Instance);
        for (var index = 0; index < target.Count; index++)
        {
            currentIndex[target[index]] = index;
        }

        var positions = new List<int>(desired.Count);
        foreach (var item in desired)
        {
            if (currentIndex.TryGetValue(item, out var index))
            {
                positions.Add(index);
            }
        }

        var keep = FindLongestIncreasingRun(positions, target.Count);
        RemoveUnkept(target, keep);
        InsertMissing(target, desired);
        return true;
    }

    /// <summary>
    /// Determines whether two lists contain the same items, by reference, in the same order.
    /// </summary>
    /// <typeparam name="T">The type of item.</typeparam>
    /// <param name="first">The first list.</param>
    /// <param name="second">The second list.</param>
    /// <returns><see langword="true"/> when the sequences are identical.</returns>
    private static bool SequenceEqual<T>(IReadOnlyList<T> first, IReadOnlyList<T> second)
        where T : class
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var index = 0; index < first.Count; index++)
        {
            if (!ReferenceEquals(first[index], second[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Finds which current positions form the longest increasing subsequence of <paramref name="positions"/>.
    /// </summary>
    /// <param name="positions">The current positions of the desired items that are already present, in desired order.</param>
    /// <param name="count">The number of items currently in the list.</param>
    /// <returns>A flag per current position: <see langword="true"/> when that item stays where it is.</returns>
    private static bool[] FindLongestIncreasingRun(List<int> positions, int count)
    {
        var tails = new int[positions.Count];
        var previous = new int[positions.Count];
        var length = 0;
        for (var index = 0; index < positions.Count; index++)
        {
            var value = positions[index];
            int low = 0, high = length;
            while (low < high)
            {
                var middle = (low + high) >>> 1;
                if (positions[tails[middle]] < value)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            previous[index] = low > 0 ? tails[low - 1] : -1;
            tails[low] = index;
            if (low == length)
            {
                length++;
            }
        }

        var keep = new bool[count];
        for (var index = length > 0 ? tails[length - 1] : -1; index >= 0; index = previous[index])
        {
            keep[positions[index]] = true;
        }

        return keep;
    }

    /// <summary>
    /// Removes every item that is not kept, one contiguous range at a time, from the end backwards.
    /// </summary>
    /// <typeparam name="T">The type of item.</typeparam>
    /// <param name="target">The list.</param>
    /// <param name="keep">Which current positions are kept.</param>
    private static void RemoveUnkept<T>(IRangeList<T> target, bool[] keep)
    {
        var index = keep.Length - 1;
        while (index >= 0)
        {
            if (keep[index])
            {
                index--;
                continue;
            }

            var end = index;
            while (index >= 0 && !keep[index])
            {
                index--;
            }

            target.RemoveRange(index + 1, end - index);
        }
    }

    /// <summary>
    /// Inserts the desired items that are missing, one contiguous run at a time. The list must already contain the
    /// kept items in their desired relative order.
    /// </summary>
    /// <typeparam name="T">The type of item.</typeparam>
    /// <param name="target">The list.</param>
    /// <param name="desired">The desired sequence.</param>
    private static void InsertMissing<T>(IRangeList<T> target, IReadOnlyList<T> desired)
        where T : class
    {
        var targetIndex = 0;
        var desiredIndex = 0;
        while (desiredIndex < desired.Count)
        {
            if (targetIndex < target.Count && ReferenceEquals(target[targetIndex], desired[desiredIndex]))
            {
                targetIndex++;
                desiredIndex++;
                continue;
            }

            var start = desiredIndex;
            while (desiredIndex < desired.Count
                && (targetIndex >= target.Count || !ReferenceEquals(target[targetIndex], desired[desiredIndex])))
            {
                desiredIndex++;
            }

            var run = new T[desiredIndex - start];
            for (var offset = 0; offset < run.Length; offset++)
            {
                run[offset] = desired[start + offset];
            }

            target.InsertRange(targetIndex, run);
            targetIndex += run.Length;
        }
    }
}
