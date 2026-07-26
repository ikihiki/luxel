namespace Luxel.Framework;

/// <summary>
/// per-frame パイプラインの実行区分。System は phase に登録され、engine が priority 順に実行する。
/// 標準 7 phase を engine が提供、<see cref="Create"/> でユーザー拡張可能。
/// </summary>
public sealed record Phase(string Name, int Priority)
{
    /// <summary>engine 側処理 (input poll, resources.Pump 等)。ユーザー登録は通常しない。</summary>
    public static readonly Phase EarlyUpdate = new(nameof(EarlyUpdate), 100);
    /// <summary>固定タイムステップの決定的シミュレーション (物理・キャラ制御)。1 フレームで 0 回以上、
    /// 溜まった分だけ回る。dt は常に <c>FixedDt</c> — 可変 dt に依存する処理は Update 側へ。</summary>
    public static readonly Phase FixedUpdate = new(nameof(FixedUpdate), 150);
    /// <summary>ゲームロジック本体。ユーザー system の中心。</summary>
    public static readonly Phase Update = new(nameof(Update), 200);
    /// <summary>Update 後の追従処理 (camera follow, animation blend 後の骨計算 等)。</summary>
    public static readonly Phase LateUpdate = new(nameof(LateUpdate), 300);
    /// <summary>Render 直前の準備 (TransformPropagate, Extractor 系)。</summary>
    public static readonly Phase PreRender = new(nameof(PreRender), 400);
    /// <summary>RenderGraph に pass を追加する phase。<c>FrameContext.RenderGraph</c> が非 null。</summary>
    public static readonly Phase Render = new(nameof(Render), 500);
    /// <summary>engine 側後処理 (AudioMixer.Tick, present 等)。</summary>
    public static readonly Phase PostRender = new(nameof(PostRender), 600);

    /// <summary>カスタム phase を作成。既存 phase の間に挟むには priority を調整 (例: 250 = Update と LateUpdate の間)。</summary>
    public static Phase Create(string name, int priority) => new(name, priority);

    public override string ToString() => $"{Name}({Priority})";
}
