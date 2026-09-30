using System.Reflection;
using System.Text.Json;

namespace RapidPoint;

internal static class EditorUsabilityTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, Private)!.GetValue(instance)!;

    internal static void Run()
    {
        if (new RapidMacroSettings().StableInput) throw new Exception("New macros must default to fast mode.");
        foreach (var enabled in new[] { false, true })
        {
            var saved = JsonSerializer.Deserialize<RapidMacroSettings>(
                $"{{\"StableInput\":{enabled.ToString().ToLowerInvariant()}}}", SettingsStore.JsonOptions)!;
            var binding = new CoordinateBindingSettings();
            using var dialog = new MacroEditorDialog(saved, [binding], []);
            var check = Field<CheckBox>(dialog, "_stableInput");
            var hold = Field<NumericUpDown>(dialog, "_sequenceHoldInput");
            if (check.Checked != enabled || hold.Enabled != enabled)
                throw new Exception("Saved preference or timing controls changed on open.");
            // Switching to protected mouse steps must not overwrite the standard preference.
            var first = Field<ComboBox>(dialog, "_firstStepTypeInput");
            first.SelectedIndex = 1;
            if (!hold.Enabled || hold.Minimum != 20 || check.Checked != enabled)
                throw new Exception("Mouse protection changed preference or lost its timing floor.");
            first.SelectedIndex = 0;
            if (check.Checked != enabled || hold.Enabled != enabled || hold.Minimum != 1)
                throw new Exception("Returning to keyboard steps changed standard-mode preference.");
            var mode = Field<ComboBox>(dialog, "_sequenceModeInput");
            mode.SelectedIndex = 1;
            if (!hold.Enabled) throw new Exception("Pause sequence timings were disabled by fast-mode default.");
            mode.SelectedIndex = 0;
            typeof(MacroEditorDialog).GetMethod("SaveAndClose", Private)!.Invoke(dialog, null);
            if (dialog.Result?.StableInput != enabled || dialog.Result.PauseSkillSequence)
                throw new Exception("Editor did not preserve saved safety preference.");
        }
        if (KeyFormatter.Format(Keys.D1) != "1" || KeyFormatter.Format(Keys.D0) != "0" ||
            KeyFormatter.Format(Keys.Escape) != "ESC" || KeyFormatter.Format(Keys.NumPad1) != "NUMPAD1")
            throw new Exception("Friendly key labels are incorrect.");

        // Exercise empty-state guidance on actual controls without starting MainForm hooks.
        using var host = new Form();
        using var panel = new Panel { Dock = DockStyle.Fill };
        using var list = new ListView { Bounds = new Rectangle(0, 0, 400, 140) };
        using var edit = new Button();
        using var delete = new Button();
        host.Controls.Add(panel);
        panel.Controls.AddRange([list, edit, delete]);
        _ = host.Handle;
        _ = list.Handle;
        typeof(MainForm).GetMethod("ConfigureListGuidance", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [panel, list, edit, delete, "empty"]);
        if (edit.Enabled || delete.Enabled) throw new Exception("Empty lists enabled edit/delete.");
        list.Items.Add("example").Selected = true;
        if (!edit.Enabled || !delete.Enabled) throw new Exception("Selection did not enable edit/delete.");
        list.Items.Clear();
        list.Invalidate();
        if (edit.Enabled || delete.Enabled) throw new Exception("Delete-last did not disable edit/delete.");
    }
}
