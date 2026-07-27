namespace Luxel.Graphics.TwoD;

/// <summary>
/// <see cref="TileMap"/> を <see cref="RetainedCanvas"/> 上に描く保持型レイヤ。
/// **可視チャンクのみ UiNode として実体化**し (範囲外はノードを破棄せず <see cref="UiNode.Visible"/>=false で再利用)、
/// <see cref="TileMap.SetTile"/> で dirty になったチャンクだけ <see cref="Update"/> 時に Content を再構築する
/// (静的チャンクはジオメトリ不変 = スクロールはカメラ側)。大マップでも実体化するのは画面周辺のチャンクだけ。
/// </summary>
public sealed class TileMapLayer
{
    private readonly RetainedCanvas _canvas;
    private readonly TileMap _map;
    private readonly Dictionary<(int Cx, int Cy), UiNode> _chunks = new();

    public TileMapLayer(RetainedCanvas canvas, UiNode parent, TileMap map)
    {
        _canvas = canvas;
        _map = map;
        Root = canvas.AddChild(parent);
    }

    /// <summary>全チャンクノードの親。カメラ/親変換はこのノード経由で効く。</summary>
    public UiNode Root { get; }

    /// <summary>現在実体化されている (= 一度でも可視になった) チャンク数。</summary>
    public int RealizedChunkCount => _chunks.Count;

    /// <summary>ワールド表示矩形 <paramref name="worldView"/> に基づき可視チャンクを更新する。
    /// 可視範囲のチャンクは (無ければ) 実体化し、dirty なら再構築。範囲外の既存ノードは非表示にする。</summary>
    public void Update(RectF worldView)
    {
        (int x0, int y0, int x1, int y1) = _map.VisibleChunks(worldView);

        // まず全既存ノードを非表示に倒し、可視範囲だけ後で戻す (差分更新)。
        foreach (UiNode n in _chunks.Values)
            n.Visible = false;

        for (int cy = y0; cy <= y1; cy++)
            for (int cx = x0; cx <= x1; cx++)
            {
                if (!_chunks.TryGetValue((cx, cy), out UiNode? node))
                {
                    node = _canvas.AddChild(Root);
                    _chunks[(cx, cy)] = node;
                    Build(node, cx, cy);
                    _map.ClearChunkDirty(cx, cy);
                }
                else if (_map.IsChunkDirty(cx, cy))
                {
                    Build(node, cx, cy);
                    _map.ClearChunkDirty(cx, cy);
                }
                node.Visible = true;
            }
    }

    private void Build(UiNode node, int cx, int cy)
    {
        var scene = new Scene2D();
        _map.AppendChunk(cx, cy, scene);
        node.Content = scene;
    }
}
