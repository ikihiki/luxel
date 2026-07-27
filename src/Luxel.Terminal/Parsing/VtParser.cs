using System.Buffers;
using System.Text;
using Luxel.Terminal.Screen;

namespace Luxel.Terminal.Parsing;

public sealed class VtParser
{
    private enum State { Ground, Escape, Csi, Osc, OscEscape, Dcs, DcsEscape }
    private readonly TerminalBuffer _buffer;
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder _text = new(), _sequence = new();
    private State _state;
    private const int MaxSequenceLength = 16 * 1024;

    public event Action<ReadOnlyMemory<byte>>? Response;
    public event Action<string>? TitleChanged;
    public event Action<string, string>? OscReceived;

    public VtParser(TerminalBuffer buffer) => _buffer = buffer;

    public void Parse(ReadOnlySpan<byte> bytes)
    {
        Span<char> chars = stackalloc char[1024];
        while (!bytes.IsEmpty)
        {
            _decoder.Convert(bytes, chars, false, out int usedBytes, out int usedChars, out _);
            ParseChars(chars[..usedChars]); bytes = bytes[usedBytes..];
        }
    }

    public void Flush()
    {
        Span<char> chars = stackalloc char[8];
        _decoder.Convert([], chars, true, out _, out int used, out _); ParseChars(chars[..used]); FlushText();
    }

    private void ParseChars(ReadOnlySpan<char> chars)
    {
        foreach (char ch in chars)
        {
            switch (_state)
            {
                case State.Ground: Ground(ch); break;
                case State.Escape: Escape(ch); break;
                case State.Csi: Sequence(ch, State.Csi); break;
                case State.Osc: Osc(ch); break;
                case State.OscEscape: if (ch == '\\') FinishOsc(); else { AppendSequence('\x1b'); AppendSequence(ch); _state = State.Osc; } break;
                case State.Dcs: if (ch == '\x1b') _state = State.DcsEscape; else AppendSequence(ch); break;
                case State.DcsEscape: if (ch == '\\') { _sequence.Clear(); _state = State.Ground; } else { AppendSequence(ch); _state = State.Dcs; } break;
            }
        }
        FlushText();
    }

    private void Ground(char ch)
    {
        switch (ch)
        {
            case '\x1b': FlushText(); _state = State.Escape; break;
            case '\r': FlushText(); _buffer.CarriageReturn(); break;
            case '\n': case '\v': case '\f': FlushText(); _buffer.LineFeed(); break;
            case '\b': FlushText(); _buffer.Backspace(); break;
            case '\t': FlushText(); _buffer.Tab(); break;
            case '\a': break;
            default: if (ch >= ' ' && ch != '\x7f') _text.Append(ch); break;
        }
    }

    private void Escape(char ch)
    {
        switch (ch)
        {
            case '[': _sequence.Clear(); _state = State.Csi; return;
            case ']': _sequence.Clear(); _state = State.Osc; return;
            case 'P': _sequence.Clear(); _state = State.Dcs; return;
            case '7': _buffer.SaveCursor(); break;
            case '8': _buffer.RestoreCursor(); break;
            case 'D': _buffer.LineFeed(); break;
            case 'E': _buffer.CarriageReturn(); _buffer.LineFeed(); break;
            case 'M': _buffer.MoveRelative(-1, 0); break;
            case 'c': Reset(); break;
        }
        _state = State.Ground;
    }

    private void Sequence(char ch, State state)
    {
        if (ch is >= '@' and <= '~')
        {
            if (state == State.Csi) ExecuteCsi(ch, _sequence.ToString());
            _sequence.Clear(); _state = State.Ground;
        }
        else AppendSequence(ch);
    }

    private void Osc(char ch)
    {
        if (ch == '\a') FinishOsc();
        else if (ch == '\x1b') _state = State.OscEscape;
        else AppendSequence(ch);
    }

    private void FinishOsc()
    {
        string raw = _sequence.ToString(); _sequence.Clear(); _state = State.Ground;
        int semi = raw.IndexOf(';'); string code = semi < 0 ? raw : raw[..semi]; string value = semi < 0 ? "" : raw[(semi + 1)..];
        switch (code)
        {
            case "0": case "2": _buffer.Title = value; TitleChanged?.Invoke(value); break;
            case "8":
                int second = value.IndexOf(';'); _buffer.Hyperlink = second >= 0 && second + 1 < value.Length ? value[(second + 1)..] : null; break;
            default: OscReceived?.Invoke(code, value); break;
        }
    }

    private void ExecuteCsi(char final, string raw)
    {
        bool privateMode = raw.StartsWith('?'); if (privateMode) raw = raw[1..];
        int[] p = raw.Split(';', StringSplitOptions.None).Select(s => int.TryParse(s, out int n) ? n : 0).Take(64).ToArray();
        int P(int i, int fallback = 1) => i < p.Length && p[i] != 0 ? p[i] : fallback;
        switch (final)
        {
            case 'A': _buffer.MoveRelative(-P(0), 0); break;
            case 'B': case 'e': _buffer.MoveRelative(P(0), 0); break;
            case 'C': case 'a': _buffer.MoveRelative(0, P(0)); break;
            case 'D': _buffer.MoveRelative(0, -P(0)); break;
            case 'E': _buffer.MoveRelative(P(0), 0); _buffer.SetColumn(0); break;
            case 'F': _buffer.MoveRelative(-P(0), 0); _buffer.SetColumn(0); break;
            case 'G': case '`': _buffer.SetColumn(P(0) - 1); break;
            case 'H': case 'f': _buffer.MoveCursor(P(0) - 1, P(1) - 1); break;
            case 'J': _buffer.EraseDisplay(p.Length > 0 ? p[0] : 0); break;
            case 'K': _buffer.EraseLine(p.Length > 0 ? p[0] : 0); break;
            case 'X': _buffer.EraseCharacters(P(0)); break;
            case '@': _buffer.InsertCharacters(P(0)); break;
            case 'P': _buffer.DeleteCharacters(P(0)); break;
            case 'L': _buffer.InsertLines(P(0)); break;
            case 'M': _buffer.DeleteLines(P(0)); break;
            case 'S': _buffer.ScrollUp(P(0)); break;
            case 's': _buffer.SaveCursor(); break;
            case 'u': _buffer.RestoreCursor(); break;
            case 'r': _buffer.SetScrollRegion(P(0) - 1, P(1, _buffer.Rows) - 1); break;
            case 'm': ApplySgr(p); break;
            case 'n': if (P(0) == 6) Send($"\x1b[{_buffer.Snapshot().Cursor.Row + 1};{_buffer.Snapshot().Cursor.Column + 1}R"); break;
            case 'c': Send("\x1b[?62;4;6;22c"); break;
            case 'h': case 'l': if (privateMode) SetPrivateModes(p, final == 'h'); break;
        }
    }

