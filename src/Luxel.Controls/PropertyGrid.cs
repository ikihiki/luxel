using System.Globalization;
using System.Numerics;
using System.Reflection;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

/// <summary>float プロパティを Slider で編集させる範囲指定 (PropertyGrid 用)。</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class PropertyRangeAttribute(float min, float max) : Attribute
{
    public float Min { get; } = min;
    public float Max { get; } = max;
}

/// <summary>PropertyGrid に出さないメンバー。</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class PropertyIgnoreAttribute : Attribute;

/// <summary>PropertyGrid のグループ見出し (未指定は先頭の無名グループ)。</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class PropertyGroupAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>PropertyGrid の行 1 つ (リフレクション結果のスナップショット、テスト用に公開)。</summary>
public sealed record PropertyRow(string Name, string Group, Type Type,
                                 Func<object?> Get, Action<object?> Set,
                                 float? RangeMin = null, float? RangeMax = null);

/// <summary>
/// Inspector (ADR-0014): 対象オブジェクト (config / ECS コンポーネント / 任意の型) の
/// public 読み書きメンバーを反映し、型別エディタを行に並べる —
/// bool=Check / enum=Select / uint=ColorPicker / float=[PropertyRange で Slider、無ければ数値
/// TextField] / int,string=TextField / Vector2,3=軸別数値 / Length=LengthField。
/// [PropertyGroup] でグループ見出し、[PropertyIgnore] で除外。編集は対象へ即書き込み +
/// <see cref="OnChanged"/> 通知 (boxed struct は箱へ書く — ECS へ戻すのはシェルの責務)。
/// 外部から対象が変わったら <see cref="Refresh"/>。
/// </summary>
[UiComponent]
public sealed partial class PropertyGrid : CompositeControl
{
    private const string FloatPattern = @"^-?[0-9]*\.?[0-9]*$";
    private const string IntPattern = "^-?[0-9]*$";
    private const float NameW = 110, CtlW = 150, Gap = 8;

    /// <summary>編集対象 (null = 空表示)。</summary>
    [UiParam] private readonly Bindable<object?> _target = new();
    /// <summary>全幅 (px)。</summary>
    [UiParam] private readonly Bindable<float> _width = 300f;

    /// <summary>編集通知 (第一引数 = 発火元, メンバー名, 新しい値)。対象へは書き込み済み。</summary>
    [UiEvent] public UiEvent<PropertyGrid, string, object?> OnChanged;

    private readonly Signal<int> _version = new(0);
    private readonly Dictionary<string, Widget> _editors = new();   // play/テスト用

    /// <summary>対象の値が外部で変わったとき呼ぶ — 反映し直す (Peek ベース — Effect 内から
    /// 呼ばれても自己購読しない)。</summary>
    public void Refresh() => _version.Value = _version.Peek() + 1;

    /// <summary>メンバー名のエディタ widget (play/テスト用)。</summary>
    public Widget? EditorOf(string name) => _editors.GetValueOrDefault(name);

