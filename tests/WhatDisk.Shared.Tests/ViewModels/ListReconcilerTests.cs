using WhatDisk.ViewModels;

namespace WhatDisk.Tests.ViewModels;

/// <summary>
/// Tests the minimal-change list reconciliation used to update the visible tree rows.
/// </summary>
public sealed class ListReconcilerTests
{
    /// <summary>
    /// An identical sequence causes no changes.
    /// </summary>
    [Fact]
    public void Apply_DoesNothingWhenTheSequenceIsUnchanged()
    {
        var items = Items(5);
        var list = new RecordingList(items);

        Assert.False(ListReconciler.Apply(list, items));
        Assert.Empty(list.Operations);
    }

    /// <summary>
    /// Expanding a row inserts its children as one range.
    /// </summary>
    [Fact]
    public void Apply_InsertsANewBlockInOneOperation()
    {
        var items = Items(6);
        var list = new RecordingList([items[0], items[1], items[5]]);

        ListReconciler.Apply(list, items);

        Assert.Equal(items, list);
        Assert.Equal(["insert 2 x3"], list.Operations);
    }

    /// <summary>
    /// Collapsing a row removes its descendants as one range.
    /// </summary>
    [Fact]
    public void Apply_RemovesABlockInOneOperation()
    {
        var items = Items(6);
        var list = new RecordingList(items);

        ListReconciler.Apply(list, [items[0], items[4], items[5]]);

        Assert.Equal([items[0], items[4], items[5]], list);
        Assert.Equal(["remove 1 x3"], list.Operations);
    }

    /// <summary>
    /// When a small row overtakes a large expanded sibling, only the small row moves.
    /// </summary>
    [Fact]
    public void Apply_MovesTheSmallestNumberOfItems()
    {
        var items = Items(102);
        var list = new RecordingList(items);
        List<Item> desired = [items[101], .. items.Take(101)];

        ListReconciler.Apply(list, desired);

        Assert.Equal(desired, list);
        Assert.Equal(["remove 101 x1", "insert 0 x1"], list.Operations);
    }

    /// <summary>
    /// Arbitrary reorderings, insertions and removals always produce the desired sequence.
    /// </summary>
    [Fact]
    public void Apply_ProducesTheDesiredSequenceForRandomChanges()
    {
        var random = new Random(7);
        for (var iteration = 0; iteration < 300; iteration++)
        {
            var pool = Items(40);
            var current = pool.Where(_ => random.NextDouble() < 0.7).OrderBy(_ => random.Next()).ToList();
            var desired = pool.Where(_ => random.NextDouble() < 0.7).OrderBy(_ => random.Next()).ToList();
            var list = new RecordingList(current);

            ListReconciler.Apply(list, desired);

            Assert.Equal(desired, list);
        }
    }

    /// <summary>
    /// Creates distinct items.
    /// </summary>
    /// <param name="count">The number of items.</param>
    /// <returns>The items.</returns>
    private static List<Item> Items(int count) => [.. Enumerable.Range(0, count).Select(static index => new Item(index))];

    /// <summary>
    /// A uniquely identified item.
    /// </summary>
    /// <param name="Id">The item's number.</param>
    private sealed record Item(int Id);

    /// <summary>
    /// A range list that records the operations applied to it.
    /// </summary>
    /// <param name="items">The initial items.</param>
    private sealed class RecordingList(IEnumerable<Item> items) : IRangeList<Item>
    {
        /// <summary>The current items.</summary>
        private readonly List<Item> _items = [.. items];

        /// <summary>
        /// Gets the operations applied, as text.
        /// </summary>
        public List<string> Operations { get; } = [];

        /// <inheritdoc/>
        public int Count => _items.Count;

        /// <inheritdoc/>
        public Item this[int index] => _items[index];

        /// <inheritdoc/>
        public void InsertRange(int index, IReadOnlyList<Item> items)
        {
            Operations.Add($"insert {index} x{items.Count}");
            _items.InsertRange(index, items);
        }

        /// <inheritdoc/>
        public void RemoveRange(int index, int count)
        {
            Operations.Add($"remove {index} x{count}");
            _items.RemoveRange(index, count);
        }

        /// <inheritdoc/>
        public IEnumerator<Item> GetEnumerator() => _items.GetEnumerator();

        /// <inheritdoc/>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
