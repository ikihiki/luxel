using Luxel.Typography;
using Luxel.UI;
using Luxel.Controls;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.UI;

/// <summary>
/// Knobs の autodoc 風テーブル (Storybook の ArgsTable 相当): 名前 | 型 | 説明 | 操作。
/// 操作列は knob の型に応じたエディタ (bool=Check / color=ColorPicker / int,float=規制付き
/// TextField / string=TextField)。編集は <see cref="OnEdit"/> で通知する — signal への書き込みは
/// effect 文脈外で行う必要があるため、受け手は <c>StoryContext.QueueKnobEdit</c> へ積むこと
/// (ホストのフレームループが <c>PumpKnobEdits</c> で適用する)。
/// </summary>
[UiComponent]
public sealed partial class KnobsTable : CompositeControl
{
    private const string FloatPattern = @"^-?[0-9]*\.?[0-9]*$";
    private const string IntPattern = "^-?[0-9]*$";
    private const float NameW = 90, TypeW = 46, CtlW = 130, Gap = 6;

    /// <summary>表示する knob 列。</summary>
    [UiParam] private readonly Bindable<IReadOnlyList<StoryKnob>> _knobs = new([]);
    /// <summary>テーブル全幅 (px)。説明列が残り幅を受ける。</summary>
    [UiParam] private readonly Bindable<float> _width = 480f;

    /// <summary>knob の文字列編集 (第一引数 = 発火元, knob, 新しい値の文字列表現)。</summary>
    [UiEvent] public UiEvent<KnobsTable, StoryKnob, string> OnEdit;

    protected override Widget Build()
    {
        IReadOnlyList<StoryKnob> knobs = Knobs.Get();
        if (knobs.Count == 0)
            return Text("knob なし", 11, color: Bind.From(() => UiTheme.T.TextMuted),
                        margin: new Thickness(4, 0, 0, 0));

        float descW = MathF.Max(50, Width.Get() - NameW - TypeW - CtlW - Gap * 3);
        Widget Cell(string s, float w, bool muted = false) =>
            Text(s, 11, color: Bind.From(() => muted ? UiTheme.T.TextMuted : UiTheme.T.Text),
                 width: w, wrap: TextWrap.Word, margin: new Thickness(0, 4, 0, 0));

        var rows = new List<Widget>
        {
            HStack(Gap)[
                Cell("名前", NameW, muted: true), Cell("型", TypeW, muted: true),
                Cell("説明", descW, muted: true), Cell("操作", CtlW, muted: true)],
            Divider(),
        };
        foreach (StoryKnob k in knobs)
        {
            // 型列は "enum:A|B|C" の候補列挙を出さない (候補は操作列の Select で見える)
            string typeLabel = k.Type.StartsWith("enum:") ? "enum" : k.Type;
            rows.Add(HStack(Gap)[
                Cell(k.Name, NameW),
                Cell(typeLabel, TypeW, muted: true),
                Cell(k.Description ?? "", descW, muted: true),
                Editor(k)]);
        }
        return VStack(3)[rows.ToArray()];
    }

    /// <summary>操作列 — 型別エディタ。commit は OnEdit (effect 内から呼ばれるため受け手はキューへ)。</summary>
    private Widget Editor(StoryKnob k)
    {
        void Commit(string v) => OnEdit.Invoke(this, k, v);

        if (k.Type == "bool")
        {
            var b = new Signal<bool>(k.Value == "true");
            bool first = true;
            Reactive.Effect(() => { bool v = b.Value; if (first) { first = false; return; } Commit(v ? "true" : "false"); });
            return Check(b, "");
        }
        if (k.Type == "color")
        {
            // Kit ファクトリが型名を隠すため完全修飾 (CS0119 回避)
            var col = new Signal<uint>(global::Luxel.Controls.ColorPicker.TryParseHex(k.Value, out uint c) ? c : 0xFF000000u);
            bool first = true;
            Reactive.Effect(() => { uint v = col.Value; if (first) { first = false; return; } Commit(global::Luxel.Controls.ColorPicker.ToHex(v)); });
            return ColorPicker(col);
        }
        if (k.Type == "length")
        {
            var len = new Signal<Length>(Length.TryParse(k.Value, null, out Length l) ? l : default);
            bool first = true;
            Reactive.Effect(() => { Length v = len.Value; if (first) { first = false; return; } Commit(v.ToString()); });
            return LengthField(len);
        }
        if (k.Type.StartsWith("enum:"))
        {
            string[] opts = k.Type[5..].Split('|');
            var sel = new Signal<int>(Math.Max(0, Array.IndexOf(opts, k.Value)));
            bool first = true;
            Reactive.Effect(() => { int i = sel.Value; if (first) { first = false; return; } Commit(opts[Math.Clamp(i, 0, opts.Length - 1)]); });
            return Select(opts, sel);
        }
        var txt = new Signal<string>(k.Value);
        bool firstT = true;
        Reactive.Effect(() => { string v = txt.Value; if (firstT) { firstT = false; return; } Commit(v); });
        TextField tf = TextField(txt, width: CtlW);
        tf.Pattern = k.Type switch { "int" => IntPattern, "float" => FloatPattern, _ => null };
        return tf;
    }
}
