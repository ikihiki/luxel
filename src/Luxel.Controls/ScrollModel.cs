using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// 縦/横 1 軸ぶんのスクロール計算の共通機構。<see cref="ScrollViewer"/>/<see cref="ListView"/> のような
/// スクロールバー付きコントロールと、自前描画パイプラインを持つもの (<see cref="RichTextEditor"/> 等) が
/// 同じ数式 (クランプ・サム幾何・ドラッグ写像) を共有する。
///
/// **オフセットだけでなく内容長/ビューポート長も Signal** で持つのが要点 —
/// 幅変更などで内容長が変わったとき、<see cref="Clamped"/> を読む effect (content transform 等) が
/// 確実に再実行され、保持していたスクロール位置まで自動でスクロールし直される
/// (オフセットへの同値クランプ書き込みだけでは signal が発火せず、実体化時に掴んだ 0 のまま
/// 取り残される — Docs ページの幅変更で先頭に戻っていた原因)。
/// フィールドとして持てば再実体化をまたいで位置が生き残る。
/// </summary>
public sealed class ScrollModel
{
    /// <summary>目標オフセット (px)。平滑表示するコントロールはこれを目標に UiStates で追従する。</summary>
    public Signal<float> Offset { get; } = new(0);

    private readonly Signal<float> _content = new(0);
    private readonly Signal<float> _viewport = new(0);

    /// <summary>内容全長 (px、リアクティブ読み)。</summary>
    public float ContentLength => _content.Value;
    /// <summary>ビューポート長 (px、リアクティブ読み)。</summary>
    public float ViewportLength => _viewport.Value;

    /// <summary>スクロール可能量 (リアクティブ読み — effect 内で読むと寸法変更にも追従する)。</summary>
    public float MaxScroll => MathF.Max(0, _content.Value - _viewport.Value);

    /// <summary>クランプ済みオフセット (リアクティブ読み)。content transform はこれを使う。</summary>
    public float Clamped => Math.Clamp(Offset.Value, 0, MaxScroll);

    private float MaxScrollPeek => MathF.Max(0, _content.Peek() - _viewport.Peek());
    /// <summary>クランプ済みオフセット (非リアクティブ — 入力ハンドラ用)。</summary>
    public float ClampedPeek => Math.Clamp(Offset.Peek(), 0, MaxScrollPeek);

    /// <summary>内容長/ビューポート長を更新する (レイアウト/Refresh から毎回呼んでよい —
    /// 変化が無ければ signal を揺らさない)。オフセットは新しい範囲へクランプされる。</summary>
    public void SetLengths(float content, float viewport)
    {
        if (content != _content.Peek()) _content.Value = content;
        if (viewport != _viewport.Peek()) _viewport.Value = viewport;
        float c = Math.Clamp(Offset.Peek(), 0, MaxScrollPeek);
        if (c != Offset.Peek()) Offset.Value = c;
    }

    /// <summary>絶対位置へスクロール (クランプされる)。</summary>
    public void ScrollTo(float offset) => Offset.Value = Math.Clamp(offset, 0, MaxScrollPeek);

    /// <summary>相対スクロール (ホイール等)。</summary>
    public void ScrollBy(float delta) => ScrollTo(ClampedPeek + delta);

    /// <summary>内容座標の [top, bottom] が pad を残して見えるまで最小移動する (キャレット追従用)。</summary>
    public void EnsureVisible(float top, float bottom, float pad = 0)
    {
        float s = ClampedPeek;
        float view = _viewport.Peek();
        if (top - s < pad) s = MathF.Max(0, top - pad);
        else if (bottom - s > view - pad) s = bottom - view + pad;
        ScrollTo(s);
    }

    // ---- サム幾何 (トラック長 = ビューポート長の縦バー想定。横でも同式) ----

    /// <summary>サム長 (リアクティブ読み)。内容比例、minLength を下回らない。</summary>
    public float ThumbLength(float minLength = 28)
    {
        float content = _content.Value, view = _viewport.Value;
        return content <= 0 ? view : MathF.Max(minLength, view * view / MathF.Max(view, content));
    }

