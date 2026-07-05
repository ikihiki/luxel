using System.Text.Json;
using Luxel.Controls;
using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Tests;

/// <summary>
/// ソースジェネレーター (Luxel.UI.Generators) のテスト用コンポーネント。
/// [UiComponent] → <c>TestKit.TestBadge(...)</c> ファクトリ生成、
/// [UiParam] → DebugProps/SetDebugProp の焼き込み (switch + ジェネリック直書き) を検証する。
/// トップレベル partial であること (nested は生成対象外)。
/// </summary>
[UiComponent(Factory = "TestKit")]
public sealed partial class TestBadge : Widget
{
    [UiParam] public readonly Bindable<uint> Background = new();
    [UiParam] public Bindable<float> Rounded = 6f;
    [UiParam] public Bindable<bool> Filled = true;
    [UiParam] public Bindable<string> Tag = "tag";
    [UiParam] public BindableString Caption = "cap";

    protected override void PerformLayout(Constraints c, LayoutContext ctx) => Size = c.Constrain(new Size(10, 10));
    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin) { }
}

public class WidgetDebugGenTests
{
    // ---- ファクトリ生成 (組み立て側) ----

    [Fact]
    public void Factory_SetsSpecifiedParams_AndKeepsFieldDefaults()
    {
        TestBadge b = TestKit.TestBadge(background: Color2D.Rgba(1, 2, 3), width: 120f);
        Assert.Equal(Color2D.Rgba(1, 2, 3), b.Background.Get());
        Assert.Equal((Length)120f, b.Width.Get());
        // 未指定の引数はフィールド初期化子の既定値を壊さない
        Assert.Equal(6f, b.Rounded.Get());
        Assert.True(b.Filled.Get());
        Assert.Equal("tag", b.Tag.Get());
        Assert.False(b.Height.HasValue);   // 共通引数も未指定なら未設定のまま
    }

    [Fact]
    public void Factory_CommonParams_AreAutoAdded_FromWidgetBase()
    {
        // Width/Height/Margin/HAlign/VAlign は Widget 基底の [UiParam] 由来の共通引数
        TestBadge b = TestKit.TestBadge(
            margin: new Thickness(2, 4),
            hAlign: Align.Center, vAlign: Align.Stretch, height: 32f);
        Assert.Equal(new Thickness(2, 4), b.Margin.Get());
        Assert.Equal(Align.Center, b.HAlign.Get());
        Assert.Equal(Align.Stretch, b.VAlign.Get());
        Assert.Equal((Length)32f, b.Height.Get());
    }

    [Fact]
    public void Factory_ReactiveBinding_IsPreserved()
    {
        // Width/Height (Length) は値引数なので、リアクティブ束縛の検証は Bindable<float> の Rounded で行う
        var r = new Signal<float>(100f);
        TestBadge b = TestKit.TestBadge(rounded: r);
        Assert.Equal(100f, b.Rounded.Get());
        r.Value = 250f;
        Assert.Equal(250f, b.Rounded.Get());   // Get() せず Bindable のまま代入されている
    }

    [Fact]
    public void Factory_Length_AcceptsNumberAndCssString()
    {
        // Length 引数: 数値 = px、文字列 = CSS 風 ("50%" "1.5em" "40vw")
        TestBadge px = TestKit.TestBadge(width: 380);
        Assert.Equal(new Length(380, LengthUnit.Px), px.Width.Get());
        TestBadge pct = TestKit.TestBadge(width: "50%", height: "1.5em");
        Assert.Equal(new Length(50, LengthUnit.Percent), pct.Width.Get());
        Assert.Equal(new Length(1.5f, LengthUnit.Em), pct.Height.Get());
    }

    [Fact]
    public void SetAttached_RoundTrips()
    {
        // fluent 添付 (GridColumn 等) の下回り — SetAttached/GetAttached の往復
        TestBadge b = TestKit.TestBadge();
        b.SetAttached(new Attached("Grid.Column", 2));
        Assert.Equal(2, b.GetAttached<int>("Grid.Column"));
    }

