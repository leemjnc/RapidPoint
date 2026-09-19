using System.Runtime.InteropServices;

namespace RapidPoint;

// Read-only observer. Never suppresses, delays deliberately, or replays input.
internal sealed class MouseClickObserver : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private readonly Action<NativeMethods.Point> _clicked;
    private IntPtr _hook;
    internal bool IsInstalled => _hook != IntPtr.Zero;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseHookData
    {
        public NativeMethods.Point Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    internal MouseClickObserver(Action<NativeMethods.Point> clicked)
    {
        _clicked = clicked;
        _callback = Observe;
        _hook = NativeMethods.SetWindowsHookEx(14, _callback, NativeMethods.GetModuleHandle(null), 0);
    }

    internal static bool IsButtonDown(int message) => message is 0x0201 or 0x0204 or 0x0207 or 0x020B;

    private IntPtr Observe(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && IsButtonDown(message.ToInt32()))
        {
            try
            {
                // Include physical and generated clicks. Capture the event position,
                // not a later cursor sample after the macro may have moved it.
                _clicked(Marshal.PtrToStructure<MouseHookData>(data).Point);
            }
            catch { /* A visual aid must never interrupt mouse input. */ }
        }
        return NativeMethods.CallNextHookEx(_hook, code, message, data);
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }
}
