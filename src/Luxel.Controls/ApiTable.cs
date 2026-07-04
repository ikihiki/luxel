using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

/// <summary>
/// コントロール API リファレンス表 (Storybook autodocs の ArgsTable 相当)。ソースジェネレーターが
/// [UiComponent] から焼き込んだ <see cref="ControlApiRegistry"/> を名前で引き、
/// **コンストラクタ引数 / イベント / パラメータ** を /// コメントの説明付きで一覧する。
/// docs ページに <c>{ApiTable("Button")}</c> と置く。既定は自身のメンバーのみ —
/// <c>inherited: true</c> で基底 (Widget 共通: margin/hAlign/transform...) も出す。
/// </summary>
[UiComponent]
public sealed partial class ApiTable : CompositeControl
{
    private const float NameW = 110, TypeW = 150, Gap = 8;

    private readonly string _control;
    private readonly bool _inherited;
    private readonly float _width;

    [UiCtor]
    internal ApiTable(string control, bool inherited = false, float width = 640f)
    {
        _control = control;
        _inherited = inherited;
        _width = width;
    }

    protected override Widget Build()
    {
        ControlApi? api = ControlApiRegistry.Find(_control);
        if (api is null)
            return Alert($"不明なコントロール: {_control} (ControlApiRegistry 未登録)", Intent.Danger);

        float descW = MathF.Max(120, _width - NameW - TypeW - Gap * 2);
        Widget Cell(string s, float w, bool muted = false, bool head = false) =>
            Text(s, head ? 11 : 12, color: Bind.From(() => muted ? UiTheme.T.TextMuted : UiTheme.T.Text),
                 width: w, wrap: TextWrap.Word, margin: new Thickness(0, 2, 0, 0));

        var rows = new List<Widget>();
        if (api.Summary.Length > 0)
            rows.Add(Text(api.Summary, 12, color: Bind.From(() => UiTheme.T.TextMuted),
                          width: _width, wrap: TextWrap.Word, margin: new Thickness(0, 0, 0, 4)));

        void Section(string title, IEnumerable<ApiMember> members)
        {
            ApiMember[] ms = members.ToArray();
            if (ms.Length == 0) return;
            rows.Add(Text(title, 11, color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(0, 8, 0, 0)));
            rows.Add(Divider());
            foreach (ApiMember m in ms)
            {
                string type = m.Stateable ? $"{m.Type} (状態対応)" : m.Type;
                rows.Add(HStack(Gap)[
                    Cell(m.Name, NameW),
                    Cell(type, TypeW, muted: true),
                    Cell(m.Description, descW, muted: true)]);
            }
        }

        bool Show(ApiMember m) => _inherited || !m.Inherited;
        Section("コンストラクタ引数", api.Members.Where(m => m.Kind == "ctor"));
        Section("イベント", api.Members.Where(m => m.Kind == "event" && Show(m)));
        Section("パラメータ", api.Members.Where(m => m.Kind == "param" && Show(m)));
        if (!_inherited && api.Members.Any(m => m.Inherited))
            rows.Add(Text("(+ Widget 共通パラメータ — inherited: true で表示)", 10,
                          color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(0, 6, 0, 0)));

        return VStack(3)[rows.ToArray()];
    }
}
