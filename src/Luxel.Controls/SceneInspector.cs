using System.Globalization;
using System.Numerics;
using Luxel.SceneEdit;
using Luxel.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

/// <summary>
/// シーンエディタのインスペクタ (ToDo 27 GE-2) — <see cref="SceneEditorView"/> の主選択エンティティの
/// コンポーネントを <see cref="IComponentSchema"/> 経由で行に並べる。**PropertyGrid の行/エディタ流儀の
/// スキーマ駆動版** (PropertyGrid はリフレクションベースで、スキーマ駆動の SceneComponent には
/// 合わないため別コントロール — 単一の真実はスキーマ、ADR-0015)。編集はすべて
/// <see cref="SceneEditorView.ApplyEdit"/> の Transaction 経由 (直接 mutate しない = undo が壊れない)。
/// スキーマに無いコンポーネント/フィールドは読み取り表示で保全する。コンポーネント追加は
/// シーンの space で出し分け (原則 2)。
/// </summary>
[UiComponent]
public sealed partial class SceneInspector : CompositeControl
{
    private const string FloatPattern = @"^-?[0-9]*\.?[0-9]*$";
    private const string IntPattern = "^-?[0-9]*$";
    private const float CtlW = 150, Gap = 8;

    /// <summary>対象のシーンエディタ。</summary>
    [UiParam] private readonly Bindable<SceneEditorView> _editor = new();
    /// <summary>コンポーネントスキーマ登録簿 (既定 = 組み込みのみ)。</summary>
    [UiParam] private readonly Bindable<SchemaRegistry> _schemas = new();
    /// <summary>全幅 (px)。</summary>
    [UiParam] private readonly Bindable<float> _width = 260f;

    private readonly Dictionary<(string Comp, string Field), Widget> _editors = new();
    private readonly Signal<int> _addChoice = new(0);
    private string[] _addable = [];

    /// <summary>フィールドのエディタ widget (play/テスト用)。</summary>
    public Widget? EditorOf(string component, string field) => _editors.GetValueOrDefault((component, field));

    /// <summary>主選択エンティティへスキーマの既定値でコンポーネントを追加する (追加ボタンと同じ経路)。</summary>
    public void AddComponent(string type)
    {
        if (Editor.Get() is not { } ed || ed.Scene.Selection.Main < 0) return;
        if (Registry().TryGet(type) is not { } schema) return;
        ed.ApplyEdit(new SetComponent(ed.Scene.Selection.Main, SceneSchemas.NewComponent(schema)));
    }

    /// <summary>主選択エンティティからコンポーネントを外す (× ボタンと同じ経路)。</summary>
    public void RemoveComponent(string type)
    {
        if (Editor.Get() is not { } ed || ed.Scene.Selection.Main < 0) return;
        ed.ApplyEdit(new RemoveComponent(ed.Scene.Selection.Main, type));
    }

    private SchemaRegistry Registry() => Schemas.Get() ?? SceneSchemas.BuiltIns();

    protected override Widget Build()
    {
        _editors.Clear();
        SceneEditorView? ed = Editor.Get();
        if (ed is null) return Note("エディタ未接続");
        _ = ed.Revision.Value;   // 状態 (選択/文書) の確定変化で作り直す
        SceneEditState state = ed.Scene;
        int id = state.Selection.Main;
        if (id < 0) return Note("選択なし");
        SceneEntity entity = state.Doc.Entity(id);
        SchemaRegistry registry = Registry();
        float w = Width.Get();

        var rows = new List<Widget>
        {
            Text($"{entity.Name}  (id {id})", 13, color: Bind.From(() => UiTheme.T.Text)),
        };

        foreach (SceneComponent c in entity.Components)
        {
            IComponentSchema? schema = registry.TryGet(c.Type);
            rows.Add(Divider());
            rows.Add(HStack(Gap)[
                Text(schema?.DisplayName ?? c.Type, 12, color: Bind.From(() => UiTheme.T.TextMuted),
                     width: w - 40, margin: new Thickness(0, 6, 0, 0)),
                Button(_ => RemoveComponent(c.Type), "×", width: 26f, height: 22f)]);
            foreach (SceneField f in c.Fields)
            {
                SceneFieldDef? def = schema?.Fields.FirstOrDefault(d => d.Name == f.Name);
                Widget editor = def is null
                    ? Note(f.Value.ToString())   // スキーマ外は読み取り表示 (未知保全)
                    : FieldEditor(ed, id, c.Type, def, f.Value);
                _editors[(c.Type, f.Name)] = editor;
                rows.Add(HStack(Gap)[
                    Text(f.Name, 12, color: Bind.From(() => UiTheme.T.Text), width: MathF.Max(50, w - CtlW - Gap),
                         margin: new Thickness(4, 5, 0, 0)),
                    editor]);
            }
        }

        // コンポーネント追加 — シーンの space に合い、まだ載っていないスキーマだけ
        _addable = registry.For(state.Doc.Space)
            .Where(s => entity.Component(s.Type) is null)
            .Select(s => s.Type).ToArray();
        if (_addable.Length > 0)
        {
            if (_addChoice.Peek() >= _addable.Length) _addChoice.Value = 0;
            rows.Add(Divider());
            rows.Add(HStack(Gap)[
                Select(_addable.Select(t => registry.TryGet(t)!.DisplayName).ToArray(), _addChoice, width: CtlW),
                Button(_ => AddComponent(_addable[Math.Clamp(_addChoice.Peek(), 0, _addable.Length - 1)]), "追加")]);
        }

        return VStack(4)[rows.ToArray()];
    }

