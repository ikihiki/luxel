using Luxel.TwoD;

namespace Luxel.UI;

/// <summary>
/// 既存コントロールを**宣言的に組み合わせる**複合コントロールの基底 (docs/COMPOSITE_PLAN.md)。
/// <see cref="Build"/> が返したサブツリーへレイアウト/実体化を委譲する —
/// PerformLayout/RealizeCore は書かない (完全自前描画が要るときは従来どおり <see cref="Widget"/> 直接派生)。
///
/// 状態の 3 層 (規約):
/// - 外部 props = <c>[UiParam] Bindable</c> フィールド (呼び出し側/knobs が束縛)
/// - 内部の値状態 = <c>private Signal</c> — Bindable/getter で子へ配線し細粒度反映 (Rebuild 不要)
/// - 内部の構造状態 = フィールド — 変更したら <see cref="Rebuild"/> (明示)
///
/// **状態を保ちたい子コントロールはフィールドに保持し、Build ではその参照を組み込むこと** —
/// Rebuild で作り直されるのはコンテナ (StackPanel 等、状態なし) だけで、子インスタンスは生き残る。
/// </summary>
public abstract class CompositeControl : Widget
{
    private Widget? _root;
    private IDisposable? _trackSub;
    private int _buildGen;

    /// <summary>サブツリーを宣言する。初回レイアウト前に 1 回呼ばれ、<see cref="Rebuild"/> で作り直される。</summary>
    protected abstract Widget Build();

    /// <summary>Build 中に読んだ signal を自動追跡し、変化したら自動で <see cref="Rebuild"/> する (既定 true) —
    /// 通常は Rebuild を呼ぶタイミングを意識しなくてよい。
    /// **パフォーマンス制御**: 高頻度に変わる signal を Build で読むコントロールが再構築の頻度を
    /// 自分で決めたい場合は false にする (構造変化で明示的に Rebuild() を呼ぶ)。
    /// 注意: 追跡は「Build 本体での読み取り」だけが対象 — 値→表示は getter/Bindable 束縛で配線すれば
    /// 細粒度更新のままになる。追跡させたくない読み取りは <c>Peek()</c>。</summary>
    protected virtual bool TrackBuild => true;

    /// <summary>複合コントロール自身の登録 (ヒット/アニメーション/Effect) が要るときの任意フック。
    /// 登録はこの widget のスコープに入り、再実体化で破棄・再登録される。</summary>
    protected virtual void OnRealize(UiBuildContext ctx) { }

    /// <summary>構造が変わった (子の増減等) とき呼ぶ — Build() し直して dirty 伝播で部分再実体化する。
    /// **値の変化ではこれを呼ばない**こと (Bindable/getter 束縛の細粒度更新で反映する)。</summary>
    protected void Rebuild()
    {
        _root = null;
        MarkNeedsRealize();
    }

    /// <summary>エラー境界: Build が throw したら赤枠 + 例外メッセージへ縮退する
    /// (アプリは落とさない。次の Rebuild で本来の Build が再試行される)。</summary>
    private Widget BuildSafe()
    {
        try { return Build(); }
        catch (Exception ex)
        {
            UiError.Report(ex, $"{GetType().Name}.Build");
            return new ErrorWidget(ex);
        }
    }

    /// <summary>宣言済みのルート (デバッグ/テスト用)。未 Build なら null。</summary>
    public Widget? Root => _root;

    protected sealed override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        if (_root is null)
        {
            _trackSub?.Dispose();
            _trackSub = null;
            _buildGen++;
            if (TrackBuild) (_root, _trackSub) = Reactive.Track(BuildSafe, Rebuild);
            else _root = BuildSafe();
        }
        _root.Layout(c, ctx, parentUsesSize: true);
        _root.Offset = default;
        Size = c.Constrain(_root.Size);
    }

    protected sealed override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        SetWorldPos(worldOrigin + Offset);
        // 追跡購読の寿命をスコープへ (widget 破棄 = SetRoot/差し替えで解除 — リーク防止)。
        // 世代タグ付き: Rebuild 直後の再実体化では「旧スコープの破棄」が新しい購読より後に走るため、
        // 世代が一致するときだけ (= まだ現役の購読だけ) 解除する。
        if (_trackSub is not null)
        {
            int gen = _buildGen;
            CompositeControl self = this;
            ctx.Own(new TrackCleanup(self, gen));
        }
        OnRealize(ctx);
        _root!.Offset = Offset;   // 親が書いた自分の配置をルートへ引き継ぐ
        _root.Realize(ctx, parent, worldOrigin);
    }

    private sealed class TrackCleanup(CompositeControl owner, int gen) : IDisposable
    {
        public void Dispose()
        {
            if (owner._buildGen != gen) return;   // 既に次世代の購読 — 触らない
            owner._trackSub?.Dispose();
            owner._trackSub = null;
        }
    }

    public override IEnumerable<Widget> DebugChildren() => _root is null ? [] : [_root];
}
