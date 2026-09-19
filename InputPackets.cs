using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RapidPoint;

// Immutable packets shared across workers. No packet arrays/LINQ in the warmed-up input path.
internal static class InputPackets
{
    private readonly record struct PacketKey(int Kind, Keys First, Keys Second, int Count, MouseButtonKind Button, bool Down);
    private static readonly ConcurrentDictionary<PacketKey, NativeMethods.Input[]> Cache = new();
    private static readonly int InputSize = Marshal.SizeOf<NativeMethods.Input>();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, NativeMethods.Input[] inputs, int size);

    internal static NativeMethods.Input[] KeyChange(Keys[] keys, bool down)
    {
        if (keys.Length is < 1 or > 2) throw new ArgumentException("키 조합은 1~2개여야 합니다.");
        return Get(new(0, keys[0], keys.Length == 2 ? keys[1] : Keys.None, keys.Length, default, down));
    }
    internal static NativeMethods.Input[] MouseChange(MouseButtonKind button, bool down) => Get(new(1, 0, 0, 0, button, down));
    internal static NativeMethods.Input[] Click(MouseButtonKind button) => Get(new(2, 0, 0, 0, button, false));
    internal static NativeMethods.Input[] Tap(Keys key) => Get(new(3, key, 0, 0, default, false));
    internal static NativeMethods.Input[] TapAndClick(Keys key, MouseButtonKind button) => Get(new(4, key, 0, 0, button, false));

    private static NativeMethods.Input[] Get(PacketKey key) => Cache.GetOrAdd(key, static item => item.Kind switch
    {
        0 => item.Count == 1 ? [Key(item.First, item.Down)] : [Key(item.First, item.Down), Key(item.Second, item.Down)],
        1 => [Mouse(item.Button, item.Down)],
        2 => [Mouse(item.Button, true), Mouse(item.Button, false)],
        3 => [Key(item.First, true), Key(item.First, false)],
        _ => [Key(item.First, true), Key(item.First, false), Mouse(item.Button, true), Mouse(item.Button, false)]
    });

    private static NativeMethods.Input Key(Keys key, bool down)
    {
        var extended = key is Keys.Left or Keys.Right or Keys.Up or Keys.Down
            or Keys.Insert or Keys.Delete or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown
            or Keys.NumLock or Keys.Divide or Keys.RControlKey or Keys.RMenu;
        return new NativeMethods.Input
        {
            Type = NativeMethods.InputKeyboard,
            Union = new NativeMethods.InputUnion { Keyboard = new NativeMethods.KeyboardInput
            {
                VirtualKey = (ushort)key,
                Flags = (extended ? NativeMethods.KeyEventExtendedKey : 0) | (down ? 0 : NativeMethods.KeyEventKeyUp)
            } }
        };
    }

    private static NativeMethods.Input Mouse(MouseButtonKind button, bool down) => new()
    {
        Type = NativeMethods.InputMouse,
        Union = new NativeMethods.InputUnion { Mouse = new NativeMethods.MouseInput
        {
            Flags = (button, down) switch
            {
                (MouseButtonKind.Right, true) => NativeMethods.MouseEventRightDown,
                (MouseButtonKind.Right, false) => NativeMethods.MouseEventRightUp,
                (MouseButtonKind.Middle, true) => NativeMethods.MouseEventMiddleDown,
                (MouseButtonKind.Middle, false) => NativeMethods.MouseEventMiddleUp,
                (_, true) => NativeMethods.MouseEventLeftDown,
                _ => NativeMethods.MouseEventLeftUp
            }
        } }
    };

    internal static void Send(NativeMethods.Input[] packet) => Send(packet,
        static inputs => SendInput((uint)inputs.Length, inputs, InputSize));

    internal static void Send(NativeMethods.Input[] packet, Func<NativeMethods.Input[], uint> send)
    {
        var count = send(packet);
        if (count == packet.Length) return;
        var error = Marshal.GetLastWin32Error();
        // Only the failure path allocates. Never replay downs: that could activate a skill twice.
        // Release every down requested by an incomplete packet, including partial chord insertion.
        var releases = new List<NativeMethods.Input>();
        foreach (var input in packet.Reverse())
        {
            var release = input;
            if (input.Type == NativeMethods.InputKeyboard && (input.Union.Keyboard.Flags & NativeMethods.KeyEventKeyUp) == 0)
                release.Union.Keyboard.Flags |= NativeMethods.KeyEventKeyUp;
            else if (input.Type == NativeMethods.InputMouse && (input.Union.Mouse.Flags &
                (NativeMethods.MouseEventLeftDown | NativeMethods.MouseEventRightDown | NativeMethods.MouseEventMiddleDown)) != 0)
                release.Union.Mouse.Flags <<= 1;
            else
                continue;
            releases.Add(release);
        }
        var cleanupFailed = releases.Count > 0 && send(releases.ToArray()) != releases.Count;
        throw new Win32Exception(error, $"Windows 입력 전송 실패 ({count}/{packet.Length}). 반복을 중지했습니다." +
            (cleanupFailed ? " 입력 해제도 실패했습니다. 대상 창과 실행 권한을 확인하세요." : ""));
    }
}
