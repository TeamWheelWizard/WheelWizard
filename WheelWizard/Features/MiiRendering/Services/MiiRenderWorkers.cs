namespace WheelWizard.MiiRendering.Services;

/// <summary>
/// The threads all Mii rendering work runs on (head builds, still images): a few at most, below normal priority, so a
/// page full of Miis can't take every core and the UI stays smooth even on a dual-core laptop.
/// <para>
/// Urgent work (heads of Miis on screen right now, which are quick) goes before the rest, newest first: the Miis that
/// just scrolled into view load before the ones scrolled past. Other work runs in order.
/// </para>
/// </summary>
public static class MiiRenderWorkers
{
    private static readonly int MaxWorkers = Math.Clamp(Environment.ProcessorCount - 1, 1, 3);

    private static readonly object Lock = new();
    private static readonly LinkedList<Action> Queue = new();
    private static readonly SemaphoreSlim Pending = new(0);
    private static int _workers;
    private static int _idle;

    /// <summary>Queues <paramref name="work"/>; it must not throw.</summary>
    public static void Enqueue(Action work, bool urgent)
    {
        lock (Lock)
        {
            if (urgent)
                Queue.AddFirst(work);
            else
                Queue.AddLast(work);
            if (_idle == 0 && _workers < MaxWorkers)
            {
                _workers++;
                new Thread(Work)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.BelowNormal,
                    Name = "Mii renderer",
                }.Start();
            }
        }

        Pending.Release();
    }

    /// <summary>Runs <paramref name="work"/> on a worker (not urgent); cancelled before it starts, it doesn't run.</summary>
    public static Task<T> Run<T>(Func<T> work, CancellationToken cancellationToken = default)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(
            () =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    result.TrySetResult(work());
                }
                catch (OperationCanceledException exception)
                {
                    result.TrySetCanceled(exception.CancellationToken);
                }
                catch (Exception exception)
                {
                    result.TrySetException(exception);
                }
            },
            urgent: false
        );
        return result.Task;
    }

    private static void Work()
    {
        while (true)
        {
            lock (Lock)
                _idle++;
            Pending.Wait();
            Action? work = null;
            lock (Lock)
            {
                _idle--;
                if (Queue.First is { } first)
                {
                    work = first.Value;
                    Queue.RemoveFirst();
                }
            }

            work?.Invoke();
        }
    }
}
