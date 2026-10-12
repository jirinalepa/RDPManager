using System.Windows.Input;

namespace RDPManager.Docking;

/// <summary>Parses and formats hotkeys like "Ctrl+Alt+R".</summary>
public record HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    public static HotkeyGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var modifiers = ModifierKeys.None;
        Key? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win": modifiers |= ModifierKeys.Windows; break;
                default:
                    if (Enum.TryParse<Key>(part, true, out var k))
                        key = k;
                    else
                        return null;
                    break;
            }
        }
        return key is { } kk && modifiers != ModifierKeys.None ? new HotkeyGesture(modifiers, kk) : null;
    }

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    public uint NativeModifiers =>
        (Modifiers.HasFlag(ModifierKeys.Control) ? NativeMethods.MOD_CONTROL : 0) |
        (Modifiers.HasFlag(ModifierKeys.Alt) ? NativeMethods.MOD_ALT : 0) |
        (Modifiers.HasFlag(ModifierKeys.Shift) ? NativeMethods.MOD_SHIFT : 0) |
        (Modifiers.HasFlag(ModifierKeys.Windows) ? NativeMethods.MOD_WIN : 0) |
        NativeMethods.MOD_NOREPEAT;

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }
}