    // ---- デバッグ焼き込み (DebugProps / SetDebugProp) ----

    [Fact]
    public void DebugProps_ContainsOwnAndInheritedCommonParams()
    {
        var b = TestKit.TestBadge(background: Color2D.Rgba(0x12, 0x34, 0x56));
        var props = Assert.IsType<DebugProp[]>(b.DebugProps());

        DebugProp bg = Assert.Single(props, p => p.Name == "Background");
        Assert.Equal("color", bg.Type);
        Assert.Equal("#123456", bg.Value);

        Assert.Single(props, p => p.Name == "Rounded" && p.Type == "float");
        Assert.Single(props, p => p.Name == "Filled" && p.Type == "bool");
        Assert.Single(props, p => p.Name == "Tag" && p.Type == "string" && p.Value == "tag");
        // Widget 基底の共通プロパティ (enum はメンバー一覧付き型ヒント)
        Assert.Single(props, p => p.Name == "Width" && p.Type == "length");   // Length は専用エディタ (数値+単位)
        Assert.Single(props, p => p.Name == "Margin");
        Assert.Single(props, p => p.Name == "HAlign" && p.Type == "enum:Start|Center|End|Stretch");
    }

    [Fact]
    public void DebugProps_BakedIntoOtherAssemblies_Too()
    {
        // Luxel.UI (Border) にも同じ生成が効いている (アセンブリ横断)
        var b = Kit.Border(background: Color2D.Rgba(0xaa, 0xbb, 0xcc));
        var props = Assert.IsType<DebugProp[]>(b.DebugProps());
        Assert.Equal("#aabbcc", Assert.Single(props, p => p.Name == "Background").Value);
        Assert.Single(props, p => p.Name == "Width");
    }

    [Fact]
    public void SetDebugProp_WritesTyped_ViaGeneratedSwitch()
    {
        var b = new TestBadge();
        Set(b, "Background", "\"#ff0080\"");
        Assert.Equal(Color2D.Rgba(0xff, 0x00, 0x80), b.Background.Get());

        Set(b, "Rounded", "12.5");
        Assert.Equal(12.5f, b.Rounded.Get());

        Set(b, "Filled", "false");
        Assert.False(b.Filled.Get());

        Set(b, "Tag", "\"hello\"");
        Assert.Equal("hello", b.Tag.Get());

        Set(b, "Width", "77");
        Assert.Equal(77f, b.Width.Or(0f));   // 未設定フィールドへの override も Or で見える
    }

    [Fact]
    public void SetDebugProp_Enum_ViaTryParse()
    {
        var b = new TestBadge();
        Set(b, "HAlign", "\"Stretch\"");
        Assert.Equal(Align.Stretch, b.HAlign.Get());
        // 不正値は no-op
        Set(b, "HAlign", "\"Nope\"");
        Assert.Equal(Align.Stretch, b.HAlign.Get());
    }

    [Fact]
    public void SetDebugProp_Parsable_Thickness_ViaIParsable()
    {
        var b = new TestBadge();
        Set(b, "Margin", "\"8,4\"");
        Assert.Equal(new Thickness(8, 4), b.Margin.Get());
        Set(b, "Margin", "\"1,2,3,4\"");
        Assert.Equal(new Thickness(1, 2, 3, 4), b.Margin.Get());
        // parse 失敗は no-op
        Set(b, "Margin", "\"x\"");
        Assert.Equal(new Thickness(1, 2, 3, 4), b.Margin.Get());
    }

    [Fact]
    public void SetDebugProp_UnknownName_IsNoOp()
    {
        var b = new TestBadge();
        b.Background.SetBase(Color2D.Rgba(1, 2, 3));
        Set(b, "Nope", "\"#ffffff\"");
        Assert.False(b.Background.HasOverride);
        Assert.Equal(Color2D.Rgba(1, 2, 3), b.Background.Get());
    }

    [Fact]
    public void NonGeneratedWidget_DefaultsToEmptyProps_AndNoOpSetter()
    {
        // nested クラスは生成対象外 → 基底の既定 (空 / no-op)
        var w = new PlainWidget();
        Assert.Empty(w.DebugProps());
        Set(w, "Width", "10");
        Assert.False(w.Width.HasOverride);
    }

