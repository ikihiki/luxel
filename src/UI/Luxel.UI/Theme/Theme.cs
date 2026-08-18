using Luxel.Graphics.TwoD;

namespace Luxel.UI;

/// <summary>
/// 配色・寸法・タイポのトークン集合。<see cref="UiTheme.Current"/> に signal 保持し、切替時は
/// 束縛ノードの recolor (スタイルのみ部分更新) で反映される (保持型の高速パスと好相性)。
/// </summary>
public sealed record class Theme
{
    // 配色 (RGBA). Theme snapshots are replace-only; tokens cannot be mutated after construction.
    public uint Background { get; init; }
    public uint Surface { get; init; }
    public uint SurfaceAlt { get; init; }
    public uint BorderColor { get; init; }
    public uint Text { get; init; }
    public uint TextMuted { get; init; }
    public uint OnAccent { get; init; }
    public uint Primary { get; init; }
    public uint PrimaryHover { get; init; }
    public uint PrimaryActive { get; init; }
    public uint Success { get; init; }
    public uint Warning { get; init; }
    public uint Danger { get; init; }
    public uint Info { get; init; }

    // シンタックスハイライトのトークン色 (SH — コードブロック)。既定値は VS Code Light+/Dark+ 相当
    public uint TokComment { get; init; }
    public uint TokString { get; init; }
    public uint TokEscape { get; init; }
    public uint TokRegexp { get; init; }
    public uint TokNumber { get; init; }
    public uint TokConstant { get; init; }
    public uint TokKeyword { get; init; }
    public uint TokKeywordControl { get; init; }
    public uint TokOperator { get; init; }
    public uint TokFunction { get; init; }
    public uint TokType { get; init; }
    public uint TokVariable { get; init; }
    public uint TokTag { get; init; }
    public uint TokAttribute { get; init; }

    // 寸法
    public float Radius { get; init; } = 6;
    public float RadiusLg { get; init; } = 12;
    public float Space { get; init; } = 8;
    // Web typography baseline: body 16px, supporting text 14px, h2 24px, h1 32px.
    public float Font { get; init; } = 16;
    public float FontSm { get; init; } = 14;
    public float FontLg { get; init; } = 24;
    public float FontHeading { get; init; } = 32;

    // コントロール密度 (既定 = 従来のハードコード値。Compact() で詰まる)
    public float ControlH { get; init; } = 38;       // TextField/Select の高さ
    public float BtnPadX { get; init; } = 18;
    public float BtnPadY { get; init; } = 9;         // Button の既定 padding
    public float PadIn { get; init; } = 10;          // 入力系の内側左右 pad (Select は +2)
    public float CheckBox { get; init; } = 20;
    public float CheckGap { get; init; } = 9;

    /// <summary>この配色のまま寸法だけ詰めた高密度テーマを返す (ツール/インスペクタ向け)。</summary>
    public Theme Compact() => this with
    {
        // Compact changes density, not legibility. Typography stays on the web baseline.
        Font = 16,
        FontSm = 14,
        FontLg = 24,
        FontHeading = 32,
        Space = 4,
        Radius = 4,
        RadiusLg = 8,
        ControlH = 24,
        BtnPadX = 10,
        BtnPadY = 3,
        PadIn = 6,
        CheckBox = 14,
        CheckGap = 6,
    };

    private static uint C(byte r, byte g, byte b) => Color2D.Rgba(r, g, b);

    public static Theme Light => new()
    {
        Background = C(247, 248, 250),
        Surface = Color2D.White,
        SurfaceAlt = C(238, 240, 245),
        BorderColor = C(214, 218, 226),
        Text = C(28, 30, 36),
        TextMuted = C(112, 118, 130),
        OnAccent = Color2D.White,
        Primary = C(56, 118, 224),
        PrimaryHover = C(80, 140, 240),
        PrimaryActive = C(40, 96, 196),
        Success = C(46, 160, 90),
        Warning = C(214, 158, 46),
        Danger = C(220, 72, 72),
        Info = C(60, 150, 220),
        // VS Code Light+ のトークン色
        TokComment = C(0, 128, 0),
        TokString = C(163, 21, 21),
        TokEscape = C(238, 0, 0),
        TokRegexp = C(129, 31, 63),
        TokNumber = C(9, 134, 88),
        TokConstant = C(0, 112, 193),
        TokKeyword = C(0, 0, 255),
        TokKeywordControl = C(175, 0, 219),
        TokOperator = C(60, 60, 60),
        TokFunction = C(121, 94, 38),
        TokType = C(38, 127, 153),
        TokVariable = C(0, 16, 128),
        TokTag = C(128, 0, 0),
        TokAttribute = C(229, 0, 0),
    };

    public static Theme Dark => new()
    {
        Background = C(20, 22, 28),
        Surface = C(30, 33, 40),
        SurfaceAlt = C(40, 44, 52),
        BorderColor = C(56, 60, 70),
        Text = C(232, 235, 240),
        TextMuted = C(150, 156, 168),
        OnAccent = Color2D.White,
        Primary = C(86, 150, 250),
        PrimaryHover = C(112, 172, 255),
        PrimaryActive = C(66, 128, 226),
        Success = C(70, 184, 116),
        Warning = C(230, 178, 70),
        Danger = C(236, 100, 100),
        Info = C(96, 176, 240),
        // VS Code Dark+ のトークン色
        TokComment = C(106, 153, 85),
        TokString = C(206, 145, 120),
        TokEscape = C(215, 186, 125),
        TokRegexp = C(209, 105, 105),
        TokNumber = C(181, 206, 168),
        TokConstant = C(79, 193, 255),
        TokKeyword = C(86, 156, 214),
        TokKeywordControl = C(197, 134, 192),
        TokOperator = C(212, 212, 212),
        TokFunction = C(220, 220, 170),
        TokType = C(78, 201, 176),
        TokVariable = C(156, 220, 254),
        TokTag = C(86, 156, 214),
        TokAttribute = C(156, 220, 254),
    };
}

/// <summary>現在のテーマ (signal)。<c>UiTheme.Current.Value = Theme.Dark;</c> で全体を再配色。</summary>
public static class UiTheme
{
    public static readonly Signal<Theme> Current = new(Theme.Light);

    /// <summary>Effect 内で読むとテーマ変更を購読する。</summary>
    public static Theme T => Current.Value;
}
