using WhatDisk.Scanning;

namespace WhatDisk.Tests.Scanning;

/// <summary>
/// Tests the blocking LIFO work queue that feeds scan workers.
/// </summary>
public sealed class WorkQueueTests
{
    /// <summary>
    /// Items are taken most-recent first.
    /// </summary>
    [Fact]
    public void TryTake_ReturnsItemsInLastInFirstOutOrder()
    {
        using var queue = new WorkQueue<int>();
        queue.Add(1);
        queue.Add(2);
        queue.Add(3);

        Assert.True(queue.TryTake(out var first, TestContext.Current.CancellationToken));
        Assert.True(queue.TryTake(out var second, TestContext.Current.CancellationToken));
        Assert.Equal(3, first);
        Assert.Equal(2, second);
        Assert.Equal(1, queue.Count);
    }

    /// <summary>
    /// The queue completes once every item has been marked done, and waiting consumers are released.
    /// </summary>
    [Fact]
    public async Task MarkDone_CompletesTheQueueAndReleasesEveryConsumer()
    {
        using var queue = new WorkQueue<int>();
        queue.Add(1);
        Assert.True(queue.TryTake(out _, TestContext.Current.CancellationToken));

        var consumers = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => queue.TryTake(out var _, CancellationToken.None), TestContext.Current.CancellationToken))
            .ToArray();
        queue.MarkDone();

        var results = await Task.WhenAll(consumers).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.All(results, Assert.False);
        Assert.True(queue.IsCompleted);
    }

    /// <summary>
    /// Items added while processing keep the queue open until they are done too.
    /// </summary>
    [Fact]
    public void MarkDone_DoesNotCompleteWhileChildItemsAreOutstanding()
    {
        using var queue = new WorkQueue<string>();
        queue.Add("parent");
        Assert.True(queue.TryTake(out _, TestContext.Current.CancellationToken));
        queue.Add("child");
        queue.MarkDone();

        Assert.False(queue.IsCompleted);
        Assert.True(queue.TryTake(out var child, TestContext.Current.CancellationToken));
        Assert.Equal("child", child);
        queue.MarkDone();
        Assert.True(queue.IsCompleted);
    }

    /// <summary>
    /// An empty queue can be completed so that consumers do not wait forever.
    /// </summary>
    [Fact]
    public void CompleteIfIdle_CompletesAnEmptyQueue()
    {
        using var queue = new WorkQueue<int>();
        queue.CompleteIfIdle();

        Assert.True(queue.IsCompleted);
        Assert.False(queue.TryTake(out _, TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(() => queue.Add(1));
    }

    /// <summary>
    /// Marking more items done than were added is a programming error.
    /// </summary>
    [Fact]
    public void MarkDone_ThrowsWhenNothingIsOutstanding()
    {
        using var queue = new WorkQueue<int>();
        Assert.Throws<InvalidOperationException>(queue.MarkDone);
    }

    /// <summary>
    /// Waiting can be cancelled.
    /// </summary>
    [Fact]
    public void TryTake_ThrowsWhenCancelled()
    {
        using var queue = new WorkQueue<int>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        Assert.ThrowsAny<OperationCanceledException>(() => queue.TryTake(out _, cancellation.Token));
    }
}