    /// <summary>表示オフセット (平滑値でも可) に対応するサム上端位置 (リアクティブ読み)。</summary>
    public float ThumbPos(float displayOffset, float minLength = 28)
    {
        float max = MaxScroll;
        return max > 0 ? Math.Clamp(displayOffset / max, 0, 1) * (_viewport.Value - ThumbLength(minLength)) : 0;
    }

    /// <summary>サム上端位置 → オフセット (ドラッグ写像、非リアクティブ)。</summary>
    public float OffsetForThumbTop(float top, float minLength = 28)
    {
        float view = _viewport.Peek();
        float th = _content.Peek() <= 0 ? view
            : MathF.Max(minLength, view * view / MathF.Max(view, _content.Peek()));
        return Math.Clamp(top / MathF.Max(1, view - th), 0, 1) * MaxScrollPeek;
    }

    /// <summary>サムドラッグ開始 (非リアクティブ)。サム上なら掴んだ点の食い込み、トラック上なら
    /// サム中央掴み (= その位置へジャンプしてそのまま掴む) の grab オフセットを返す。</summary>
    public float BeginThumbDrag(float pos, float minLength = 28)
    {
        float view = _viewport.Peek();
        float th = _content.Peek() <= 0 ? view
            : MathF.Max(minLength, view * view / MathF.Max(view, _content.Peek()));
        float max = MaxScrollPeek;
        float thumbTop = max > 0 ? ClampedPeek / max * (view - th) : 0;
        bool onThumb = pos >= thumbTop && pos <= thumbTop + th;
        return onThumb ? pos - thumbTop : th / 2;
    }
}

/// <summary>
/// 縦スクロールバー (サム + ドラッグ) の共通実装。右端に thumb ノードを作り、
/// 形/位置/表示は <see cref="ScrollModel"/> の signal に追従する (内容が収まるときは透明)。
/// ドラッグはサム掴み / トラック押下はジャンプしてそのまま掴む。
/// </summary>
public static class ScrollBars
{
    public const float ThumbW = 6f, Pad = 2f;
    /// <summary>掴み判定の幅 (描画より広め)。コンテンツのヒットより後に登録すれば前面が勝つ。</summary>
    public const float GrabW = ThumbW + Pad * 2 + 6;

    /// <summary>host の右端 (viewW - ThumbW - Pad) に縦バーを取り付ける。
    /// displayOffset は平滑表示するコントロールの表示値 (UiStates) — 省略時は model.Clamped。
    /// onDirectChange はサムドラッグの直前に呼ばれる (平滑チャネルの切替用)。</summary>
    public static UiNode AttachVertical(UiBuildContext ctx, UiNode host, ScrollModel model,
        float viewW, float viewH,
        Func<float>? displayOffset = null, Action? onDirectChange = null,
        Bindable<uint>? color = null, float minThumb = 28)
    {
        UiNode bar = ctx.Canvas.AddChild(host);
        bar.Z = 2;
        bar.Opacity = 0f;
        ctx.Effect(() => bar.Color = color is null ? ctx.Theme.Value.BorderColor : color.Or(ctx.Theme.Value.BorderColor));

        float thumbLen = -1;
        ctx.Effect(() =>
        {
            if (model.MaxScroll <= 0) { bar.Opacity = 0f; return; }
            bar.Opacity = 1f;
            float th = model.ThumbLength(minThumb);
            if (th != thumbLen)
            {
                thumbLen = th;
                var s = new Scene2D();
                s.FillRoundedRect(Color2D.White, viewW - ThumbW - Pad, 0, ThumbW, th, ThumbW / 2);
                bar.Content = s;
            }
            float off = displayOffset?.Invoke() ?? model.Clamped;
            bar.Transform = Affine2D.Translate(0, model.ThumbPos(off, minThumb));
        });

        float grab = 0;
        void SetFromThumbTop(float top)
        {
            onDirectChange?.Invoke();
            model.ScrollTo(model.OffsetForThumbTop(top, minThumb));
        }
        ctx.AddHit(host, new Rect(viewW - GrabW, 0, GrabW, viewH),
            onDragStart: e =>
            {
                if (model.MaxScroll <= 0) return;
                grab = model.BeginThumbDrag(e.Y, minThumb);
                SetFromThumbTop(e.Y - grab);
            },
            onDrag: e => { if (model.MaxScroll > 0) SetFromThumbTop(e.Y - grab); });
        return bar;
    }
}
