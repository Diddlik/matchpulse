using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MatchPulse;

/// <summary>
/// A system-wide hotkey such as <c>Ctrl+Alt+M</c>, registered with Windows (RegisterHotKey), so it works while the game
/// has the focus. It only listens for its own key combination; it does not see any other keys.
/// </summary>
public sealed class Hotkey : NativeWindow, IDisposable
{
    const int WmHotkey = 0x0312, Id = 1;
    const uint NoRepeat = 0x4000;
    readonly Action _pressed;
    bool _registered;

    public Hotkey(Action pressed)
    {
        _pressed = pressed;
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // message-only window
    }

    /// <summary>Registers <paramref name="text"/> (replacing the previous one); empty clears it. False when Windows refuses
    /// it, usually because another program already uses the combination.</summary>
    public bool Set(string text)
    {
        if (_registered) UnregisterHotKey(Handle, Id);
        _registered = false;
        if (text == "") return true;
        if (Parse(text) is not { } key) return false;
        _registered = RegisterHotKey(Handle, Id, key.Modifiers | NoRepeat, (uint)key.Key);
        return _registered;
    }

    /// <summary>Parses "Ctrl+Alt+M", "Shift+F9", "Win+Ctrl+P": at least one of Ctrl, Alt, Shift, Win and one key
    /// (A-Z, 0-9, F1-F24, Pause, Insert, Home, End, PageUp, PageDown, NumPad0-9). Null when not valid.</summary>
    public static (uint Modifiers, Keys Key)? Parse(string text)
    {
        uint modifiers = 0;
        Keys? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "strg" or "control": modifiers |= 0x2; break;
                case "alt": modifiers |= 0x1; break;
                case "shift" or "umschalt": modifiers |= 0x4; break;
                case "win": modifiers |= 0x8; break;
                default:
                    if (key is not null) return null;
                    var name = part.Length == 1 && char.IsAsciiDigit(part[0]) ? "D" + part : part;
                    if (!Enum.TryParse<Keys>(name, ignoreCase: true, out var k) || !Allowed(k)) return null;
                    key = k;
                    break;
            }
        }
        return modifiers != 0 && key is { } found ? (modifiers, found) : null;
    }

    static bool Allowed(Keys k) =>
        k is >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9 or >= Keys.F1 and <= Keys.F24 or >= Keys.NumPad0 and <= Keys.NumPad9
            or Keys.Pause or Keys.Insert or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam == Id) _pressed();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_registered) UnregisterHotKey(Handle, Id);
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
