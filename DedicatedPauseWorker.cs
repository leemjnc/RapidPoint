using System.Collections.Concurrent;

namespace RapidPoint;

internal sealed class DedicatedPauseWorker
{
    internal static readonly DedicatedPauseWorker Shared = new();
    private readonly BlockingCollection<(Action Work, TaskCompletionSource Done)> _queue = new();

    private DedicatedPauseWorker()
    {
        var thread = new Thread(() =>
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                try { item.Work(); item.Done.SetResult(); }
                catch (Exception error) { item.Done.SetException(error); }
            }
        }) { IsBackground = true, Name = "RapidPoint 퍼즈 입력", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    internal Task Enqueue(Action work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add((work, done));
        return done.Task;
    }
}
