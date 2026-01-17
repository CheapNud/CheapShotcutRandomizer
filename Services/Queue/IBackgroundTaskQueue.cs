namespace CheapShotcutRandomizer.Services.Queue;

/// <summary>
/// Interface for background task queue
/// </summary>
public interface IBackgroundTaskQueue
{
    /// <summary>
    /// Queue a background work item
    /// </summary>
    ValueTask QueueBackgroundWorkItemAsync(Func<CancellationToken, ValueTask> workItem);

    /// <summary>
    /// Dequeue a background work item (blocks until item available)
    /// </summary>
    ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(CancellationToken cancellationToken);
}
