using System.Text.Json;
using System.Text.Json.Serialization;

namespace RapidPoint;

internal enum MouseButtonKind
{
    Left,
    Right,
    Middle
}

internal enum RepeatMode
{
    Toggle,
    Hold,
    Count
}

internal enum InputActionKind
{
    MouseClick,
    MouseMove,
    KeyboardKey
}

internal enum CoordinateSpace
{
    Screen,
    TargetWindow,
    CurrentCursor
}

internal enum BindingActivation
{
    SinglePress,
    RepeatWhileHeld,
    ReleaseClick
}

internal enum MacroFirstStepKind
{
    KeyboardKey,
    MouseClick
}

internal enum PauseSequenceKind
{
    Skill,
    ClickThenEscape,
    EscapeThenClick,
    EscapeWithKey
}

internal sealed class CoordinateBindingSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "바인딩 1";
    public int TriggerKey { get; set; } = (int)Keys.D1;
    public int X { get; set; } = 640;
    public int Y { get; set; } = 360;
    public CoordinateSpace CoordinateSpace { get; set; } = CoordinateSpace.Screen;
    public int ReferenceWidth { get; set; } = 1280;
    public int ReferenceHeight { get; set; } = 720;
    public InputActionKind MouseAction { get; set; } = InputActionKind.MouseClick;
    public MouseButtonKind MouseButton { get; set; } = MouseButtonKind.Left;
    public BindingActivation Activation { get; set; } = BindingActivation.SinglePress;
    public int IntervalMs { get; set; } = 50;
    public bool Enabled { get; set; } = true;

    public CoordinateBindingSettings Clone() => new()
    {
        Id = Id,
        Name = Name,
        TriggerKey = TriggerKey,
        X = X,
        Y = Y,
        CoordinateSpace = CoordinateSpace,
        ReferenceWidth = ReferenceWidth,
        ReferenceHeight = ReferenceHeight,
        MouseAction = MouseAction,
        MouseButton = MouseButton,
        Activation = Activation,
        IntervalMs = IntervalMs,
        Enabled = Enabled
    };
}

internal sealed class RapidMacroSettings
{
    public const string CurrentCursorBindingId = "__CURRENT_CURSOR__";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "연타 매크로 1";
    public int TriggerKey { get; set; } = (int)Keys.X;
    public bool KeyboardEnabled { get; set; } = true;
    public bool UseFirstKeyBinding { get; set; } = true;
    public bool PauseSkillSequence { get; set; }
    public bool StableInput { get; set; } = true;
    public PauseSequenceKind PauseSequenceKind { get; set; }
    public bool PauseAfterClick { get; set; } = true;
    public int SequenceHoldMs { get; set; } = 10;
    public int BeforeClickMs { get; set; } = 10;
    public int BeforePauseMs { get; set; } = 10;
    public MacroFirstStepKind FirstStepKind { get; set; } = MacroFirstStepKind.KeyboardKey;
    public int KeyboardKey { get; set; } = (int)Keys.D1;
    public MouseButtonKind FirstStepMouseButton { get; set; } = MouseButtonKind.Left;
    public bool MouseEnabled { get; set; } = true;
    public string BindingId { get; set; } = CurrentCursorBindingId;
    public int IntervalMs { get; set; } = 20;
    public RepeatMode RepeatMode { get; set; } = RepeatMode.Hold;
    public int RepeatCount { get; set; } = 100;
    public bool Enabled { get; set; } = true;

    public RapidMacroSettings Clone() => new()
    {
        Id = Id,
        Name = Name,
        TriggerKey = TriggerKey,
        KeyboardEnabled = KeyboardEnabled,
        UseFirstKeyBinding = UseFirstKeyBinding,
        PauseSkillSequence = PauseSkillSequence,
        StableInput = StableInput,
        PauseSequenceKind = PauseSequenceKind,
        PauseAfterClick = PauseAfterClick,
        SequenceHoldMs = SequenceHoldMs,
        BeforeClickMs = BeforeClickMs,
        BeforePauseMs = BeforePauseMs,
        FirstStepKind = FirstStepKind,
        KeyboardKey = KeyboardKey,
        FirstStepMouseButton = FirstStepMouseButton,
        MouseEnabled = MouseEnabled,
        BindingId = BindingId,
        IntervalMs = IntervalMs,
        RepeatMode = RepeatMode,
        RepeatCount = RepeatCount,
        Enabled = Enabled
    };
}

internal sealed class AppSettings
{
    public int X { get; set; } = 640;
    public int Y { get; set; } = 360;
    public CoordinateSpace CoordinateSpace { get; set; } = CoordinateSpace.Screen;
    public MouseButtonKind MouseButton { get; set; } = MouseButtonKind.Left;
    public int KeyboardKey { get; set; } = (int)Keys.D1;
    public int CoordinateMappingKey { get; set; } = (int)Keys.D1;
    public int MacroTriggerKey { get; set; } = (int)Keys.X;
    public bool CoordinateMappingEnabled { get; set; } = true;
    public bool MacroKeyboardEnabled { get; set; } = true;
    public bool MacroMouseEnabled { get; set; } = true;
    public InputActionKind MacroMouseAction { get; set; } = InputActionKind.MouseClick;
    public int IntervalMs { get; set; } = 20;
    public RepeatMode RepeatMode { get; set; } = RepeatMode.Hold;
    public int RepeatCount { get; set; } = 100;
    public bool SuppressHotkeys { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool ShowCoordinateMonitor { get; set; }
    public int ReferenceWidth { get; set; } = 1280;
    public int ReferenceHeight { get; set; } = 720;
    public string TargetProcessName { get; set; } = string.Empty;
    public string TargetWindowTitle { get; set; } = string.Empty;
    public List<CoordinateBindingSettings> Bindings { get; set; } = [];
    public string MacroBindingId { get; set; } = string.Empty;
    public List<RapidMacroSettings> Macros { get; set; } = [];
    public bool MultipleMacrosInitialized { get; set; }
}

internal static class SettingsStore
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RapidPoint");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // 설정 저장 실패가 클릭 실행을 방해하지 않도록 무시합니다.
        }
    }
}

internal static class KeyFormatter
{
    public static string Format(Keys key) => key switch
    {
        Keys.Space => "SPACE",
        Keys.Return => "ENTER",
        Keys.Back => "BACKSPACE",
        Keys.Escape => "ESC",
        Keys.ControlKey => "CTRL",
        Keys.ShiftKey => "SHIFT",
        Keys.Menu => "ALT",
        Keys.Oemcomma => ",",
        Keys.OemPeriod => ".",
        Keys.OemMinus => "-",
        Keys.Oemplus => "+",
        _ => key.ToString().ToUpperInvariant()
    };
}
