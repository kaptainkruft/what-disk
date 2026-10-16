using System.Diagnostics.CodeAnalysis;

namespace WhatDisk.Scanning;

/// <summary>
/// A blocking last-in-first-out work queue shared by scan workers. It completes automatically when every item that
/// was added has been taken and marked done, which happens exactly when the scan has run out of directories.
/// </summary>
/// <remarks>
/// Last-in-first-out order makes the traversal depth-first, which keeps the queue small and lets subtrees finish
/// early.
/// </remarks>
/// <typeparam name="T">The type of work item.</typeparam>
internal sealed class WorkQueue<T> : IDisposable
{
    /// <summary>Guards <see cref="_items"/>, <see cref="_outstanding"/> and <see cref="_completed"/>.</summary>
    private readonly Lock _gate = new();

    /// <summary>The items waiting to be taken.</summary>
    private readonly Stack<T> _items = new();

    /// <summary>Counts items available to take, plus one extra permit once the queue completes.</summary>
    private readonly SemaphoreSlim _available = new(0);

    /// <summary>The number of items added but not yet marked done.</summary>
    private int _outstanding;

    /// <summary>Whether the queue has completed.</summary>
    private bool _completed;

    /// <summary>
    /// Gets a value indicating whether the queue has completed.
    /// </summary>
    public bool IsCompleted
    {
        get
        {
            lock (_gate)
            {
                return _completed;
            }
        }
    }

    /// <summary>
    /// Gets the number of items waiting to be taken.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>
    /// Adds an item. Each added item must eventually be passed to <see cref="MarkDone"/> after it is taken.
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <exception cref="InvalidOperationException">The queue has already completed.</exception>
    public void Add(T item)
    {
        lock (_gate)
        {
            if (_completed)
            {
                throw new InvalidOperationException("Items cannot be added to a completed work queue.");
            }

            _items.Push(item);
            _outstanding++;
        }

        _available.Release();
    }

    /// <summary>
    /// Takes the most recently added item, blocking until one is available or the queue completes.
    /// </summary>
    /// <param name="item">The item taken, when the method returns <see langword="true"/>.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns><see langword="true"/> when an item was taken; <see langword="false"/> when the queue has completed.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public bool TryTake([MaybeNullWhen(false)] out T item, CancellationToken cancellationToken)
    {
        while (true)
        {
            _available.Wait(cancellationToken);
            lock (_gate)
            {
                if (_items.TryPop(out item))
                {
                    return true;
                }

                if (_completed)
                {
                    break;
                }
            }
        }

        // Pass the completion permit on so that every waiting consumer observes completion.
        _available.Release();
        return false;
    }

    /// <summary>
    /// Marks a previously taken item as done. Completes the queue when nothing remains outstanding.
    /// </summary>
    public void MarkDone()
    {
        lock (_gate)
        {
            if (_outstanding <= 0)
            {
                throw new InvalidOperationException("More items were marked done than were added.");
            }

            if (--_outstanding > 0)
            {
                return;
            }

            _completed = true;
        }

        _available.Release();
    }

    /// <summary>
    /// Completes the queue if nothing is outstanding, for example when there was never anything to add.
    /// </summary>
    public void CompleteIfIdle()
    {
        lock (_gate)
        {
            if (_outstanding > 0 || _completed)
            {
                return;
            }

            _completed = true;
        }

        _available.Release();
    }

    /// <inheritdoc/>
    public void Dispose() => _available.Dispose();
}