    // ---- BindableString の [UiParam] 対応 ----

    [Fact]
    public void BindableString_Factory_Default_And_Set()
    {
        TestBadge d = TestKit.TestBadge();
        Assert.Equal("cap", d.Caption.Get());                       // フィールド初期化子が既定値

        var sig = new Signal<string>("live");
        TestBadge b = TestKit.TestBadge(caption: sig);              // Signal 束縛のまま渡る
        Assert.Equal("live", b.Caption.Get());
        sig.Value = "changed";
        Assert.Equal("changed", b.Caption.Get());

        TestBadge i = TestKit.TestBadge(caption: $"n={sig}");       // 補完文字列 handler
        Assert.Equal("n=changed", i.Caption.Get());
    }

    [Fact]
    public void BindableString_DebugProps_And_SetDebugProp()
    {
        var b = new TestBadge();
        var props = (DebugProp[])b.DebugProps();
        Assert.Single(props, p => p.Name == "Caption" && p.Type == "string" && p.Value == "cap");

        Set(b, "Caption", "\"edited\"");                            // DevTools override
        Assert.Equal("edited", b.Caption.Get());
        Assert.Single((DebugProp[])b.DebugProps(), p => p.Name == "Caption" && p.Value == "edited");
    }

    [Fact]
    public void BindableString_SetProp_And_StateLayer()
    {
        var b = new TestBadge();
        // 名前ベース書込 (Tailwind PropPart 経路): 基底差し替え
        Assert.True(b.SetProp<string>("Caption", WidgetState.Default, "base"));
        Assert.Equal("base", b.Caption.Get());
        // hover レイヤ
        Assert.True(b.SetProp<string>("Caption", WidgetState.Hover, "hovered"));
        Assert.Equal("base", b.Caption.Get());
        b.Hovered.Value = true;
        Assert.Equal("hovered", b.Caption.Get());
        // 型不一致は false
        Assert.False(b.SetProp<int>("Caption", WidgetState.Default, 1));
    }

    [Fact]
    public void Button_VariantIntent_AreEnumUiParams()
    {
        // fluent (WithVariant/WithIntent) 廃止 → [UiParam] Bindable<enum> フィールド
        var b = Kit.Button(_ => { }, "X", variant: Variant.Ghost, intent: Intent.Danger);
        Assert.Equal(Variant.Ghost, b.Variant.Get());
        Assert.Equal(Intent.Danger, b.Intent.Get());

        var d = Kit.Button(_ => { }, "Y");
        Assert.Equal(Variant.Filled, d.Variant.Get());    // 未設定 = enum 先頭
        Assert.Equal(Intent.Primary, d.Intent.Get());

        // DebugProps はメンバー一覧付き enum ヒント → DevTools/Gallery でドロップダウン編集可
        var props = (DebugProp[])d.DebugProps();
        Assert.Single(props, p => p.Name == "Variant" && p.Type == "enum:Filled|Tonal|Outline|Ghost");
        Set(d, "Variant", "\"Outline\"");
        Assert.Equal(Variant.Outline, d.Variant.Get());
    }

    [Fact]
    public void Thickness_Parse_Formats()
    {
        Assert.Equal(new Thickness(5), Thickness.Parse("5", null));
        Assert.Equal(new Thickness(8, 4), Thickness.Parse("8,4", null));
        Assert.Equal(new Thickness(1, 2, 3, 4), Thickness.Parse("1, 2, 3, 4", null));
        Assert.False(Thickness.TryParse("1,2,3", null, out _));
        Assert.Equal("1,2,3,4", new Thickness(1, 2, 3, 4).ToString());
    }

    private static void Set(Widget w, string name, string json)
    {
        using var doc = JsonDocument.Parse(json);
        w.SetDebugProp(name, "", doc.RootElement);
    }

    private sealed class PlainWidget : Widget
    {
        protected override void PerformLayout(Constraints c, LayoutContext ctx) { }
        protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin) { }
    }
}
