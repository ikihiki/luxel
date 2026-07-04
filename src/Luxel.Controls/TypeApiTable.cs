using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

/// <summary>
/// 型 API リファレンス表 (<see cref="ApiTable"/> のコントロール以外の型版)。ソースジェネレーター
/// (AssemblyApiGenerator) が参照アセンブリの XML doc から焼き込んだ <see cref="TypeApiRegistry"/> を
/// 名前で引き、**コンストラクタ / メソッド / プロパティ / イベント / フィールド** を説明付きで一覧する。
/// docs ページに <c>{TypeApiTable("Scene2D")}</c> と置く。
/// </summary>
[UiComponent]
public sealed partial class TypeApiTable : CompositeControl
{
    private const float NameW = 130, TypeW = 260, Gap = 8;

    private readonly string _type;
    private readonly float _width;

    [UiCtor]
    internal TypeApiTable(string type, float width = 760f)
    {
        _type = type;
        _width = width;
    }

    protected override Widget Build()
    {
        TypeApi? api = TypeApiRegistry.Find(_type);
        if (api is null)
            return Alert($"不明な型: {_type} (TypeApiRegistry 未登録 — [assembly: GenerateAssemblyApi] を確認)", Intent.Danger);

        float descW = MathF.Max(120, _width - NameW - TypeW - Gap * 2);
        Widget Cell(string s, float w, bool muted = false) =>
            Text(s, 12, color: Bind.From(() => muted ? UiTheme.T.TextMuted : UiTheme.T.Text),
                 width: w, wrap: TextWrap.Word, margin: new Thickness(0, 2, 0, 0));

        var rows = new List<Widget>();
        if (api.Summary.Length > 0)
            rows.Add(Text($"{api.Kind} — {api.Summary}", 12, color: Bind.From(() => UiTheme.T.TextMuted),
                          width: _width, wrap: TextWrap.Word, margin: new Thickness(0, 0, 0, 4)));

        void Section(string title, IEnumerable<ApiMember> members)
        {
            ApiMember[] ms = members.ToArray();
            if (ms.Length == 0) return;
            rows.Add(Text(title, 11, color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(0, 8, 0, 0)));
            rows.Add(Divider());
            foreach (ApiMember m in ms)
                rows.Add(HStack(Gap)[
                    Cell(m.Name, NameW),
                    Cell(m.Type, TypeW, muted: true),
                    Cell(m.Description, descW, muted: true)]);
        }

        Section("コンストラクタ", api.Members.Where(m => m.Kind == "ctor"));
        Section("メソッド", api.Members.Where(m => m.Kind == "method"));
        Section("プロパティ", api.Members.Where(m => m.Kind == "prop"));
        Section("イベント", api.Members.Where(m => m.Kind == "event"));
        Section(api.Kind == "enum" ? "値" : "フィールド", api.Members.Where(m => m.Kind == "field"));
        return VStack(3)[rows.ToArray()];
    }
}
