using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RapidPoint;

internal static class NativeMethods
{
    internal const int WhKeyboardLl = 13;
    internal const int WmKeyDown = 0x0100;
    internal const int WmKeyUp = 0x0101;
    internal const int WmSysKeyDown = 0x0104;
    internal const int WmSysKeyUp = 0x0105;
    internal const uint InputMouse = 0;
    internal const uint InputKeyboard = 1;
    internal const uint MouseEventLeftDown = 0x0002;
    internal const uint MouseEventLeftUp = 0x0004;
    internal const uint MouseEventRightDown = 0x0008;
    internal const uint MouseEventRightUp = 0x0010;
    internal const uint MouseEventMiddleDown = 0x0020;
    internal const uint MouseEventMiddleUp = 0x0040;
    internal const uint KeyEventExtendedKey = 0x0001;
    internal const uint KeyEventKeyUp = 0x0002;
    internal const uint LowLevelKeyboardInjected = 0x00000010;
    internal const uint GaRoot = 2;
    internal const int SwRestore = 9;

    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    internal delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardHookData
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(IntPtr window, out Rect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ScreenToClient(IntPtr window, ref Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(IntPtr window, ref Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowText(IntPtr window, char[] text, int maximumCount);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProc callback,
        IntPtr moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hookHandle, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("winmm.dll")]
    internal static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    internal static extern uint timeEndPeriod(uint period);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    internal static void Click(MouseButtonKind button)
    {
        InputPackets.Send(InputPackets.Click(button));
    }

    internal static void SetMouseButton(MouseButtonKind button, bool pressed)
    {
        InputPackets.Send(InputPackets.MouseChange(button, pressed));
    }

    internal static void SetKeys(Keys[] keys, bool pressed)
    {
        InputPackets.Send(InputPackets.KeyChange(keys, pressed));
    }

    internal static void PressKey(Keys key)
    {
        InputPackets.Send(InputPackets.Tap(key));
    }

    internal static void PressKeyAndClick(Keys key, MouseButtonKind button)
    {
        InputPackets.Send(InputPackets.TapAndClick(key, button));
    }

    internal static string GetWindowTitle(IntPtr window)
    {
        var buffer = new char[512];
        var length = GetWindowText(window, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : "제목 없는 창";
    }
}

internal sealed class GlobalKeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private readonly HashSet<Keys> _heldKeys = [];
    private readonly object _heldKeysSync = new();
    private IntPtr _hookHandle;

    public event Action<Keys, bool>? HotkeyChanged;
    public bool SuppressHotkeys { get; set; } = true;
    public Func<Keys, bool>? IsTrackedKey { get; set; }
    public bool IsInstalled => _hookHandle != IntPtr.Zero;

    public void ResetHeldKeys()
    {
        lock (_heldKeysSync)
        {
            _heldKeys.Clear();
        }
    }

    public GlobalKeyboardHook()
    {
        _callback = HookCallback;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = NativeMethods.GetModuleHandle(module?.ModuleName);
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _callback, moduleHandle, 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            var isDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
            var isUp = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;
            if (isDown || isUp)
            {
                var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(lParam);
                if ((data.Flags & NativeMethods.LowLevelKeyboardInjected) != 0)
                {
                    return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
                }

                var key = (Keys)data.VkCode;
                var isTracked = IsTrackedKey?.Invoke(key) ?? false;
                if (isDown && isTracked)
                {
                    var firstPress = false;
                    lock (_heldKeysSync)
                    {
                        firstPress = _heldKeys.Add(key);
                    }
                    if (firstPress)
                    {
                        HotkeyChanged?.Invoke(key, true);
                    }

                    if (SuppressHotkeys)
                    {
                        return (IntPtr)1;
                    }
                }
                else if (isUp)
                {
                    bool wasHeld;
                    lock (_heldKeysSync)
                    {
                        wasHeld = _heldKeys.Remove(key);
                    }
                    if (wasHeld)
                    {
                        HotkeyChanged?.Invoke(key, false);
                    }

                    if (wasHeld && SuppressHotkeys)
                    {
                        return (IntPtr)1;
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookHandle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }
}