    private static Widget Note(string text)
        => Text(text, 11, color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(4, 0, 0, 0));

    /// <summary>型別エディタ (PropertyGrid と同じ「signal + 初回スキップ Effect で commit」方式)。
    /// commit = <see cref="SceneEditorView.ApplyEdit"/> の SetField 1 個 = 1 undo。</summary>
    private Widget FieldEditor(SceneEditorView ed, int entityId, string comp, SceneFieldDef def, SceneValue value)
    {
        void Commit(SceneValue v) => ed.ApplyEdit(new SetField(entityId, comp, def.Name, v));

        switch (def.Type)
        {
            case SceneFieldType.Bool:
            {
                var b = new Signal<bool>(value.AsBool());
                Skip1(() => Commit(SceneValue.Of(b.Value)), b);
                return Check(b, "");
            }
            case SceneFieldType.Enum:
            {
                string[] names = def.EnumValues!.ToArray();
                var sel = new Signal<int>(Math.Max(0, Array.IndexOf(names, value.AsText())));
                Skip1(() => Commit(SceneValue.Of(names[Math.Clamp(sel.Value, 0, names.Length - 1)])), sel);
                return Select(names, sel, width: CtlW);
            }
            case SceneFieldType.Color:
            {
                Vector4 c = value.AsVec4();
                var col = new Signal<uint>(Color2D.FromFloat(c.X, c.Y, c.Z, c.W));
                Skip1(() => Commit(SceneValue.Of(new Vector4(
                    (col.Value & 0xFF) / 255f, (col.Value >> 8 & 0xFF) / 255f,
                    (col.Value >> 16 & 0xFF) / 255f, (col.Value >> 24 & 0xFF) / 255f))), col);
                return ColorPicker(col);
            }
            case SceneFieldType.Vec2 or SceneFieldType.Vec3:
            {
                bool v3 = def.Type == SceneFieldType.Vec3;
                Vector3 cur = v3 ? value.AsVec3() : new Vector3(value.AsVec2(), 0);
                return Axes(cur, v3, v => Commit(v3 ? SceneValue.Of(v) : SceneValue.Of(new Vector2(v.X, v.Y))));
            }
            case SceneFieldType.Quat:
            {
                // 表示はオイラー角 (度)、保存は Quat のまま (ADR-0015)
                Vector3 euler = SceneRotation.ToEulerDegrees(value.AsQuat());
                return Axes(euler, v3: true, v => Commit(SceneValue.Of(SceneRotation.FromEulerDegrees(v))));
            }
            case SceneFieldType.Int:
            {
                var txt = new Signal<string>(value.AsInt().ToString(CultureInfo.InvariantCulture));
                Skip1(() => { if (int.TryParse(txt.Value, out int i)) Commit(SceneValue.Of(i)); }, txt);
                TextField tf = TextField(txt, width: CtlW);
                tf.Pattern = IntPattern;
                return tf;
            }
            case SceneFieldType.Float:
            {
                var txt = new Signal<string>(F(value.AsFloat()));
                Skip1(() => Commit(SceneValue.Of(P(txt.Value))), txt);
                TextField tf = TextField(txt, width: CtlW);
                tf.Pattern = FloatPattern;
                return tf;
            }
            default:   // String / AssetRef (パス文字列)
            {
                var txt = new Signal<string>(value.AsText());
                Skip1(() => Commit(SceneValue.Of(txt.Value)), txt);
                return TextField(txt, width: CtlW);
            }
        }
    }

    // 軸別数値フィールド (Vec2/Vec3/オイラー共用、PropertyGrid の Vector 行と同じ形)
    private static Widget Axes(Vector3 cur, bool v3, Action<Vector3> commit)
    {
        float axisW = v3 ? (CtlW - 8) / 3 : (CtlW - 4) / 2;
        var axes = new Signal<string>[v3 ? 3 : 2];
        var fields = new Widget[axes.Length];
        for (int i = 0; i < axes.Length; i++)
        {
            axes[i] = new Signal<string>(F(i == 0 ? cur.X : i == 1 ? cur.Y : cur.Z));
            TextField tf = TextField(axes[i], width: axisW);
            tf.Pattern = FloatPattern;
            fields[i] = tf;
        }
        bool first = true;
        Reactive.Effect(() =>
        {
            float x = P(axes[0].Value), y = P(axes[1].Value);
            float z = v3 ? P(axes[2].Value) : 0;
            if (first) { first = false; return; }
            commit(new Vector3(x, y, z));
        });
        return HStack(4)[fields];
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static float P(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0;

    private static void Skip1<T>(Action action, Signal<T> sig)
    {
        bool first = true;
        Reactive.Effect(() => { _ = sig.Value; if (first) { first = false; return; } action(); });
    }
}
