using System.Globalization;
using WhatDisk.Scanning;

namespace WhatDisk.Charting;

/// <summary>
/// Computes pie chart slices for the children of a container. The largest items get a slice each and the rest are
/// grouped into "Other".
/// </summary>
/// <remarks>
/// Colours follow items rather than their rank: an item keeps its colour slot for as long as it stays among the
/// largest, even while the scan reorders them. Slices are drawn in slot order so that neighbouring slices always use
/// colour pairs that were validated as distinguishable.
/// </remarks>
public sealed class PieChartModel
{
    /// <summary>
    /// The number of items that get their own slice; the categorical palette has a validated colour for each.
    /// </summary>
    public const int MaxNamedSlices = 5;

    /// <summary>
    /// The colour slot of the "Other" slice.
    /// </summary>
    public const int OtherSlot = -1;

    /// <summary>The colour slot assigned to each item currently shown.</summary>
    private readonly Dictionary<FileSystemNode, int> _slots = new(ReferenceEqualityComparer.Instance);

    /// <summary>The container whose children are shown; slot assignments reset when it changes.</summary>
    private object? _container;

    /// <summary>
    /// Recomputes the chart for the items of a container.
    /// </summary>
    /// <param name="container">An object identifying the container; colour assignments are kept while it is unchanged.</param>
    /// <param name="items">The container's children.</param>
    /// <returns>The chart contents.</returns>
    public PieChartSnapshot Update(object? container, IReadOnlyList<FileSystemNode> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (!ReferenceEquals(container, _container))
        {
            _slots.Clear();
            _container = container;
        }

        // Sizes change while a scan runs, so each is read once. A single pass keeps the largest items and totals the
        // rest, so folders with hundreds of thousands of entries stay cheap to chart on every refresh.
        var named = new List<(FileSystemNode Node, long Size)>(MaxNamedSlices + 1);
        long total = 0, sizedCount = 0;
        foreach (var node in items)
        {
            var size = node.Size;
            if (size <= 0)
            {
                continue;
            }

            total += size;
            sizedCount++;
            if (named.Count == MaxNamedSlices && !IsLarger((node, size), named[^1]))
            {
                continue;
            }

            var position = named.FindIndex(existing => IsLarger((node, size), existing));
            named.Insert(position < 0 ? named.Count : position, (node, size));
            if (named.Count > MaxNamedSlices)
            {
                named.RemoveAt(named.Count - 1);
            }
        }

        if (total <= 0)
        {
            _slots.Clear();
            return PieChartSnapshot.Empty;
        }

        AssignSlots(named.Select(static item => item.Node));

        var slices = new List<PieSlice>(named.Count + 1);
        var angle = 0d;
        foreach (var (node, size) in named.OrderBy(item => _slots[item.Node]))
        {
            var fraction = (double)size / total;
            slices.Add(new PieSlice(node, node.Name, size, fraction, _slots[node], angle, fraction * 360));
            angle += fraction * 360;
        }

        var remaining = sizedCount - named.Count;
        var otherSize = total - named.Sum(static item => item.Size);
        if (remaining > 0 && otherSize > 0)
        {
            var fraction = (double)otherSize / total;
            var label = string.Create(CultureInfo.CurrentCulture, $"{remaining:N0} other {(remaining == 1 ? "item" : "items")}");
            slices.Add(new PieSlice(null, label, otherSize, fraction, OtherSlot, angle, 360 - angle));
        }

        PieSlice[] legend = [.. slices.Where(static slice => !slice.IsOther).OrderByDescending(static slice => slice.Size), .. slices.Where(static slice => slice.IsOther)];
        return new PieChartSnapshot(total, slices, legend);
    }

    /// <summary>
    /// Determines whether one item ranks before another: larger first, then by name.
    /// </summary>
    /// <param name="candidate">The item being placed.</param>
    /// <param name="existing">An item already ranked.</param>
    /// <returns><see langword="true"/> when <paramref name="candidate"/> ranks first.</returns>
    private static bool IsLarger((FileSystemNode Node, long Size) candidate, (FileSystemNode Node, long Size) existing) =>
        candidate.Size != existing.Size
            ? candidate.Size > existing.Size
            : StringComparer.CurrentCultureIgnoreCase.Compare(candidate.Node.Name, existing.Node.Name) < 0;

    /// <summary>
    /// Releases the slots of items that left the named set and gives each newly named item the lowest free slot.
    /// </summary>
    /// <param name="named">The items that get their own slice, largest first.</param>
    private void AssignSlots(IEnumerable<FileSystemNode> named)
    {
        FileSystemNode[] ordered = [.. named];
        var current = ordered.ToHashSet<FileSystemNode>(ReferenceEqualityComparer.Instance);
        foreach (var stale in _slots.Keys.Where(node => !current.Contains(node)).ToArray())
        {
            _slots.Remove(stale);
        }

        foreach (var node in ordered)
        {
            if (_slots.ContainsKey(node))
            {
                continue;
            }

            var slot = 0;
            while (_slots.ContainsValue(slot))
            {
                slot++;
            }

            _slots[node] = slot;
        }
    }
}
