using System.Text;

namespace Luxel.Terminal.Input;

public enum TerminalKey
{
    Enter, Escape, Backspace, Tab, Up, Down, Left, Right, Home, End,
    Insert, Delete, PageUp, PageDown, F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
}

public readonly record struct TerminalKeyEvent(TerminalKey Key, bool Shift = false, bool Alt = false, bool Control = false);

public static class TerminalKeyEncoder
{
    public static byte[] Encode(TerminalKeyEvent e, bool applicationCursor = false)
    {
        string value = e.Key switch
        {
            TerminalKey.Enter => "\r", TerminalKey.Escape => "\x1b", TerminalKey.Backspace => "\x7f", TerminalKey.Tab => e.Shift ? "\x1b[Z" : "\t",
            TerminalKey.Up => applicationCursor ? "\x1bOA" : "\x1b[A", TerminalKey.Down => applicationCursor ? "\x1bOB" : "\x1b[B",
            TerminalKey.Right => applicationCursor ? "\x1bOC" : "\x1b[C", TerminalKey.Left => applicationCursor ? "\x1bOD" : "\x1b[D",
            TerminalKey.Home => "\x1b[H", TerminalKey.End => "\x1b[F", TerminalKey.Insert => "\x1b[2~", TerminalKey.Delete => "\x1b[3~",
            TerminalKey.PageUp => "\x1b[5~", TerminalKey.PageDown => "\x1b[6~", TerminalKey.F1 => "\x1bOP", TerminalKey.F2 => "\x1bOQ",
            TerminalKey.F3 => "\x1bOR", TerminalKey.F4 => "\x1bOS", TerminalKey.F5 => "\x1b[15~", TerminalKey.F6 => "\x1b[17~",
            TerminalKey.F7 => "\x1b[18~", TerminalKey.F8 => "\x1b[19~", TerminalKey.F9 => "\x1b[20~", TerminalKey.F10 => "\x1b[21~",
            TerminalKey.F11 => "\x1b[23~", TerminalKey.F12 => "\x1b[24~", _ => ""
        };
        if ((e.Shift || e.Alt || e.Control) && value.StartsWith("\x1b[") && value.Length >= 3 && char.IsLetter(value[^1]))
        {
            int modifier = 1 + (e.Shift ? 1 : 0) + (e.Alt ? 2 : 0) + (e.Control ? 4 : 0);
            value = value[..^1] + ";" + modifier + value[^1];
        }
        else if (e.Alt && e.Key is not TerminalKey.Escape) value = "\x1b" + value;
        return Encoding.UTF8.GetBytes(value);
    }

    public static byte[] EncodeText(string text) => Encoding.UTF8.GetBytes(text);
    public static byte[] EncodePaste(string text, bool bracketed) => Encoding.UTF8.GetBytes(bracketed ? $"\x1b[200~{text}\x1b[201~" : text);
}
