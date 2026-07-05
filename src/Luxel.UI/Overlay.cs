namespace Luxel.UI;

/// <summary>オーバーレイの配置方法。</summary>
public enum OverlayPlacement
{
    Center,          // ダイアログ (画面中央)
    Below,           // アンカー矩形の下 (はみ出すなら上にフリップ)
    Above,           // アンカー矩形の上
    RightEdge,       // 右端 (ドロワー)
    LeftEdge,        // 左端
    BottomEdge,      // 下端
    CornerBottomRight, // 右下隅 (トースト)
}

/// <summary>
/// 最前面レイヤへ実体化されるオーバーレイ。<see cref="Open"/> 切替で opacity 部分更新。
/// アンカー配置 (flip/clamp) は実体化時に計算。<see cref="ContentRect"/> は外側クリック判定に使う。
/// </summary>
public sealed class OverlayEntry
{
    public required Signal<bool> Open { get; init; }
    public required Widget Content { get; init; }
    public OverlayPlacement Placement { get; init; } = OverlayPlacement.Center;
    public Func<Rect>? Anchor { get; init; }          // トリガのワールド矩形 (Below/Above 用)
    public bool Modal { get; init; }                  // scrim + 背面ブロック
    public bool DismissOnOutside { get; init; } = true;
    public float Gap { get; init; } = 6;
    public float Margin { get; init; } = 16;          // 端/隅からの余白

    internal Rect ContentRect;                        // 実体化後の content ワールド矩形
}
