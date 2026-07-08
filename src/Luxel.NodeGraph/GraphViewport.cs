using System.Numerics;

namespace Luxel.NodeGraph;

/// <summary>
/// キャンバスの pan/zoom 状態 — world → screen 変換の素。<see cref="Pan"/> は world 原点の画面位置オフセット、
/// <see cref="Zoom"/> は倍率。純射影ジオメトリ (S3) がこれを使って world↔screen を解く。状態の一部だが
/// undo 対象ではない (パン/ズームは履歴に積まない)。
/// </summary>
public readonly record struct GraphViewport(Vector2 Pan, float Zoom)
{
    /// <summary>原点・等倍。</summary>
    public static readonly GraphViewport Default = new(Vector2.Zero, 1f);
}
