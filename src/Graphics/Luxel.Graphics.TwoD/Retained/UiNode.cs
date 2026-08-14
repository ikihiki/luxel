namespace Luxel.Graphics.TwoD;

/// <summary>クリップ矩形 (ワールド座標)。</summary>
public readonly record struct RectClip(float X, float Y, float W, float H);

/// <summary>
/// 保持型ツリーのノード。ローカル変換・色・不透明度・クリップ・Z・子・コンテンツ(ローカル座標の図形)を持つ。
/// プロパティ変更は <see cref="RetainedCanvas"/> に dirty を伝える (移動=変換のみ, 色=スタイルのみ更新)。
/// </summary>
public sealed class UiNode
{
    private readonly RetainedCanvas _canvas;
    private Affine2D _local = Affine2D.Identity;
    private uint _color = Color2D.White;
    private float _opacity = 1f;
    private bool _visible = true;
    private float _z;
    private RectClip? _clip;

    internal UiNode(RetainedCanvas canvas) => _canvas = canvas;

    public UiNode? Parent { get; internal set; }
    public List<UiNode> Children { get; } = new();

    private Scene2D? _content;

    /// <summary>ローカル座標の図形 (この描画ノードの内容)。色はノードの <see cref="Color"/> が優先。
    /// 差し替えは content dirty として部分更新される (新しい線分/パス数が既存レンジに収まれば
    /// in-place 書き込み、収まらなければフル再構築へフォールバック) — 手動の
    /// <see cref="RetainedCanvas.Invalidate"/> は不要。</summary>
    public Scene2D? Content
    {
        get => _content;
        set { _content = value; _canvas.MarkContentDirty(this); }
    }

    public Affine2D Transform
    {
        get => _local;
        set { _local = value; _canvas.MarkTransformDirty(this); }
    }

    public uint Color
    {
        get => _color;
        set { if (_color == value) return; _color = value; _canvas.MarkStyleDirty(this); }
    }

    /// <summary>不透明度。<b>実効値は 親 × 自分 の積</b> (compositor 風に子へ継承される)。
    /// 変更はサブツリーのスタイル部分更新で反映。</summary>
    public float Opacity
    {
        get => _opacity;
        set { if (_opacity == value) return; _opacity = value; _canvas.MarkStyleDirty(this); }
    }

    /// <summary>可視性 (<b>サブツリーへ継承</b>)。false で自分と子孫を描画順 (Order) から除外する —
    /// パス/スタイル等のバッファは保持したまま order バッファのみ再構成するので、
    /// 切替は軽量な部分更新で、非表示中の GPU コストはゼロ (Flutter の IndexedStack / CSS display:none 相当)。</summary>
    public bool Visible
    {
        get => _visible;
        set { if (_visible == value) return; _visible = value; _canvas.MarkOrderDirty(); }
    }

    public float Z
    {
        get => _z;
        set { if (_z == value) return; _z = value; _canvas.MarkOrderDirty(); }   // 描画順のみ (order 再構成)
    }

    public RectClip? Clip
    {
        get => _clip;
        set { _clip = value; _canvas.MarkClipDirty(this); }
    }

    /// <summary>このノードを再描画対象にする (SoA の再アップロードは style slot 1 個のみ)。
    /// **外部バッファをサンプリングする image ノード** (SurfaceView/ImageView) の中身だけが変わった
    /// ときに呼ぶ — シーン構造は不変でもラスタライズし直せば新しい絵になるため。
    /// (旧イディオム `node.Color = node.Color` は setter の同値 early-out で無効になった。)</summary>
    public void Touch() => _canvas.MarkStyleDirty(this);

    /// <summary>Content (Scene2D) の**形状ごとの色**を尊重する (既定 false = 1 ノード 1 色 —
    /// パスは白描き + <see cref="Color"/> が実色)。true にすると多色シーンを 1 ノードで描ける
    /// (Canvas2D 等の 2D 直描き用)。制約: このノードの <see cref="Color"/>/<see cref="Opacity"/> の
    /// 実行時変更は content の色に反映されない (エンコード時の実効 opacity で焼き込み)。
    /// 実体化前 (最初の Rebuild 前) に設定すること。</summary>
    private bool _contentColors;
    public bool ContentColors
    {
        get => _contentColors;
        set { if (_contentColors == value) return; _contentColors = value; _canvas.MarkStructureDirty(); }
    }

    /// <summary>Content 差し替えの最低予約 (仮想化リスト等、ワーストケースが分かる場合)。
    /// 次の Rebuild からこのノードの線分/パス容量が指定値以上確保され、以内の差し替えは
    /// in-place (フル再構築なし) で受けられる。サイズは <see cref="Scene2D.CountEncoded"/> で見積もる。</summary>
    public void ReserveContent(int segments, int paths)
    {
        if (ReserveSegs == segments && ReservePaths == paths) return;
        ReserveSegs = segments;
        ReservePaths = paths;
        _canvas.MarkStructureDirty();
    }

    /// <summary>祖先を辿った「現在の」ワールド変換 (Flush のキャッシュでなく都度計算)。
    /// ヒットテスト等、描画反映前でも最新の変換が要る用途向け。</summary>
    public Affine2D ComputeWorldNow()
        => Parent is null ? _local : Affine2D.Mul(Parent.ComputeWorldNow(), _local);

    /// <summary>自分と祖先がすべて <see cref="Visible"/> か (画面に出ているか)。</summary>
    public bool IsShown
    {
        get
        {
            for (UiNode? n = this; n != null; n = n.Parent)
                if (!n._visible) return false;
            return true;
        }
    }

    // ---- バックエンド (スロット/レンジ/キャッシュ) ----
    internal int TransformSlot = -1, StyleSlot = -1, ClipSlot = -1;
    /// <summary>ContentColors 用の形状別スタイルレンジ先頭 (PathCapacity 個予約、Rebuild で割当)。</summary>
    internal int ContentStyleStart = -1;
    /// <summary>パスレンジ (Rebuild で割当)。PathCount = 現在の実数、PathCapacity = スラック込みの予約数 —
    /// 予約以内のパス数変化は in-place + order 再構成だけで反映できる (フル再構築なし)。</summary>
    internal int PathStart, PathCount, PathCapacity;
    /// <summary>線分レンジ (Rebuild で割当)。SegCapacity 以内の Content 差し替えは in-place 更新できる。</summary>
    internal int SegStart, SegCapacity;
    internal int ReserveSegs, ReservePaths;   // ReserveContent の最低予約 (0 = 指定なし)
    internal Affine2D World;
    internal float EffectiveOpacity = 1f;   // 親 × 自分 (Rebuild / スタイル部分更新で再計算)
}
