using System.Collections.Concurrent;

namespace RapidPoint;

internal sealed class LowLatencyInputDispatcher : IDisposable
{
    private readonly ConcurrentQueue<(Keys Key, bool Pressed)> _queue = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly Action<Keys, bool> _handler;
    private readonly Action<Exception> _errorHandler;
    private readonly Thread _worker;
    private volatile bool _stopping;

    public LowLatencyInputDispatcher(Action<Keys, bool> handler, Action<Exception> errorHandler)
    {
        _handler = handler;
        _errorHandler = errorHandler;
        _worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "RapidPoint 저지연 입력",
            Priority = ThreadPriority.AboveNormal
        };
        _worker.Start();
    }

    public void Enqueue(Keys key, bool pressed)
    {
        if (_stopping)
        {
            return;
        }
        _queue.Enqueue((key, pressed));
        try
        {
            _signal.Set();
        }
        catch (ObjectDisposedException)
        {
            // 종료와 동시에 들어온 마지막 입력은 폐기합니다.
        }
    }

    private void Run()
    {
        while (true)
        {
            _signal.WaitOne();
            if (_stopping)
            {
                return;
            }

            while (_queue.TryDequeue(out var input))
            {
                if (_stopping)
                {
                    return;
                }
                try
                {
                    _handler(input.Key, input.Pressed);
                }
                catch (Exception exception)
                {
                    try
                    {
                        _errorHandler(exception);
                    }
                    catch
                    {
                        // 오류 표시 실패가 입력 처리 스레드까지 중단시키지 않게 합니다.
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        if (_stopping)
        {
            return;
        }
        _stopping = true;
        while (_queue.TryDequeue(out _))
        {
        }
        _signal.Set();
        if (_worker.Join(TimeSpan.FromSeconds(2)))
        {
            _signal.Dispose();
        }
    }
}
