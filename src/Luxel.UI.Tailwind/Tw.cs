using Luxel.TwoD;

namespace Luxel.UI.Tailwind;

/// <summary>
/// Tailwind CSS のデザイントークン (色スケール + サイズ定数) を C# const として提供。
///
/// <para>色は Tailwind v3 のパレットに対応 (Slate/Gray/Red/Orange/Amber/Yellow/Lime/Green/Emerald/Teal/Cyan/Sky/Blue/Indigo/Violet/Purple/Fuchsia/Pink/Rose)。
/// 数値スケール (50/100/200/.../900) で「明度の段階」を表す。</para>
///
/// <para>サイズはスペーシング (P1=4, P2=8, P4=16, ...) と角丸 (RoundedSm/Md/Lg/Xl/Full) を提供。</para>
///
/// <para>ユーザーの自前 <c>AppTheme</c> record で参照することを想定:</para>
/// <code>
/// public sealed record AppTheme {
///     public required uint Primary { get; init; }
/// }
/// var light = new AppTheme { Primary = Tw.Blue500 };
/// </code>
/// </summary>
public static class Tw
{
    // ============================================================
    // 色 (主要 10 色のみ初期収録、必要に応じて拡張)
    // ============================================================

    // Slate (neutral cool gray)
    public static readonly uint Slate50  = Color2D.Rgba(248, 250, 252);
    public static readonly uint Slate100 = Color2D.Rgba(241, 245, 249);
    public static readonly uint Slate200 = Color2D.Rgba(226, 232, 240);
    public static readonly uint Slate300 = Color2D.Rgba(203, 213, 225);
    public static readonly uint Slate400 = Color2D.Rgba(148, 163, 184);
    public static readonly uint Slate500 = Color2D.Rgba(100, 116, 139);
    public static readonly uint Slate600 = Color2D.Rgba( 71,  85, 105);
    public static readonly uint Slate700 = Color2D.Rgba( 51,  65,  85);
    public static readonly uint Slate800 = Color2D.Rgba( 30,  41,  59);
    public static readonly uint Slate900 = Color2D.Rgba( 15,  23,  42);

    // Red
    public static readonly uint Red50  = Color2D.Rgba(254, 242, 242);
    public static readonly uint Red100 = Color2D.Rgba(254, 226, 226);
    public static readonly uint Red300 = Color2D.Rgba(252, 165, 165);
    public static readonly uint Red400 = Color2D.Rgba(248, 113, 113);
    public static readonly uint Red500 = Color2D.Rgba(239,  68,  68);
    public static readonly uint Red600 = Color2D.Rgba(220,  38,  38);
    public static readonly uint Red700 = Color2D.Rgba(185,  28,  28);
    public static readonly uint Red900 = Color2D.Rgba(127,  29,  29);

    // Amber
    public static readonly uint Amber400 = Color2D.Rgba(251, 191,  36);
    public static readonly uint Amber500 = Color2D.Rgba(245, 158,  11);
    public static readonly uint Amber600 = Color2D.Rgba(217, 119,   6);

    // Green
    public static readonly uint Green100 = Color2D.Rgba(220, 252, 231);
    public static readonly uint Green400 = Color2D.Rgba( 74, 222, 128);
    public static readonly uint Green500 = Color2D.Rgba( 34, 197,  94);
    public static readonly uint Green600 = Color2D.Rgba( 22, 163,  74);
    public static readonly uint Green700 = Color2D.Rgba( 21, 128,  61);

    // Cyan
    public static readonly uint Cyan400 = Color2D.Rgba( 34, 211, 238);
    public static readonly uint Cyan500 = Color2D.Rgba(  6, 182, 212);

    // Sky
    public static readonly uint Sky400 = Color2D.Rgba( 56, 189, 248);
    public static readonly uint Sky500 = Color2D.Rgba( 14, 165, 233);
    public static readonly uint Sky600 = Color2D.Rgba(  2, 132, 199);

    // Blue (主要)
    public static readonly uint Blue50  = Color2D.Rgba(239, 246, 255);
    public static readonly uint Blue100 = Color2D.Rgba(219, 234, 254);
    public static readonly uint Blue200 = Color2D.Rgba(191, 219, 254);
    public static readonly uint Blue300 = Color2D.Rgba(147, 197, 253);
    public static readonly uint Blue400 = Color2D.Rgba( 96, 165, 250);
    public static readonly uint Blue500 = Color2D.Rgba( 59, 130, 246);
    public static readonly uint Blue600 = Color2D.Rgba( 37,  99, 235);
    public static readonly uint Blue700 = Color2D.Rgba( 29,  78, 216);
    public static readonly uint Blue800 = Color2D.Rgba( 30,  64, 175);
    public static readonly uint Blue900 = Color2D.Rgba( 30,  58, 138);

    // Indigo
    public static readonly uint Indigo500 = Color2D.Rgba( 99, 102, 241);
    public static readonly uint Indigo600 = Color2D.Rgba( 79,  70, 229);

    // Violet
    public static readonly uint Violet500 = Color2D.Rgba(139,  92, 246);

    // Pink
    public static readonly uint Pink500 = Color2D.Rgba(236,  72, 153);

    // White / Black
    public static readonly uint White = Color2D.White;
    public static readonly uint Black = Color2D.Rgba(0, 0, 0);
    public static readonly uint Transparent = 0x00000000u;

    // ============================================================
    // スペーシング (Tailwind の spacing scale = 4px 刻み)
    // p-0=0, p-1=4, p-2=8, p-3=12, p-4=16, p-6=24, p-8=32, p-12=48, p-16=64
    // ============================================================
    public const float P0  = 0f;
    public const float P1  = 4f;
    public const float P2  = 8f;
    public const float P3  = 12f;
    public const float P4  = 16f;
    public const float P5  = 20f;
    public const float P6  = 24f;
    public const float P8  = 32f;
    public const float P10 = 40f;
    public const float P12 = 48f;
    public const float P16 = 64f;
    public const float P20 = 80f;
    public const float P24 = 96f;

    // ============================================================
    // 角丸 (Tailwind の rounded-*)
    // rounded-sm=2, rounded=4, rounded-md=6, rounded-lg=8, rounded-xl=12, rounded-2xl=16, rounded-full=9999
    // ============================================================
    public const float RoundedNone = 0f;
    public const float RoundedSm = 2f;
    public const float Rounded = 4f;
    public const float RoundedMd = 6f;
    public const float RoundedLg = 8f;
    public const float RoundedXl = 12f;
    public const float Rounded2xl = 16f;
    public const float Rounded3xl = 24f;
    public const float RoundedFull = 9999f;
}