    private void SetPrivateModes(int[] modes, bool enabled)
    {
        foreach (int mode in modes) switch (mode)
        {
            case 1: _buffer.ApplicationCursorKeys = enabled; break;
            case 6: _buffer.OriginMode = enabled; break;
            case 7: _buffer.AutoWrap = enabled; break;
            case 25: _buffer.CursorVisible = enabled; break;
            case 47: case 1047: case 1049: _buffer.UseAlternateScreen(enabled); break;
            case 2004: _buffer.BracketedPaste = enabled; break;
        }
    }

    private void ApplySgr(int[] values)
    {
        if (values.Length == 0) values = [0]; TerminalAttributes a = _buffer.Attributes;
        for (int i = 0; i < values.Length; i++)
        {
            int v = values[i];
            switch (v)
            {
                case 0: a = TerminalAttributes.Default; break;
                case 1: a = a with { Style = a.Style | TerminalStyle.Bold }; break;
                case 2: a = a with { Style = a.Style | TerminalStyle.Dim }; break;
                case 3: a = a with { Style = a.Style | TerminalStyle.Italic }; break;
                case 4: a = a with { Style = a.Style | TerminalStyle.Underline }; break;
                case 5: a = a with { Style = a.Style | TerminalStyle.Blink }; break;
                case 7: a = a with { Style = a.Style | TerminalStyle.Inverse }; break;
                case 8: a = a with { Style = a.Style | TerminalStyle.Hidden }; break;
                case 9: a = a with { Style = a.Style | TerminalStyle.Strikethrough }; break;
                case 22: a = a with { Style = a.Style & ~(TerminalStyle.Bold | TerminalStyle.Dim) }; break;
                case 23: a = a with { Style = a.Style & ~TerminalStyle.Italic }; break;
                case 24: a = a with { Style = a.Style & ~TerminalStyle.Underline }; break;
                case 25: a = a with { Style = a.Style & ~TerminalStyle.Blink }; break;
                case 27: a = a with { Style = a.Style & ~TerminalStyle.Inverse }; break;
                case 28: a = a with { Style = a.Style & ~TerminalStyle.Hidden }; break;
                case 29: a = a with { Style = a.Style & ~TerminalStyle.Strikethrough }; break;
                case >= 30 and <= 37: a = a with { Foreground = TerminalColor.Indexed((byte)(v - 30)) }; break;
                case 39: a = a with { Foreground = TerminalColor.Default }; break;
                case >= 40 and <= 47: a = a with { Background = TerminalColor.Indexed((byte)(v - 40)) }; break;
                case 49: a = a with { Background = TerminalColor.Default }; break;
                case >= 90 and <= 97: a = a with { Foreground = TerminalColor.Indexed((byte)(v - 90 + 8)) }; break;
                case >= 100 and <= 107: a = a with { Background = TerminalColor.Indexed((byte)(v - 100 + 8)) }; break;
                case 38: a = a with { Foreground = ExtendedColor(values, ref i) }; break;
                case 48: a = a with { Background = ExtendedColor(values, ref i) }; break;
                case 58: a = a with { UnderlineColor = ExtendedColor(values, ref i) }; break;
                case 59: a = a with { UnderlineColor = TerminalColor.Default }; break;
            }
        }
        _buffer.Attributes = a;
    }

    private static TerminalColor ExtendedColor(int[] p, ref int i)
    {
        if (i + 2 < p.Length && p[i + 1] == 5) { i += 2; return TerminalColor.Indexed((byte)Math.Clamp(p[i], 0, 255)); }
        if (i + 4 < p.Length && p[i + 1] == 2) { byte r = (byte)Math.Clamp(p[i + 2], 0, 255), g = (byte)Math.Clamp(p[i + 3], 0, 255), b = (byte)Math.Clamp(p[i + 4], 0, 255); i += 4; return TerminalColor.Rgb(r, g, b); }
        return TerminalColor.Default;
    }

    private void Reset() { _buffer.Attributes = TerminalAttributes.Default; _buffer.CursorVisible = true; _buffer.AutoWrap = true; _buffer.UseAlternateScreen(false); _buffer.EraseDisplay(2); _buffer.MoveCursor(0, 0); }
    private void FlushText() { if (_text.Length == 0) return; foreach (Rune rune in _text.ToString().EnumerateRunes()) _buffer.WriteRune(rune); _text.Clear(); }
    private void AppendSequence(char ch) { if (_sequence.Length < MaxSequenceLength) _sequence.Append(ch); else { _sequence.Clear(); _state = State.Ground; } }
    private void Send(string value) => Response?.Invoke(Encoding.ASCII.GetBytes(value));
}
