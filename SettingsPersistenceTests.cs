using System.Text.Json;

namespace RapidPoint;

internal static class SettingsPersistenceTests
{
    internal static void Run()
    {
        // Exercise the same binding normalization used at startup, without touching
        // the user's settings file or installing a keyboard hook via MainForm.
        foreach (var json in new[] { "{}", "{\"Bindings\":[]}", "{\"Bindings\":null}" })
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(json, SettingsStore.JsonOptions)!;
            settings.NormalizeBindings();
            if (settings.Bindings.Count != 0) throw new Exception("Startup created an unwanted binding.");
        }
        var binding = new CoordinateBindingSettings { Name = "유지할 바인딩", TriggerKey = (int)Keys.Q, X = 123, Y = 456 };
        var macro = new RapidMacroSettings { Name = "유지할 매크로", KeyboardKey = (int)Keys.Escape };
        var saved = new AppSettings
        {
            Bindings = [binding], Macros = [macro], MultipleMacrosInitialized = true,
            ShowCoordinateMonitor = true
        };
        saved.NormalizeBindings();
        if (saved.Bindings.Count != 1 || saved.Bindings[0] != binding || binding.X != 123 || binding.Y != 456)
            throw new Exception("Existing binding was modified/replaced.");
        saved.Bindings.Remove(binding); // Delete the final binding, then save and restart repeatedly.
        for (var restart = 0; restart < 5; restart++)
        {
            saved = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(saved, SettingsStore.JsonOptions), SettingsStore.JsonOptions)!;
            saved.NormalizeBindings();
            if (saved.Bindings.Count != 0 || saved.Macros.Count != 1 || saved.Macros[0].Id != macro.Id ||
                saved.Macros[0].KeyboardKey != (int)Keys.Escape || !saved.ShowCoordinateMonitor)
                throw new Exception("Delete/save/restart did not preserve empty bindings and other settings.");
        }
        saved.Bindings.Add(binding.Clone());
        saved.NormalizeBindings();
        if (saved.Bindings.Count != 1 || saved.Bindings[0].Id != binding.Id)
            throw new Exception("Adding a binding after an empty restart failed.");
    }
}
