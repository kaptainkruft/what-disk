using System.Globalization;

namespace WhatDisk.Charting;

/// <summary>
/// Computes pie chart slices from a set of items, such as the total size of each file type in a folder. The largest
/// items get a slice each and the rest are grouped into "other".
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
    private readonly Dictionary<string, int> _slots = new(StringComparer.Ordinal);

    /// <summary>The container whose contents are shown; slot assignments reset when it changes.</summary>
    private object? _container;

    /// <summary>
    /// Recomputes the chart for the items of a container.
    /// </summary>
    /// <param name="container">An object identifying the container; colour assignments are kept while it is unchanged.</param>
    /// <param name="items">The items to chart; keys must be unique.</param>
    /// <returns>The chart contents.</returns>
    public PieChartSnapshot Update(object? container, IReadOnlyList<ChartItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (!ReferenceEquals(container, _container))
        {
            _slots.Clear();
            _container = container;
        }

        // Sizes change while a scan runs, so each is read once. A single pass keeps the largest items and totals the
        // rest, so folders with hundreds of thousands of entries stay cheap to chart on every refresh.
        var named = new List<ChartItem>(MaxNamedSlices + 1);
        long total = 0, sizedCount = 0;
        foreach (var item in items)
        {
            var size = item.Size;
            if (size <= 0)
            {
                continue;
            }

            total += size;
            sizedCount++;
            if (named.Count == MaxNamedSlices && !IsLarger(item, named[^1]))
            {
                continue;
            }

            var position = named.FindIndex(existing => IsLarger(item, existing));
            named.Insert(position < 0 ? named.Count : position, item);
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

        AssignSlots(named.Select(static item => item.Key));

        var slices = new List<PieSlice>(named.Count + 1);
        var angle = 0d;
        foreach (var item in named.OrderBy(item => _slots[item.Key]))
        {
            var fraction = (double)item.Size / total;
            slices.Add(new PieSlice(item.Key, item.Label, item.Description, item.Size, fraction, _slots[item.Key], angle, fraction * 360));
            angle += fraction * 360;
        }

        var remaining = sizedCount - named.Count;
        var otherSize = total - named.Sum(static item => item.Size);
        if (remaining > 0 && otherSize > 0)
        {
            var fraction = (double)otherSize / total;
            var label = string.Create(CultureInfo.CurrentCulture, $"{remaining:N0} other {(remaining == 1 ? "type" : "types")}");
            var description = string.Join(", ", items.Where(item => item.Size > 0 && !named.Contains(item)).OrderByDescending(static item => item.Size).Take(8).Select(static item => item.Label));
            slices.Add(new PieSlice(null, label, remaining > 8 ? $"{description}, \u2026" : description, otherSize, fraction, OtherSlot, angle, 360 - angle));
        }

        PieSlice[] legend = [.. slices.Where(static slice => !slice.IsOther).OrderByDescending(static slice => slice.Size), .. slices.Where(static slice => slice.IsOther)];
        return new PieChartSnapshot(total, slices, legend);
    }

    /// <summary>
    /// Determines whether one item ranks before another: larger first, then by label.
    /// </summary>
    /// <param name="candidate">The item being placed.</param>
    /// <param name="existing">An item already ranked.</param>
    /// <returns><see langword="true"/> when <paramref name="candidate"/> ranks first.</returns>
    private static bool IsLarger(ChartItem candidate, ChartItem existing) =>
        candidate.Size != existing.Size
            ? candidate.Size > existing.Size
            : StringComparer.CurrentCultureIgnoreCase.Compare(candidate.Label, existing.Label) < 0;

    /// <summary>
    /// Releases the slots of items that left the named set and gives each newly named item the lowest free slot.
    /// </summary>
    /// <param name="named">The keys of the items that get their own slice, largest first.</param>
    private void AssignSlots(IEnumerable<string> named)
    {
        string[] ordered = [.. named];
        var current = ordered.ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _slots.Keys.Where(key => !current.Contains(key)).ToArray())
        {
            _slots.Remove(stale);
        }

        foreach (var key in ordered)
        {
            if (_slots.ContainsKey(key))
            {
                continue;
            }

            var slot = 0;
            while (_slots.ContainsValue(slot))
            {
                slot++;
            }

            _slots[key] = slot;
        }
    }
}
