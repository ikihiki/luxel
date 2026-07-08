using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Luxel.UI;

/// <summary>記録/リプレイが扱う入力の種類。<see cref="PlayDriver"/> が UiHost へ配送する
/// 低レベル操作 (クリックは押下/解放へ分解される) にそのまま対応する。</summary>
public enum InputKind
{
    PointerDown,
    PointerUp,
    PointerMove,
    Wheel,
    KeyDown,
    Char,
}

/// <summary>フレーム番号でスタンプされた 1 つの入力イベント。
/// <para>決定性の要: 経過秒でなく<b>フレーム番号</b>で位置を持つ (アニメ/物理が絡んでも
/// 固定 dt で再生すれば同じ絵になる)。JSON へ素直に往復できるようフラット構造。</para></summary>
public readonly record struct RecordedInput(
    int Frame,
    InputKind Kind,
    float X = 0,
    float Y = 0,
    float Delta = 0,
    Key Key = Key.None,
    bool Shift = false,
    bool Ctrl = false,
    bool Alt = false,
    string Text = "");

/// <summary>1 回の記録セッション。<see cref="Frames"/> = Stop 時点の総フレーム数
/// (リプレイはここまで step して静定フレームまで再現する)。</summary>
public sealed record InputRecording(int Version, int Frames, IReadOnlyList<RecordedInput> Events)
{
    /// <summary>現行フォーマット版。</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,   // 0/false/"" を省いて簡潔に
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },   // Kind/Key は文字列で可読に
    };

    /// <summary>空の記録。</summary>
    public static InputRecording Empty { get; } = new(CurrentVersion, 0, []);

    /// <summary>JSON へ直列化する (人が読める・差分に強いインデント + 文字列 enum)。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>JSON から復元する。version 不一致は例外 (前方互換は将来対応)。</summary>
    public static InputRecording FromJson(string json)
    {
        InputRecording? rec = JsonSerializer.Deserialize<InputRecording>(json, JsonOptions)
            ?? throw new FormatException("入力記録の JSON が null にデシリアライズされた");
        if (rec.Version != CurrentVersion)
            throw new FormatException($"未対応の入力記録バージョン {rec.Version} (対応 = {CurrentVersion})");
        return rec;
    }
}

/// <summary>記録を <see cref="PlayDriver"/> の play コード列 (<c>d.Click(...)</c> 等) に起こす。
/// <para>手操作の記録を回帰テスト用 play の下書きに変換するのが狙い — 低レベルな
/// 押下/移動/解放列を Click / Drag / Type / Key / Wheel へ畳んで読める形にする。</para></summary>
public static class InputScript
{
    /// <summary>記録を play 本体のコード (1 操作 = 1 行) へ変換する。フレーム間隔は省く
    /// (操作順のみ保持 — play は各操作が自前で step する)。</summary>
    public static string ToPlayCode(InputRecording rec)
    {
        var sb = new StringBuilder();
        IReadOnlyList<RecordedInput> e = rec.Events;
        int i = 0;
        while (i < e.Count)
        {
            RecordedInput ev = e[i];
            switch (ev.Kind)
            {
                case InputKind.PointerDown:
                    i = EmitPointerSequence(sb, e, i);
                    break;
                case InputKind.Char:
                    i = EmitTypeRun(sb, e, i);
                    break;
                case InputKind.KeyDown:
                    sb.Append("d.Key(Key.").Append(ev.Key);
                    if (ev.Shift) sb.Append(", shift: true");
                    if (ev.Ctrl) sb.Append(", ctrl: true");
                    if (ev.Alt) sb.Append(", alt: true");
                    sb.AppendLine(");");
                    i++;
                    break;
                case InputKind.Wheel:
                    sb.Append("d.Wheel(").Append(Coord(ev.X)).Append(", ").Append(Coord(ev.Y))
                      .Append(", ").Append(Num(ev.Delta)).AppendLine(");");
                    i++;
                    break;
                default:
                    // 対応する押下なしの単発 move/up — 通常は起きないが安全側で読み飛ばす
                    i++;
                    break;
            }
        }
        return sb.ToString();
    }

    // 押下 (→ 移動列) → 解放 を Click か Drag に畳む。
    private static int EmitPointerSequence(StringBuilder sb, IReadOnlyList<RecordedInput> e, int i)
    {
        RecordedInput down = e[i];
        int j = i + 1;
        bool moved = false;
        while (j < e.Count && e[j].Kind is InputKind.PointerMove)
        {
            moved = true;
            j++;
        }
        if (j < e.Count && e[j].Kind == InputKind.PointerUp)
        {
            RecordedInput up = e[j];
            if (moved)
                sb.Append("await d.Drag(").Append(Coord(down.X)).Append(", ").Append(Coord(down.Y))
                  .Append(", ").Append(Coord(up.X)).Append(", ").Append(Coord(up.Y)).AppendLine(");");
            else
                sb.Append("await d.Click(").Append(Coord(down.X)).Append(", ").Append(Coord(down.Y)).AppendLine(");");
            return j + 1;
        }
        // 解放が無い (捕獲されなかった等) — 押下だけ低レベルに落とす
        sb.Append("d.Host.PointerDown(").Append(Coord(down.X)).Append(", ").Append(Coord(down.Y)).AppendLine(");");
        return i + 1;
    }

    // 連続する Char を 1 つの Type("...") に畳む。
    private static int EmitTypeRun(StringBuilder sb, IReadOnlyList<RecordedInput> e, int i)
    {
        var text = new StringBuilder();
        int j = i;
        while (j < e.Count && e[j].Kind == InputKind.Char)
        {
            text.Append(e[j].Text);
            j++;
        }
        sb.Append("await d.Type(\"").Append(Escape(text.ToString())).AppendLine("\");");
        return j;
    }

    private static string Coord(float v) => ((int)MathF.Round(v)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Num(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
