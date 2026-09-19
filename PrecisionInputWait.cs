using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RapidPoint;

// One timer per running macro, reused for every hold, gap and repeat delay.
internal sealed class PrecisionInputWait : IDisposable
{
    private sealed class TimerHandle : WaitHandle
    {
        internal TimerHandle(IntPtr handle) => SafeWaitHandle = new SafeWaitHandle(handle, true);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period,
        IntPtr callback, IntPtr argument, bool resume);

    private readonly TimerHandle? _timer;
    private readonly WaitHandle[] _handles = new WaitHandle[2];
    internal bool HighResolutionAvailable => _timer != null;

    internal PrecisionInputWait(bool forceFallback = false)
    {
        if (forceFallback) return;
        var handle = CreateWaitableTimerExW(IntPtr.Zero, null, 2, 0x00100002);
        if (handle != IntPtr.Zero) _timer = new TimerHandle(handle);
    }

    internal void Delay(double milliseconds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (milliseconds <= 0) return;
        var deadline = Stopwatch.GetTimestamp() + milliseconds * Stopwatch.Frequency / 1000d;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var remaining = (deadline - Stopwatch.GetTimestamp()) * 1000d / Stopwatch.Frequency;
            if (remaining <= 0) return;
            // Never shorten a later hold/gap to compensate for an earlier scheduling delay.
            if (_timer != null)
            {
                var due = -Math.Max(1L, (long)Math.Ceiling(remaining * 10000));
                if (SetWaitableTimer(_timer.SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                {
                    _handles[0] = token.WaitHandle;
                    _handles[1] = _timer;
                    if (WaitHandle.WaitAny(_handles) == 0) token.ThrowIfCancellationRequested();
                    continue;
                }
            }
            // Older systems: sleep most of the interval, then a bounded final spin.
            if (remaining > 1)
                token.WaitHandle.WaitOne(Math.Max(1, (int)Math.Floor(remaining)));
            else
                Thread.SpinWait(64);
        }
    }

    public void Dispose() => _timer?.Dispose();
}
