using Avalonia.Input;

namespace mRemoteNG.Avalonia.Views;

/// <summary>
/// Turns Avalonia's keyboard input into the X11 keysyms VNC sends. Keys that type nothing (Enter, arrows, modifiers,
/// F-keys) go as key presses; typed characters go as the text they produce, so the keyboard layout, Shift and dead
/// keys are applied here, as they are for every other app.
/// </summary>
internal static class VncKeyMap
{
    private static readonly Dictionary<Key, uint> Special = new()
    {
        [Key.Back] = 0xFF08, [Key.Tab] = 0xFF09, [Key.Enter] = 0xFF0D, [Key.Escape] = 0xFF1B,
        [Key.Insert] = 0xFF63, [Key.Delete] = 0xFFFF, [Key.Home] = 0xFF50, [Key.End] = 0xFF57,
        [Key.PageUp] = 0xFF55, [Key.PageDown] = 0xFF56,
        [Key.Left] = 0xFF51, [Key.Up] = 0xFF52, [Key.Right] = 0xFF53, [Key.Down] = 0xFF54,
        [Key.LeftShift] = 0xFFE1, [Key.RightShift] = 0xFFE2, [Key.LeftCtrl] = 0xFFE3, [Key.RightCtrl] = 0xFFE4,
        // The right Alt key is AltGr (ISO_Level3_Shift) on most Linux layouts; the characters it makes come as text.
        [Key.LeftAlt] = 0xFFE9, [Key.RightAlt] = 0xFE03, [Key.LWin] = 0xFFEB, [Key.RWin] = 0xFFEC,
        [Key.Apps] = 0xFF67, [Key.CapsLock] = 0xFFE5, [Key.NumLock] = 0xFF7F, [Key.Scroll] = 0xFF14,
        [Key.Pause] = 0xFF13, [Key.PrintScreen] = 0xFF61
    };

    /// <summary>Keys held with another key make a shortcut (Ctrl+C) rather than text.</summary>
    public static readonly IReadOnlySet<Key> ShortcutModifiers = new HashSet<Key>
    {
        Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.LWin, Key.RWin
    };

    /// <summary>The keysym of a key that types nothing; null for a key that types a character.</summary>
    public static uint? SpecialKeySym(Key key)
    {
        if (Special.TryGetValue(key, out uint special))
            return special;
        if (key is >= Key.F1 and <= Key.F24)
            return 0xFFBE + (uint)(key - Key.F1);
        return null;
    }

    /// <summary>
    /// The keysym of a character key pressed as part of a shortcut, which types no text: the character Avalonia
    /// reports, or else the unshifted letter or digit.
    /// </summary>
    public static uint? ShortcutKeySym(Key key, string? symbol)
    {
        if (symbol is { Length: > 0 } && !char.IsControl(symbol[0]))
            return FromCodePoint(char.IsSurrogatePair(symbol, 0) ? char.ConvertToUtf32(symbol, 0) : symbol[0]);
        return key switch
        {
            >= Key.A and <= Key.Z => (uint)('a' + (key - Key.A)),
            >= Key.D0 and <= Key.D9 => (uint)('0' + (key - Key.D0)),
            Key.Space => 0x20,
            _ => null
        };
    }

    /// <summary>The keysym of a typed character: Latin-1 as itself, the rest as Unicode keysym.</summary>
    public static uint FromCodePoint(int codePoint) => codePoint < 0x100 ? (uint)codePoint : 0x01000000u | (uint)codePoint;
}
