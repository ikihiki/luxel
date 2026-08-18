using System.Text.Json;
using Luxel.Typography;
using Luxel.UI;
using Luxel.Controls;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.UI;

/// <summary>KnobsTable の行レイアウトをカード型の Args 表示へ切り替える外観設定。</summary>
public sealed record KnobsTableAppearance(
    float RowHeight = 52,
    float RowGap = 1,
    float PaddingX = 12,
    float PaddingY = 8,
    float ControlWidth = 160,
    float Radius = 7,
    uint? BorderColor = null,
    uint? RowBackground = null,
    uint? NameColor = null,
    uint? TypeColor = null,
    uint? DescriptionColor = null);

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
    /// <summary>省略時は従来の4列テーブル。指定時はBlazor Gallery風のカード行。</summary>
    [UiParam] private readonly Bindable<KnobsTableAppearance> _appearance = new();

    /// <summary>knob の文字列編集 (第一引数 = 発火元, knob, 新しい値の文字列表現)。</summary>
    [UiEvent] public UiEvent<KnobsTable, StoryKnob, string> OnEdit;

    // knob更新でテーブルが再構築されても入力widgetの開閉・フォーカス・選択状態を維持する。
    private readonly Dictionary<StoryKnob, Widget> _editors = new(ReferenceEqualityComparer.Instance);

    protected override Widget Build()
    {
        IReadOnlyList<StoryKnob> knobs = Knobs.Get();
        if (knobs.Count == 0)
            return Text(Appearance.Get() is null ? "knob なし" : "このStoryには編集可能な引数がありません。",
                Appearance.Get() is null ? 11 : 13, color: Bind.From(() => UiTheme.T.TextMuted),
                margin: new Thickness(4, Appearance.Get() is null ? 0 : 10, 0, 0));

        KnobsTableAppearance? appearance = Appearance.Get();
        if (appearance is not null) return BuildCards(knobs, appearance);

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

    private Widget BuildCards(IReadOnlyList<StoryKnob> knobs, KnobsTableAppearance appearance)
    {
        Bindable<uint> Color(uint? value, Func<uint> fallback)
            => value is uint color ? color : Bind.From(fallback);

        float tableWidth = Width.Get();
        float innerWidth = MathF.Max(80, tableWidth - 2);
        float labelWidth = MathF.Max(80,
            innerWidth - appearance.PaddingX * 2 - appearance.ControlWidth - 18);
        var rows = new List<Widget>(knobs.Count);
        foreach (StoryKnob knob in knobs)
        {
            string typeLabel = knob.Type.StartsWith("enum:") ? "enum" : knob.Type;
            var labelParts = new List<Widget>
            {
                HStack(7)[
                    Text(knob.Name, 12, color: Color(appearance.NameColor, () => UiTheme.T.Text)),
                    Text(typeLabel, 10, color: Color(appearance.TypeColor, () => UiTheme.T.TextMuted))],
            };
            if (!string.IsNullOrWhiteSpace(knob.Description))
                labelParts.Add(Text(knob.Description!, 11,
                    color: Color(appearance.DescriptionColor, () => UiTheme.T.TextMuted),
                    width: labelWidth, wrap: TextWrap.Word));

            Widget row = Border(
                background: Color(appearance.RowBackground, () => UiTheme.T.Surface),
                padding: new Thickness(appearance.PaddingX, appearance.PaddingY),
                width: innerWidth, height: appearance.RowHeight)[
                    HStack(18)[
                        VStack(3, width: labelWidth)[labelParts.ToArray()],
                        Border(width: appearance.ControlWidth)[Editor(knob)]]];
            rows.Add(row);
        }

        return Border(
            background: Color(appearance.BorderColor, () => UiTheme.T.BorderColor),
            padding: new Thickness(1), rounded: appearance.Radius, clip: true,
            width: tableWidth)[VStack(appearance.RowGap)[rows.ToArray()]];
    }

    /// <summary>操作列 — 型別エディタ。commit は OnEdit (effect 内から呼ばれるため受け手はキューへ)。</summary>
    private Widget Editor(StoryKnob k)
    {
        if (_editors.TryGetValue(k, out Widget? editor)) return editor;
        editor = CreateEditor(k);
        _editors.Add(k, editor);
        return editor;
    }

    private static string PresetLabel(string option)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(option);
            return document.RootElement.ValueKind == JsonValueKind.String
                ? document.RootElement.GetString() ?? ""
                : option;
        }
        catch (JsonException)
        {
            return option;
        }
    }

    private Widget CreateEditor(StoryKnob k)
    {
        void Commit(string v) => OnEdit.Invoke(this, k, v);

        if (k.Editor == StoryArgEditorKind.Boolean)
        {
            var b = new Signal<bool>(k.Value == "true");
            bool first = true;
            Reactive.Effect(() => { bool v = b.Value; if (first) { first = false; return; } Commit(v ? "true" : "false"); });
            return Check(b, "");
        }
        if (k.Editor == StoryArgEditorKind.Color)
        {
            // Kit ファクトリが型名を隠すため完全修飾 (CS0119 回避)
            var col = new Signal<uint>(global::Luxel.Controls.ColorPicker.TryParseHex(k.Value, out uint c) ? c : 0xFF000000u);
            bool first = true;
            Reactive.Effect(() => { uint v = col.Value; if (first) { first = false; return; } Commit(global::Luxel.Controls.ColorPicker.ToHex(v)); });
            return ColorPicker(col);
        }
        if (k.Editor == StoryArgEditorKind.Length)
        {
            var len = new Signal<Length>(Length.TryParse(k.Value, null, out Length l) ? l : default);
            bool first = true;
            Reactive.Effect(() => { Length v = len.Value; if (first) { first = false; return; } Commit(v.ToString()); });
            return LengthField(len);
        }
        if ((k.Editor is StoryArgEditorKind.Enum or StoryArgEditorKind.Preset) && k.Options is { Count: > 0 })
        {
            string[] options = k.Options.ToArray();
            int selectedIndex = Array.IndexOf(options, k.Value);
            if (selectedIndex < 0) selectedIndex = Array.FindIndex(options,
                option => string.Equals(PresetLabel(option), k.Value, StringComparison.Ordinal));
            var selected = new Signal<int>(Math.Max(0, selectedIndex));
            bool first = true;
            Reactive.Effect(() =>
            {
                int index = selected.Value;
                if (first) { first = false; return; }
                Commit(options[Math.Clamp(index, 0, options.Length - 1)]);
            });
            return Select(options.Select(PresetLabel).ToArray(), selected);
        }
        var txt = new Signal<string>(k.Value);
        bool firstT = true;
        Reactive.Effect(() => { string v = txt.Value; if (firstT) { firstT = false; return; } Commit(v); });
        TextField tf = TextField(txt, width: CtlW);
        tf.Pattern = k.Editor == StoryArgEditorKind.Number
            ? k.Type == "int" ? IntPattern : FloatPattern
            : null;
        return tf;
    }
}