    /// <summary>対象から行を発見する (宣言順、public 読み書き + 対応型のみ)。テスト用に公開。</summary>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "PropertyGrid discovers arbitrary runtime members. Native AOT callers should use generated/static property descriptors instead.")]
    public static List<PropertyRow> Discover(object target)
    {
        var rows = new List<PropertyRow>();
        Type t = target.GetType();
        // プロパティ (宣言順) → public field (宣言順)。MetadataToken はテーブル (Property/Field) を
        // またぐと比較できないため二段にする
        var members = new List<MemberInfo>();
        members.AddRange(t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true } && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.MetadataToken));
        members.AddRange(t.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => !f.IsInitOnly).OrderBy(f => f.MetadataToken));

        foreach (MemberInfo m in members)
        {
            if (m.GetCustomAttribute<PropertyIgnoreAttribute>() is not null) continue;
            Type mt = m is PropertyInfo pi ? pi.PropertyType : ((FieldInfo)m).FieldType;
            if (!Supported(mt)) continue;
            var range = m.GetCustomAttribute<PropertyRangeAttribute>();
            string group = m.GetCustomAttribute<PropertyGroupAttribute>()?.Name ?? "";
            Func<object?> get = m is PropertyInfo gp ? () => gp.GetValue(target) : () => ((FieldInfo)m).GetValue(target);
            Action<object?> set = m is PropertyInfo sp ? v => sp.SetValue(target, v) : v => ((FieldInfo)m).SetValue(target, v);
            rows.Add(new PropertyRow(m.Name, group, mt, get, set, range?.Min, range?.Max));
        }
        return rows;
    }

    private static bool Supported(Type t)
        => t == typeof(bool) || t == typeof(int) || t == typeof(float) || t == typeof(string)
        || t == typeof(uint) || t.IsEnum || t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Length);

    protected override Widget Build()
    {
        _ = _version.Value;
        _editors.Clear();
        object? target = Target.Get();
        if (target is null)
            return Text("対象なし", 11, color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(4, 0, 0, 0));

        float w = Width.Get();
        float nameW = MathF.Max(60, w - CtlW - Gap);
        var rows = new List<Widget>();
        string currentGroup = "";
        foreach (PropertyRow row in Discover(target))
        {
            if (row.Group != currentGroup)
            {
                currentGroup = row.Group;
                if (rows.Count > 0) rows.Add(Divider());
                if (currentGroup.Length > 0)
                    rows.Add(Text(currentGroup, 11, color: Bind.From(() => UiTheme.T.TextMuted),
                                  margin: new Thickness(0, 4, 0, 0)));
            }
            Widget editor = Editor(row);
            _editors[row.Name] = editor;
            rows.Add(HStack(Gap)[
                Text(row.Name, 12, color: Bind.From(() => UiTheme.T.Text), width: nameW,
                     margin: new Thickness(4, 5, 0, 0)),
                editor]);
        }
        if (rows.Count == 0)
            return Text("編集できるメンバーなし", 11, color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(4, 0, 0, 0));
        return VStack(4)[rows.ToArray()];
    }

    /// <summary>型別エディタ (KnobsTable と同じ「signal + 初回スキップ Effect で commit」方式)。
    /// commit = 対象へ書き込み + OnChanged。</summary>
    private Widget Editor(PropertyRow row)
    {
        void Commit(object? v)
        {
            row.Set(v);
            OnChanged.Invoke(this, row.Name, v);
        }

        if (row.Type == typeof(bool))
        {
            var b = new Signal<bool>((bool)row.Get()!);
            Skip1(() => Commit(b.Value), b);
            return Check(b, "");
        }
        if (row.Type.IsEnum)
        {
            string[] names = Enum.GetNames(row.Type);
            var sel = new Signal<int>(Math.Max(0, Array.IndexOf(names, row.Get()!.ToString())));
            Skip1(() => Commit(Enum.Parse(row.Type, names[Math.Clamp(sel.Value, 0, names.Length - 1)])), sel);
            return Select(names, sel);
        }
        if (row.Type == typeof(uint))   // uint = 色 (このコードベースの規約)
        {
            var col = new Signal<uint>((uint)row.Get()!);
            Skip1(() => Commit(col.Value), col);
            return ColorPicker(col);
        }
        if (row.Type == typeof(float) && row is { RangeMin: { } min, RangeMax: { } max })
        {
            var f = new Signal<float>((float)row.Get()!);
            Skip1(() => Commit(f.Value), f);
            return Slider(f, min, max, width: CtlW);
        }
        if (row.Type == typeof(Length))
        {
            var len = new Signal<Length>((Length)row.Get()!);
            Skip1(() => Commit(len.Value), len);
            return LengthField(len);
        }
        if (row.Type == typeof(Vector2) || row.Type == typeof(Vector3))
        {
            bool v3 = row.Type == typeof(Vector3);
            Vector3 cur = v3 ? (Vector3)row.Get()! : new Vector3((Vector2)row.Get()!, 0);
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
            bool firstVec = true;
            Reactive.Effect(() =>
            {
                float x = P(axes[0].Value), y = P(axes[1].Value);
                float z = v3 ? P(axes[2].Value) : 0;
                if (firstVec) { firstVec = false; return; }
                if (v3) Commit(new Vector3(x, y, z));
                else Commit(new Vector2(x, y));
            });
            return HStack(4)[fields];
        }
        // int / float (範囲なし) / string
        var txt = new Signal<string>(row.Type == typeof(float) ? F((float)row.Get()!) : row.Get()?.ToString() ?? "");
        Skip1(() =>
        {
            string v = txt.Value;
            if (row.Type == typeof(int)) { if (int.TryParse(v, out int i)) Commit(i); }
            else if (row.Type == typeof(float)) Commit(P(v));
            else Commit(v);
        }, txt);
        TextField field = TextField(txt, width: CtlW);
        field.Pattern = row.Type == typeof(int) ? IntPattern : row.Type == typeof(float) ? FloatPattern : null;
        return field;
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static float P(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0;

    /// <summary>signal の初回読みを捨てて以降の変化で action を呼ぶ (編集 commit 用)。</summary>
    private static void Skip1<T>(Action action, Signal<T> sig)
    {
        bool first = true;
        Reactive.Effect(() => { _ = sig.Value; if (first) { first = false; return; } action(); });
    }
}
