using Luxel.Graphics.TwoD;

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
    /// <summary>Tailwind の Slate-50。</summary>
    public static readonly uint Slate50 = Color2D.Rgba(248, 250, 252);
    /// <summary>Tailwind の Slate-100。</summary>
    public static readonly uint Slate100 = Color2D.Rgba(241, 245, 249);
    /// <summary>Tailwind の Slate-200。</summary>
    public static readonly uint Slate200 = Color2D.Rgba(226, 232, 240);
    /// <summary>Tailwind の Slate-300。</summary>
    public static readonly uint Slate300 = Color2D.Rgba(203, 213, 225);
    /// <summary>Tailwind の Slate-400。</summary>
    public static readonly uint Slate400 = Color2D.Rgba(148, 163, 184);
    /// <summary>Tailwind の Slate-500。</summary>
    public static readonly uint Slate500 = Color2D.Rgba(100, 116, 139);
    /// <summary>Tailwind の Slate-600。</summary>
    public static readonly uint Slate600 = Color2D.Rgba(71, 85, 105);
    /// <summary>Tailwind の Slate-700。</summary>
    public static readonly uint Slate700 = Color2D.Rgba(51, 65, 85);
    /// <summary>Tailwind の Slate-800。</summary>
    public static readonly uint Slate800 = Color2D.Rgba(30, 41, 59);
    /// <summary>Tailwind の Slate-900。</summary>
    public static readonly uint Slate900 = Color2D.Rgba(15, 23, 42);

    // Red
    /// <summary>Tailwind の Red-50。</summary>
    public static readonly uint Red50 = Color2D.Rgba(254, 242, 242);
    /// <summary>Tailwind の Red-100。</summary>
    public static readonly uint Red100 = Color2D.Rgba(254, 226, 226);
    /// <summary>Tailwind の Red-300。</summary>
    public static readonly uint Red300 = Color2D.Rgba(252, 165, 165);
    /// <summary>Tailwind の Red-400。</summary>
    public static readonly uint Red400 = Color2D.Rgba(248, 113, 113);
    /// <summary>Tailwind の Red-500。</summary>
    public static readonly uint Red500 = Color2D.Rgba(239, 68, 68);
    /// <summary>Tailwind の Red-600。</summary>
    public static readonly uint Red600 = Color2D.Rgba(220, 38, 38);
    /// <summary>Tailwind の Red-700。</summary>
    public static readonly uint Red700 = Color2D.Rgba(185, 28, 28);
    /// <summary>Tailwind の Red-900。</summary>
    public static readonly uint Red900 = Color2D.Rgba(127, 29, 29);

    // Amber
    /// <summary>Tailwind の Amber-400。</summary>
    public static readonly uint Amber400 = Color2D.Rgba(251, 191, 36);
    /// <summary>Tailwind の Amber-500。</summary>
    public static readonly uint Amber500 = Color2D.Rgba(245, 158, 11);
    /// <summary>Tailwind の Amber-600。</summary>
    public static readonly uint Amber600 = Color2D.Rgba(217, 119, 6);

    // Green
    /// <summary>Tailwind の Green-100。</summary>
    public static readonly uint Green100 = Color2D.Rgba(220, 252, 231);
    /// <summary>Tailwind の Green-400。</summary>
    public static readonly uint Green400 = Color2D.Rgba(74, 222, 128);
    /// <summary>Tailwind の Green-500。</summary>
    public static readonly uint Green500 = Color2D.Rgba(34, 197, 94);
    /// <summary>Tailwind の Green-600。</summary>
    public static readonly uint Green600 = Color2D.Rgba(22, 163, 74);
    /// <summary>Tailwind の Green-700。</summary>
    public static readonly uint Green700 = Color2D.Rgba(21, 128, 61);

    // Cyan
    /// <summary>Tailwind の Cyan-400。</summary>
    public static readonly uint Cyan400 = Color2D.Rgba(34, 211, 238);
    /// <summary>Tailwind の Cyan-500。</summary>
    public static readonly uint Cyan500 = Color2D.Rgba(6, 182, 212);

    // Sky
    /// <summary>Tailwind の Sky-400。</summary>
    public static readonly uint Sky400 = Color2D.Rgba(56, 189, 248);
    /// <summary>Tailwind の Sky-500。</summary>
    public static readonly uint Sky500 = Color2D.Rgba(14, 165, 233);
    /// <summary>Tailwind の Sky-600。</summary>
    public static readonly uint Sky600 = Color2D.Rgba(2, 132, 199);

    // Blue (主要)
    /// <summary>Tailwind の Blue-50。</summary>
    public static readonly uint Blue50 = Color2D.Rgba(239, 246, 255);
    /// <summary>Tailwind の Blue-100。</summary>
    public static readonly uint Blue100 = Color2D.Rgba(219, 234, 254);
    /// <summary>Tailwind の Blue-200。</summary>
    public static readonly uint Blue200 = Color2D.Rgba(191, 219, 254);
    /// <summary>Tailwind の Blue-300。</summary>
    public static readonly uint Blue300 = Color2D.Rgba(147, 197, 253);
    /// <summary>Tailwind の Blue-400。</summary>
    public static readonly uint Blue400 = Color2D.Rgba(96, 165, 250);
    /// <summary>Tailwind の Blue-500。</summary>
    public static readonly uint Blue500 = Color2D.Rgba(59, 130, 246);
    /// <summary>Tailwind の Blue-600。</summary>
    public static readonly uint Blue600 = Color2D.Rgba(37, 99, 235);
    /// <summary>Tailwind の Blue-700。</summary>
    public static readonly uint Blue700 = Color2D.Rgba(29, 78, 216);
    /// <summary>Tailwind の Blue-800。</summary>
    public static readonly uint Blue800 = Color2D.Rgba(30, 64, 175);
    /// <summary>Tailwind の Blue-900。</summary>
    public static readonly uint Blue900 = Color2D.Rgba(30, 58, 138);

    // Indigo
    /// <summary>Tailwind の Indigo-500。</summary>
    public static readonly uint Indigo500 = Color2D.Rgba(99, 102, 241);
    /// <summary>Tailwind の Indigo-600。</summary>
    public static readonly uint Indigo600 = Color2D.Rgba(79, 70, 229);

    // Violet
    /// <summary>Tailwind の Violet-500。</summary>
    public static readonly uint Violet500 = Color2D.Rgba(139, 92, 246);

    // Pink
    /// <summary>Tailwind の Pink-500。</summary>
    public static readonly uint Pink500 = Color2D.Rgba(236, 72, 153);

    // White / Black
    /// <summary>白 (#FFFFFF)。</summary>
    public static readonly uint White = Color2D.White;
    /// <summary>黒 (#000000)。</summary>
    public static readonly uint Black = Color2D.Rgba(0, 0, 0);
    /// <summary>完全透明。</summary>
    public static readonly uint Transparent = 0x00000000u;

    // ============================================================
    // スペーシング (Tailwind の spacing scale = 4px 刻み)
    // p-0=0, p-1=4, p-2=8, p-3=12, p-4=16, p-6=24, p-8=32, p-12=48, p-16=64
    // ============================================================
    /// <summary>Tailwind spacing 0 (0px)。</summary>
    public const float P0 = 0f;
    /// <summary>Tailwind spacing 1 (4px)。</summary>
    public const float P1 = 4f;
    /// <summary>Tailwind spacing 2 (8px)。</summary>
    public const float P2 = 8f;
    /// <summary>Tailwind spacing 3 (12px)。</summary>
    public const float P3 = 12f;
    /// <summary>Tailwind spacing 4 (16px)。</summary>
    public const float P4 = 16f;
    /// <summary>Tailwind spacing 5 (20px)。</summary>
    public const float P5 = 20f;
    /// <summary>Tailwind spacing 6 (24px)。</summary>
    public const float P6 = 24f;
    /// <summary>Tailwind spacing 8 (32px)。</summary>
    public const float P8 = 32f;
    /// <summary>Tailwind spacing 10 (40px)。</summary>
    public const float P10 = 40f;
    /// <summary>Tailwind spacing 12 (48px)。</summary>
    public const float P12 = 48f;
    /// <summary>Tailwind spacing 16 (64px)。</summary>
    public const float P16 = 64f;
    /// <summary>Tailwind spacing 20 (80px)。</summary>
    public const float P20 = 80f;
    /// <summary>Tailwind spacing 24 (96px)。</summary>
    public const float P24 = 96f;

    // ============================================================
    // 角丸 (Tailwind の rounded-*)
    // rounded-sm=2, rounded=4, rounded-md=6, rounded-lg=8, rounded-xl=12, rounded-2xl=16, rounded-full=9999
    // ============================================================
    /// <summary>Tailwind の rounded-none (0px)。</summary>
    public const float RoundedNone = 0f;
    /// <summary>Tailwind の rounded-sm (2px)。</summary>
    public const float RoundedSm = 2f;
    /// <summary>Tailwind の rounded (4px)。</summary>
    public const float Rounded = 4f;
    /// <summary>Tailwind の rounded-md (6px)。</summary>
    public const float RoundedMd = 6f;
    /// <summary>Tailwind の rounded-lg (8px)。</summary>
    public const float RoundedLg = 8f;
    /// <summary>Tailwind の rounded-xl (12px)。</summary>
    public const float RoundedXl = 12f;
    /// <summary>Tailwind の rounded-2xl (16px)。</summary>
    public const float Rounded2xl = 16f;
    /// <summary>Tailwind の rounded-3xl (24px)。</summary>
    public const float Rounded3xl = 24f;
    /// <summary>Tailwind の rounded-full (9999px)。</summary>
    public const float RoundedFull = 9999f;
}
