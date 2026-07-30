using System.Collections.Concurrent;
using System.Diagnostics;
using RedLoader;

namespace LiveEditor.Core;

/// <summary>
/// The only component allowed to call game APIs. Everything else runs on socket/
/// thread-pool threads and must go through <see cref="Enqueue"/>. See PLAN.md 3.3 —
/// off-main-thread game calls are the #1 hard-crash source in this design.
///
/// The deadline lives on the job rather than around the await. Timing out the waiter
/// alone leaves the job sitting in the queue: during a scene load OnUpdate stops
/// ticking, every waiter reports E_TIMEOUT, and then the whole backlog fires at once
/// — re-applying commands the client was already told had failed.
///
/// NOTE the frame budget bounds how many jobs START per frame, not how long any one
/// job runs. A single handler that walks the whole item database still blocks the
/// main thread for as long as it takes.
/// </summary>
public sealed class MainThreadDispatcher
{
    private sealed record Job(
        Func<CommandResult> Execute,
        TaskCompletionSource<CommandResult> Completion,
        long DeadlineTimestamp);

    private readonly ConcurrentQueue<Job> _queue = new();
    private readonly TimeSpan _frameBudget;
    private readonly TimeSpan _jobDeadline;

    public MainThreadDispatcher(TimeSpan? frameBudget = null, TimeSpan? jobDeadline = null)
    {
        _frameBudget = frameBudget ?? TimeSpan.FromMilliseconds(3);
        _jobDeadline = jobDeadline ?? TimeSpan.FromSeconds(10);
    }

    public void Start() => GlobalEvents.OnUpdate.Subscribe(OnUpdate);

    public void Stop()
    {
        GlobalEvents.OnUpdate.Unsubscribe(OnUpdate);
        DrainAbandoned();
    }

    /// <summary>
    /// Queues work to run on the Unity main thread. The returned task always
    /// completes — with a result, an error result, or a timeout result — so callers
    /// never need their own timeout wrapper.
    /// </summary>
    public Task<CommandResult> Enqueue(Func<CommandResult> work)
    {
        var tcs = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deadline = Stopwatch.GetTimestamp() + (long)(_jobDeadline.TotalSeconds * Stopwatch.Frequency);
        _queue.Enqueue(new Job(work, tcs, deadline));
        return tcs.Task;
    }

    // Runs on the Unity main thread every frame.
    private void OnUpdate()
    {
        if (_queue.IsEmpty) return;

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < _frameBudget && _queue.TryDequeue(out var job))
        {
            // Expired while queued: report it and DO NOT run it. Running now would
            // apply a change the client already gave up on.
            if (Stopwatch.GetTimestamp() > job.DeadlineTimestamp)
            {
                job.Completion.TrySetResult(CommandResult.Error(
                    ErrorCodes.Timeout, "expired in queue before the main thread drained it; not executed"));
                continue;
            }

            try
            {
                job.Completion.TrySetResult(job.Execute());
            }
            catch (Exception ex)
            {
                job.Completion.TrySetResult(CommandResult.Error(ErrorCodes.ExecFailed, ex.ToString()));
            }
        }
    }

    private void DrainAbandoned()
    {
        while (_queue.TryDequeue(out var job))
        {
            job.Completion.TrySetResult(CommandResult.Error(
                ErrorCodes.Timeout, "dispatcher stopped before this command ran"));
        }
    }
}
