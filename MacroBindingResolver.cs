namespace RapidPoint;

internal static class MacroBindingResolver
{
    internal static CoordinateBindingSettings? FindFirstKeyBinding(
        RapidMacroSettings macro, IEnumerable<CoordinateBindingSettings> bindings) =>
        !macro.PauseSkillSequence && macro.KeyboardEnabled && macro.UseFirstKeyBinding &&
        macro.FirstStepKind == MacroFirstStepKind.KeyboardKey
            ? bindings.FirstOrDefault(binding => binding.Enabled && binding.TriggerKey == macro.KeyboardKey)
            : null;
}
