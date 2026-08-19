using Luxel.Graphics.TwoD;
using Luxel.Typography;

namespace Luxel.UI;

/// <summary>
/// 添付プロパティ値。<see cref="Key"/> は文字列キー (例 "Grid.Column")。別アセンブリのコントロールは
/// 独自キーを使える。<see cref="Value"/> は任意型 (int/enum 等)。
/// </summary>
public readonly record struct Attached(string Key, object? Value);

/// <summary>レイアウト中に必要な文脈 (テキスト計測のフォント等)。</summary>
public sealed class LayoutContext
{
    public required VectorFont Font { get; init; }

    /// <summary>レイアウト時点のテーマ (寸法トークン参照用)。UiHost が SetRoot 毎に自ホストの値を入れる。
    /// 既定 = プロセス既定テーマの現在値 (購読なしの読み)。</summary>
    public Theme Theme = UiTheme.Current.Peek();

    /// <summary>ビューポート (UiHost の論理サイズ)。<see cref="Length"/> の vw/vh の基準。
    /// UiHost が SetRoot/Resize で入れる (ホスト無しのテストでは 0 = vw/vh は 0 に解決)。</summary>
    public float ViewportW, ViewportH;
}

/// <summary>ヒットテスト対象 (アンカーノード + ローカル矩形)。
/// 判定時にノードの「現在の」ワールド変換の逆でポインタ座標をローカル空間へ写して判定するため、
/// スクロール/スライド/scale などの transform 変化と <see cref="UiNode.Visible"/> に自動追従する
/// (WPF の VisualTreeHelper.HitTest / Flutter の hitTest と同じ方式)。</summary>
public sealed class HitTarget
{
    /// <summary>判定の基準ノード (通常はコントロールのルート UiNode)。</summary>
    public required UiNode Node { get; init; }
    /// <summary><see cref="Node"/> のローカル座標での矩形。</summary>
    public required Rect Rect { get; init; }
    public Action? OnClick { get; init; }
    public Action<PointerEvent>? OnClickPos { get; init; }   // クリック座標付き
    public Action<bool>? OnHover { get; init; }    // true=enter / false=leave
    /// <summary>非捕獲時のポインタ移動。ヒット中は毎移動呼ばれる (SurfaceView 等の転送用)。</summary>
    public Action<PointerEvent>? OnMovePos { get; init; }
    public FocusTarget? Focus { get; init; }       // クリックでこの対象にフォーカス

    // ---- ドラッグ (ポインタキャプチャ)。PointerDown でこの対象に捕獲され、
    //      以後の移動は矩形外でも OnDrag に届く (開始位置/差分は PointerEvent が持つ)。----
    /// <summary>ドラッグ開始 (押下位置)。これか <see cref="OnDrag"/> があると PointerDown で捕獲される。</summary>
    public Action<PointerEvent>? OnDragStart { get; init; }
    /// <summary>捕獲中の移動 (矩形外も届く)。移動量は <see cref="PointerEvent.DeltaX"/> (画面絶対基準)。</summary>
    public Action<PointerEvent>? OnDrag { get; init; }
    /// <summary>ドラッグ終了 (離した位置)。</summary>
    public Action<PointerEvent>? OnDragEnd { get; init; }
    internal bool Draggable => OnDragStart is not null || OnDrag is not null;

    /// <summary>false を返す間このヒットは無効 (論理的なゲート用)。null = 常に有効。
    /// 可視性 (Visible) は判定側が自動で見るのでここでは扱わない。</summary>
    public Func<bool>? Active { get; set; }

    /// <summary>ヒットレイヤ (既定 0)。判定はレイヤ大 → ノード深さ → 登録順の優先 —
    /// フローティングパネル等「浅いが最前面」の UI が、背面のより深いヒットに負けないための層。
    /// 登録側には <see cref="UiBuildContext.HitLayer"/> (ambient) が刻印される。</summary>
    public int Layer { get; set; }

    /// <summary>hover 中のカーソル形状 (テキスト編集=IBeam、Splitter=Resize 等)。既定 = 矢印。</summary>
    public CursorKind Cursor { get; init; } = CursorKind.Arrow;
    /// <summary>動的なカーソル (SurfaceView の子転送等)。非 null なら <see cref="Cursor"/> より優先。</summary>
    public Func<CursorKind>? CursorFunc { get; init; }
    /// <summary>右クリック (コンテキストメニュー要求)。メニューを canvas 座標へ置くときは
    /// <see cref="PointerEvent.ScreenX"/> か <c>Node.ComputeWorldNow().Apply(local)</c> を使う。</summary>
    public Action<PointerEvent>? OnContext { get; init; }

    // ---- ペイロード付きドラッグの受け側 (QP-M4 アプリ内 D&D)。
    //      ドラッグは UiHost.BeginDrag で始まり、以後この対象が最前面ドロップ先として拾われる。----
    /// <summary>ドロップ (payload + 座標)。これがあるとドロップ先候補になる。</summary>
    public Action<object, PointerEvent>? OnDrop { get; init; }
    /// <summary>ドラッグ中、この対象の上をポインタが動くたび (インジケータ更新用)。</summary>
    public Action<object, PointerEvent>? OnDropMove { get; init; }
    /// <summary>ドロップ先としての enter/leave (ハイライト用)。</summary>
    public Action<bool>? OnDropHover { get; init; }
    /// <summary>この payload を受け入れるか。null = すべて受け入れる。</summary>
    public Func<object, bool>? AcceptsDrop { get; init; }

    internal CursorKind EffectiveCursor => CursorFunc?.Invoke() ?? Cursor;
}

/// <summary>
/// Realize 中の登録 (UiNode/ヒット/フォーカス/スクロール/アニメーション/Effect) の所有権 —
/// widget 単位の階層スコープ。<see cref="Dispose"/> で配下の登録を一括除去し Effect を破棄する
/// (SetRoot のルート破棄と、部分 Realize (再実体化境界) の基盤)。
/// </summary>
public sealed class RealizeScope : IDisposable
{
    private readonly UiBuildContext _ctx;
    internal RealizeScope? Parent;
    /// <summary>このスコープを積んだ widget (dirty 伝播の親子解決用)。ルートスコープは null。</summary>
    internal Widget? Owner;
    /// <summary>破棄済みか。dirty 処理が「まだツリー上に居るか」の判定に使う。</summary>
    public bool IsDisposed { get; private set; }
    internal readonly List<RealizeScope> Children = new();
    internal readonly List<UiNode> Nodes = new();
    internal readonly List<HitTarget> Hits = new();
    internal readonly List<FocusTarget> Focusables = new();
    internal readonly List<ScrollTarget> Scrollables = new();
    internal readonly List<OverlayEntry> Overlays = new();
    internal readonly List<Func<float, bool>> Animations = new();
    internal readonly List<IDisposable> Effects = new();

    internal RealizeScope(UiBuildContext ctx) => _ctx = ctx;

    /// <summary>破棄して親スコープからも切り離す (部分 Realize/embed 差し替え用)。
    /// 親の一括 Dispose 再帰中は呼ばないこと (列挙破壊) — その場合は Dispose が呼ばれる。</summary>
    public void Release()
    {
        Dispose();
        Parent?.Children.Remove(this);
        Parent = null;
    }

    /// <summary>配下 (子スコープ再帰) の Effect を破棄し、入力登録とノードを除去する。</summary>
    public void Dispose()
    {
        IsDisposed = true;
        foreach (RealizeScope c in Children) c.Dispose();
        Children.Clear();
        foreach (IDisposable e in Effects) e.Dispose();
        Effects.Clear();
        foreach (HitTarget h in Hits)
        {
            _ctx.Hits.Remove(h);
            _ctx.HitScopes.Remove(h);
        }
        Hits.Clear();
        foreach (FocusTarget f in Focusables)
        {
            _ctx.Focusables.Remove(f);
            _ctx.FocusScopes.Remove(f);
        }
        Focusables.Clear();
        foreach (ScrollTarget s in Scrollables)
        {
            _ctx.Scrollables.Remove(s);
            _ctx.ScrollScopes.Remove(s);
        }
        Scrollables.Clear();
        foreach (OverlayEntry overlay in Overlays.ToArray()) _ctx.UnregisterOverlay(overlay);
        Overlays.Clear();
        foreach (Func<float, bool> animation in Animations)
        {
            _ctx.Animations.Remove(animation);
            _ctx.AnimationActivity.Remove(animation);
        }
        Animations.Clear();
        foreach (UiNode n in Nodes) _ctx.Canvas.Remove(n);
        Nodes.Clear();
    }
}

/// <summary>ウィジェットを保持型ツリーへ実体化する際の文脈。</summary>
public sealed class UiBuildContext
{
    public required RetainedCanvas Canvas { get; init; }
    public required VectorFont Font { get; init; }

    /// <summary>GPU専用widgetが明示的に要求するGPU 2D capability。CPU/Skia hostではnull。</summary>
    public GpuDeviceRasterizer2D? GpuRasterizer { get; init; }

    public GpuDeviceRasterizer2D RequireGpuRasterizer()
        => GpuRasterizer ?? throw new NotSupportedException(
            "This widget requires the GPU 2D rasterizer and cannot run with the selected CPU rasterizer.");

    /// <summary>この host に属する keyed raster surfaces の登録先。GPU composition host だけが設定する。</summary>
    public UiRendererState? RendererState { get; init; }

    /// <summary>この build を所有する UiHost (D&amp;D の <see cref="UiHost.BeginDrag"/> 等、
    /// host サービスへのアクセス用)。SetRoot が設定する。</summary>
    public UiHost? Host { get; internal set; }

    /// <summary>現在のヒットレイヤ (ambient、既定 0)。<see cref="AddHit"/> が刻印する。
    /// フローティングパネル等がサブツリーの実体化中だけ上げる (try/finally で戻す)。</summary>
    public int HitLayer { get; set; }

    /// <summary>ルートスコープ (この build の全登録の所有者)。UiHost が SetRoot 時に前世代を破棄する。</summary>
    public RealizeScope Root => _root ??= new RealizeScope(this);
    private RealizeScope? _root;
    private RealizeScope? _current;
    internal RealizeScope CurrentScope => _current ?? Root;

    /// <summary>widget の Realize 開始 (Widget.Realize テンプレートが呼ぶ)。</summary>
    internal RealizeScope PushScope()
    {
        var s = new RealizeScope(this) { Parent = CurrentScope };
        CurrentScope.Children.Add(s);
        _current = s;
        return s;
    }

    internal void PopScope(RealizeScope s)
        => _current = ReferenceEquals(s.Parent, Root) ? null : s.Parent;

    /// <summary>リアクティブ副作用を現在の Realize スコープに登録する — widget の Realize 内では
    /// <see cref="Reactive.Effect"/> でなくこちらを使うこと (スコープ破棄で購読解除される。
    /// 直接 Reactive.Effect を使うと SetRoot 後も購読が残りリークする)。</summary>
    public IDisposable Effect(Action run)
    {
        IDisposable e = Reactive.Effect(run);
        CurrentScope.Effects.Add(e);
        return e;
    }

    /// <summary>任意の IDisposable を現在のスコープに所有させる (スコープ破棄で Dispose される)。
    /// 動的に生成した embed widget 等のリソース寿命をツリー破棄に連動させるために使う。</summary>
    public void Own(IDisposable d) => CurrentScope.Effects.Add(d);

    /// <summary>このホストのテーマ signal (UiHost が所有)。コントロールは Realize でこれを閉包に
    /// キャプチャして Effect 内で読む — 静的 <see cref="UiTheme.Current"/> を直接購読しないこと
    /// (別スレッドの UI 島が同一 signal を共有しないための規約)。既定 = プロセス既定テーマ。</summary>
    public Signal<Theme> Theme { get; init; } = UiTheme.Current;

    /// <summary>この UI が最終的にラスタライズされる DPI スケール (論理 px × scale = 物理 px)。
    /// 自前バッファへ描く widget (SurfaceView 等) が物理解像度で確保するために参照する。
    /// スケール変更 (DPI 変更) 時は UiHost が再実体化するので Realize で読んでよい。</summary>
    public float RenderScale { get; init; } = 1f;

    /// <summary>ヒットテスト対象一覧 (入力で使用)。</summary>
    public List<HitTarget> Hits { get; } = new();
    internal Dictionary<HitTarget, RealizeScope> HitScopes { get; } = new();
    /// <summary>フォーカス対象一覧 (タブ順 = 登録順)。</summary>
    public List<FocusTarget> Focusables { get; } = new();
    internal Dictionary<FocusTarget, RealizeScope> FocusScopes { get; } = new();
    /// <summary>スクロール対象一覧。</summary>
    public List<ScrollTarget> Scrollables { get; } = new();
    internal Dictionary<ScrollTarget, RealizeScope> ScrollScopes { get; } = new();
    /// <summary>オーバーレイ (Dialog/Menu/Tooltip/Toast/Drawer)。最前面レイヤへ実体化される。</summary>
    public List<OverlayEntry> Overlays { get; } = new();
    /// <summary>アニメーション (step(dt)→true で完了・除去)。直接追加した登録は常に active とみなす。</summary>
    public List<Func<float, bool>> Animations { get; } = new();
    internal Dictionary<Func<float, bool>, Func<bool>> AnimationActivity { get; } = new();

    /// <summary>再実体化待ちの dirty widget (<see cref="Widget.MarkNeedsRealize"/> の集積先)。
    /// UiHost が Tick 頭でまとめて処理する (バッチ = 1 フレーム内の多重変更を 1 回の部分 Realize に)。</summary>
    internal readonly List<Widget> Dirty = new();

    /// <summary>widget を dirty 登録する (通常は <see cref="Widget.MarkNeedsRealize"/> 経由)。重複は無視。</summary>
    public void MarkDirty(Widget w)
    {
        if (!Dirty.Contains(w)) Dirty.Add(w);
    }

    /// <summary>オーバーレイを現在の実体化スコープに登録する。所有スコープの破棄時に自動解除される。</summary>
    public void RegisterOverlay(OverlayEntry e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.OwnerScope is not null) throw new InvalidOperationException("OverlayEntry is already registered.");
        e.OwnerScope = CurrentScope;
        Overlays.Add(e);
        CurrentScope.Overlays.Add(e);
        Host?.OverlayRegistered(e);
    }

    /// <summary>動的オーバーレイを解除する。未登録は no-op。</summary>
    public void UnregisterOverlay(OverlayEntry e)
    {
        if (e.OwnerScope is null) return;
        Host?.OverlayUnregistered(e);
        Overlays.Remove(e);
        e.OwnerScope.Overlays.Remove(e);
        e.OwnerScope = null;
    }

    /// <summary>入力コールバック中だけ登録先スコープを発火元へ戻す。</summary>
    internal void RunWithScope(RealizeScope? scope, Action action)
    {
        RealizeScope? previous = _current;
        _current = scope is { IsDisposed: false } ? scope : null;
        try { action(); }
        finally { _current = previous; }
    }

    /// <summary>指定所有者の子スコープ内で動的な実体化を行う。</summary>
    internal RealizeScope RealizeOwned(RealizeScope owner, Action realize)
    {
        RealizeScope? previous = _current;
        var runtime = new RealizeScope(this) { Parent = owner };
        owner.Children.Add(runtime);
        _current = runtime;
        try { realize(); }
        catch
        {
            runtime.Release();
            throw;
        }
        finally { _current = previous; }
        return runtime;
    }

    /// <summary>継続的に active なアニメーションを登録する (step(dt)→true で完了)。</summary>
    public void AddAnimation(Func<float, bool> step) => AddAnimation(step, static () => true);

    /// <summary>
    /// 必要な間だけ frame tick を要求するアニメーションを登録する。
    /// <paramref name="isActive"/> が false の間は step を呼ばず、静止 UI の render opportunity も要求しない。
    /// </summary>
    public void AddAnimation(Func<float, bool> step, Func<bool> isActive)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(isActive);
        Animations.Add(step);
        AnimationActivity[step] = isActive;
        CurrentScope.Animations.Add(step);
    }

    /// <summary>クリック/hover/ドラッグを受けるコントロールが自身のノードとローカル矩形、ハンドラを登録する。
    /// 判定はノードの現在の transform に追従する (rect はノードのローカル座標)。</summary>
    public HitTarget AddHit(UiNode node, Rect rect, Action? onClick = null, Action<bool>? onHover = null, FocusTarget? focus = null,
        Action<PointerEvent>? onClickPos = null,
        Action<PointerEvent>? onDragStart = null, Action<PointerEvent>? onDrag = null, Action<PointerEvent>? onDragEnd = null,
        Action<PointerEvent>? onMovePos = null,
        CursorKind cursor = CursorKind.Arrow, Func<CursorKind>? cursorFunc = null,
        Action<PointerEvent>? onContext = null,
        Action<object, PointerEvent>? onDrop = null, Action<object, PointerEvent>? onDropMove = null,
        Action<bool>? onDropHover = null, Func<object, bool>? acceptsDrop = null)
    {
        var h = new HitTarget
        {
            Node = node,
            Rect = rect,
            OnClick = onClick,
            OnHover = onHover,
            Focus = focus,
            OnClickPos = onClickPos,
            OnDragStart = onDragStart,
            OnDrag = onDrag,
            OnDragEnd = onDragEnd,
            OnMovePos = onMovePos,
            Cursor = cursor,
            CursorFunc = cursorFunc,
            OnContext = onContext,
            OnDrop = onDrop,
            OnDropMove = onDropMove,
            OnDropHover = onDropHover,
            AcceptsDrop = acceptsDrop,
            Layer = HitLayer,
        };
        Hits.Add(h);
        CurrentScope.Hits.Add(h);
        HitScopes[h] = CurrentScope;
        return h;
    }

    /// <summary>フォーカス対象を登録する (戻り値で OnFocus/OnKey を設定)。</summary>
    public FocusTarget AddFocusable(Action<bool>? onFocus = null, Func<KeyEvent, bool>? onKey = null,
        Action<string>? onText = null, Action<string>? onCompose = null, Action<string>? onCommit = null,
        Action<ImeComposition>? onComposeEx = null, ITextInput? textInput = null)
    {
        var f = new FocusTarget
        {
            OnFocus = onFocus,
            OnKey = onKey,
            OnText = onText,
            OnCompose = onCompose,
            OnCommit = onCommit,
            OnComposeEx = onComposeEx,
            TextInput = textInput,
        };
        Focusables.Add(f);
        CurrentScope.Focusables.Add(f);
        FocusScopes[f] = CurrentScope;
        return f;
    }

    /// <summary>**既存の** FocusTarget を現在のスコープへ再登録する — 部分 Realize (再実体化) を
    /// またいでフォーカスを保存するために使う。widget が FocusTarget を初回 Realize で作ってフィールドに
    /// 保持し、以後の Realize ではこれで同じインスタンスを登録し直すと、UiHost の参照保持フォーカスが
    /// そのまま生き残る (再実体化のたびに新規作成するとフォーカスが失われる)。</summary>
    public FocusTarget AddFocusable(FocusTarget f)
    {
        Focusables.Add(f);
        CurrentScope.Focusables.Add(f);
        FocusScopes[f] = CurrentScope;
        return f;
    }

    /// <summary>スクロール対象を登録する (rect はノードのローカル座標、判定は transform 追従)。
    /// <paramref name="onScrollPos"/> はローカル座標付き (SurfaceView 等の転送用、指定時はこちらが優先)。</summary>
    public void AddScroll(UiNode node, Rect rect, Action<float>? onScroll = null,
        Action<float, float, float>? onScrollPos = null)
    {
        var s = new ScrollTarget { Node = node, Rect = rect, OnScroll = onScroll, OnScrollPos = onScrollPos };
        Scrollables.Add(s);
        CurrentScope.Scrollables.Add(s);
        ScrollScopes[s] = CurrentScope;
    }
}

/// <summary>
/// ウィジェット基底。Flutter 風の単一パスレイアウト (constraints 下り → size 上り → 親が Offset を書く)
/// と、保持型ツリーへの実体化 (<see cref="Realize"/>) を持つ。**別アセンブリで継承して独自コントロールを
/// 追加できる** (Realize/PerformLayout を override、AddChildWidget でコンテナ化)。
/// </summary>
public abstract partial class Widget
{
    private Dictionary<object, object?>? _attached;
    private Dictionary<string, object>? _setterWraps;

    // ---- 状態 signals (全 widget 共通)。Realize が入力配線し、Bindable の状態レイヤ判定と
    //      テーマ解決 Effect が読む (signal なので変化で部分更新)。----
    public readonly Signal<bool> Hovered = new(false);
    public readonly Signal<bool> Pressed = new(false);
    public readonly Signal<bool> Focused = new(false);
    public bool Enabled { get; set; } = true;

    /// <summary>状態が現在アクティブか。<see cref="Bindable{T}"/> の状態レイヤ解決が呼ぶ。
    /// Checked/Selected 等の独自状態を持つ派生は override して signal を読む。</summary>
    public virtual bool IsStateActive(WidgetState state) => state switch
    {
        WidgetState.Default => true,
        WidgetState.Hover => Hovered.Value,
        WidgetState.Pressed => Pressed.Value,
        WidgetState.Focused => Focused.Value,
        WidgetState.Disabled => !Enabled,
        _ => false,
    };

    /// <summary>名前で [UiParam] Bindable プロパティへ書き込む (Tailwind utility の適用先)。
    /// ソースジェネレーターが switch + <see cref="PropWriter.Set{TField,T}"/> の override を焼き込む。
    /// 名前または型が合わなければ false。state=Default は基底差し替え、それ以外は状態レイヤ追加。</summary>
    public virtual bool SetProp<T>(string name, WidgetState state, Bindable<T> value) => false;

    /// <summary>名前で引数なしの [UiEvent] を発火する (テスト/リモート駆動用)。
    /// ソースジェネレーターが switch の override を焼き込む。該当なし = false。</summary>
    public virtual bool InvokeEvent(string name) => false;

    /// <summary>プロパティ setter のラッパを登録する (Transition 補間の差し替え点)。</summary>
    public void SetSetterWrap<T>(string prop, Func<Action<T>, Action<T>> factory)
        => (_setterWraps ??= new())[prop] = factory;

    /// <summary>プロパティ名毎の登録が無いときの一括フォールバック (AS-M3 —
    /// <see cref="TransitionWiring"/> が状態遷移の自動配線に使う)。Realize 毎に上書きされる。</summary>
    public void SetSetterWrapFallback(ISetterWrapProvider? provider) => _setterWrapFallback = provider;
    private ISetterWrapProvider? _setterWrapFallback;

    /// <summary>登録済みラッパがあれば raw setter を包んで返す (なければフォールバック → そのまま)。
    /// widget の Realize が描画適用 setter を作るときに通す。</summary>
    public Action<T> WrapSetter<T>(string prop, Action<T> raw)
        => _setterWraps != null && _setterWraps.TryGetValue(prop, out object? f)
           && f is Func<Action<T>, Action<T>> tf ? tf(raw)
           : _setterWrapFallback?.Wrap(prop, raw) ?? raw;

    // ---- 全 widget 共通のレイアウトプロパティ。[UiParam] 付き Bindable フィールドなので
    //      ソース生成ファクトリの「共通引数」と DevTools 編集対象に自動で含まれる。
    //      値/Signal/Func を束縛できるが、レイアウトは単一パスで PerformLayout 時に読むだけ
    //      (リアクティブ再レイアウトは将来の Invalidate フックで対応)。
    //      フィールドは readonly — 差し替え不可、書き込みは SetBase (状態レイヤ/override 維持)。
    [UiParam] private readonly Bindable<Thickness> _margin = new();
    /// <summary>未設定 (default) は <see cref="Align.Start"/> (enum 先頭 = 0)。</summary>
    [UiParam] private readonly Bindable<Align> _hAlign = new();
    [UiParam] private readonly Bindable<Align> _vAlign = new();
    /// <summary>明示的に固定したい場合の幅 (未設定 = 自然サイズ。旧 float? null 相当)。全 widget で共通。</summary>
    [UiParam] private readonly Bindable<Length> _width = new();
    /// <summary>明示的に固定したい場合の高さ (未設定 = 自然サイズ)。全 widget で共通。</summary>
    [UiParam] private readonly Bindable<Length> _height = new();

    // ---- transform 成分 (TF): 全 widget 共通の表示系パラメータ。**行列を直接アニメしない**ための
    //      分解表現 (CSS の translate/rotate/scale 独立プロパティと同じ思想) — プロパティ毎に
    //      独立した状態値/トランジション (X と Y で別カーブ等) を設定できる。
    //      適用はコントロールが root ノードに <see cref="WireTransform"/> を呼ぶ (中心基準で
    //      Translate → Rotate → Scale の順に合成、レイアウトには影響しない)。
    /// <summary>X 方向の平行移動 (px)。未設定 = 0。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<float> _translateX = new();
    /// <summary>Y 方向の平行移動 (px)。未設定 = 0。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<float> _translateY = new();
    /// <summary>X 方向スケール (中心基準)。既定 = 1。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<float> _scaleX = 1f;
    /// <summary>Y 方向スケール (中心基準)。既定 = 1。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<float> _scaleY = 1f;
    /// <summary>回転 (ラジアン、中心基準)。未設定 = 0。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<float> _rotate = new();
    public Size Size { get; protected set; }
    public Point Offset { get; set; }              // 親が書く (ローカル)
    public Point WorldPos { get; protected set; }  // 実体化時に確定 (ヒット登録用)

    // ---- Width/Height (単位付き Length) の解決ヘルパ ----
    /// <summary>Width を論理 px へ解決 (% は c.MaxW 基準)。未指定は <paramref name="fallback"/>。</summary>
    public float ResolveW(in Constraints c, LayoutContext ctx, float fallback = 0)
        => Width.Get().Resolve(c.MaxW, ctx, fallback);
    /// <summary>Height を論理 px へ解決 (% は c.MaxH 基準)。未指定は <paramref name="fallback"/>。</summary>
    public float ResolveH(in Constraints c, LayoutContext ctx, float fallback = 0)
        => Height.Get().Resolve(c.MaxH, ctx, fallback);
    /// <summary>制約が手元にない文脈 (intrinsic 計測等) 用 — % は解決できず fallback。</summary>
    public float ResolveWIntrinsic(LayoutContext ctx, float fallback = 0)
        => Width.Get().Resolve(float.PositiveInfinity, ctx, fallback);

    /// <summary>root ノードの定型生成 (TF の共通処理): AddChild + WorldPos 記録 + transform 成分の配線。
    /// 従来の 3 行イディオム (`AddChild` → `node.Transform = Translate(Offset)` → `SetWorldPos`) の
    /// 置き換え — これを使うだけで TranslateX/ScaleX/Rotate 等の共通パラメータが効く。
    /// world 座標が要る場合は呼出し後に <see cref="WorldPos"/> を読む。</summary>
    protected UiNode CreateRoot(UiBuildContext ctx, UiNode parent, Point worldOrigin)
        => CreateRoot(ctx, parent, worldOrigin, out _);

    /// <summary>handle 付き版 — コントロール固有の一様スケール (Button.Scale 等) を合成する場合。</summary>
    protected UiNode CreateRoot(UiBuildContext ctx, UiNode parent, Point worldOrigin, out TransformHandle transform)
    {
        UiNode node = ctx.Canvas.AddChild(parent);
        SetWorldPos(worldOrigin + Offset);
        transform = WireTransform(ctx, node);
        return node;
    }

    /// <summary>root ノードへ transform 成分 (Offset + TranslateX/Y・ScaleX/Y・Rotate、中心基準) を
    /// 配線する — `node.Transform = Translate(Offset)` の置き換え。setter は <see cref="WrapSetter{T}"/>
    /// を通るので状態遷移のトランジション (fluent Transition 系) が効く。
    /// 戻り値の handle でコントロール固有の一様スケール (Button.Scale 等) を合成できる。</summary>
    public TransformHandle WireTransform(UiBuildContext ctx, UiNode node)
    {
        var h = new TransformHandle();
        float tx = 0, ty = 0, sx = 1, sy = 1, rot = 0;
        void Apply()
        {
            float esx = sx * h.ExtraScale, esy = sy * h.ExtraScale;
            if (tx == 0 && ty == 0 && esx == 1 && esy == 1 && rot == 0)
            {
                node.Transform = Affine2D.Translate(Offset.X, Offset.Y);
                return;
            }
            float cx = Size.Width * 0.5f, cy = Size.Height * 0.5f;
            node.Transform = Affine2D.Mul(
                Affine2D.Translate(Offset.X + tx + cx, Offset.Y + ty + cy),
                Affine2D.Mul(Affine2D.Rotate(rot),
                    Affine2D.Mul(Affine2D.Scale(esx, esy), Affine2D.Translate(-cx, -cy))));
        }
        h.Recompose = Apply;
        Action<float> txSet = WrapSetter<float>("TranslateX", v => { tx = v; Apply(); });
        Action<float> tySet = WrapSetter<float>("TranslateY", v => { ty = v; Apply(); });
        Action<float> sxSet = WrapSetter<float>("ScaleX", v => { sx = v; Apply(); });
        Action<float> sySet = WrapSetter<float>("ScaleY", v => { sy = v; Apply(); });
        Action<float> rotSet = WrapSetter<float>("Rotate", v => { rot = v; Apply(); });
        ctx.Effect(() =>
        {
            txSet(TranslateX.Get());
            tySet(TranslateY.Get());
            sxSet(ScaleX.Get());
            sySet(ScaleY.Get());
            rotSet(Rotate.Get());
        });
        Apply();
        return h;
    }

    /// <summary>直近の Layout に使われた制約 (部分 Realize が同じ制約で再レイアウトするための記録)。</summary>
    public Constraints LastConstraints { get; private set; }

    /// <summary>親が 1 回だけ呼ぶ。サイズを返し、同じ呼び出し内で子の Offset を書く。</summary>
    public Size Layout(Constraints c, LayoutContext ctx, bool parentUsesSize = false)
    {
        LastConstraints = c;
        PerformLayout(c, ctx);
        return Size;
    }

    /// <summary>自分のサイズを決め、子を Layout して Offset を書く (派生で実装)。</summary>
    protected abstract void PerformLayout(Constraints c, LayoutContext ctx);

    /// <summary>auto 列用の自然幅 (opt-in)。既定 0。Text/Button 等が override。</summary>
    public virtual float MaxIntrinsicWidth(float height, LayoutContext ctx) => 0;

    /// <summary>この widget の直近の Realize が所有する登録 (スコープ)。部分 Realize/破棄の単位。</summary>
    public RealizeScope? Scope { get; private set; }
    /// <summary>直近の Realize の親ノード/ワールド原点 (部分 Realize が同じ場所へ再実体化するための記録)。</summary>
    public UiNode? RealizedParent { get; private set; }
    public Point RealizedOrigin { get; private set; }

    /// <summary>直近の Realize での親 widget (dirty 伝播のバブリング経路)。入れ子 Realize では
    /// テンプレートがスコープ階層から自動設定する。エディタの embed のように **Realize の入れ子外**
    /// (イベントハンドラ等) から子を実体化する手動ホストは、実体化後に明示的に設定すること。</summary>
    public Widget? ParentWidget { get; set; }

    private UiBuildContext? _realizedCtx;

    /// <summary>「自分の表示が古くなった」と宣言する — UiHost が次の Tick 頭でまとめて部分 Realize する。
    /// サイズが変わらなければこの widget 自身が境界 (その場で再実体化)、変わるなら親方向へバブリングし
    /// <see cref="OnChildNeedsRealize"/> が吸収、誰も吸収しなければ SetRoot (全再構築) に縮退する。
    /// 同一インスタンスのまま Realize し直すので widget のフィールド (選択/スクロール等) は生き残る。
    /// 未実体化なら no-op。UI スレッドから呼ぶこと。</summary>
    public void MarkNeedsRealize() => _realizedCtx?.MarkDirty(this);

    /// <summary>子 widget の再実体化要求 (サイズが変わる変更) をこの widget が吸収できるなら
    /// 処理して true を返す。手動でノード/子配置を管理するコンテナ (エディタの embed 等) が override し、
    /// 子を**同一インスタンスのまま**再レイアウト + 再実体化 + 周辺の再配置を行う。
    /// false (既定) はさらに親へバブリングし、ルートまで届くと SetRoot 全再構築になる。</summary>
    protected internal virtual bool OnChildNeedsRealize(Widget child) => false;

    /// <summary>この widget 以下のヒット登録に使うレイヤ (null = 親のまま)。フローティング
    /// パネル/メニュー等「浅いが最前面」の UI が設定する — Realize テンプレートがサブツリーの
    /// 間だけ <see cref="UiBuildContext.HitLayer"/> を差し替えるので、**部分再実体化でも保たれる**。</summary>
    public int? HitLayer { get; set; }

    /// <summary>保持型ツリーへ実体化する (レイアウト後)。スコープを積んで <see cref="RealizeCore"/> を
    /// 呼ぶテンプレート — 実体化中の登録 (ノード/入力/Effect) はこの widget のスコープが所有する。</summary>
    public void Realize(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        RealizeScope scope = ctx.PushScope();
        scope.Owner = this;
        // 入れ子 Realize なら親 widget を自動記録。トップレベル (ルート/手動ホスト経由) は
        // 明示設定を尊重してそのまま (ルート widget は null のまま)。
        if (scope.Parent?.Owner is { } pw) ParentWidget = pw;
        Scope = scope;
        RealizedParent = parent;
        RealizedOrigin = worldOrigin;
        _realizedCtx = ctx;
        // 状態遷移の自動配線 (AS-M3): P.Transition.* が添付した TransitionTable があれば、
        // WrapSetter を通る全プロパティを支配状態の from/to で補間する
        if (GetAttached<Animation.TransitionTable>(TransitionWiring.TableKey) is { } transitionTable)
            SetSetterWrapFallback(new TransitionWiring.Provider(this, ctx, transitionTable));
        int before = parent.Children.Count;
        int prevLayer = ctx.HitLayer;
        if (HitLayer is { } hl) ctx.HitLayer = hl;
        try { RealizeCore(ctx, parent, worldOrigin); }
        finally
        {
            ctx.HitLayer = prevLayer;
            for (int i = before; i < parent.Children.Count; i++) scope.Nodes.Add(parent.Children[i]);
            ctx.PopScope(scope);
        }
    }

    /// <summary>実体化の本体 (派生で実装)。UiNode を生成し、signal は <see cref="UiBuildContext.Effect"/> で
    /// 束縛する (スコープ破棄で購読解除されるように)。</summary>
    protected abstract void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin);

    // ---- デバッグ introspection (DevTools のツリー可視化用。WorldPos/Size が矩形) ----
    /// <summary>デバッグ表示の型名。既定はクラス名。</summary>
    public virtual string DebugType => GetType().Name;
    /// <summary>デバッグ表示の補助情報 (Text=本文, Button=ラベル 等)。既定なし。</summary>
    public virtual string? DebugDetail => null;
    /// <summary>デバッグ用の子ウィジェット列挙 (コンテナのみ override)。既定は葉 (空)。</summary>
    public virtual IEnumerable<Widget> DebugChildren() => [];

    /// <summary>DevTools で編集可能な property セット。
    /// <c>[UiParam]</c> 付き <see cref="Bindable{T}"/> フィールドを持つ partial クラスには
    /// ソースジェネレーター (Luxel.UI.Generators) が直接アクセスの override を焼き込む。
    /// 既定 (焼き込み対象外) は空。手書き override も可 (テストの ProbeWidget 等)。</summary>
    public virtual IEnumerable<DebugProp> DebugProps() => [];

    /// <summary>DevTools から prop 書き換え要求を受信。生成コードが name の switch で
    /// <see cref="WidgetDebugCodec.Write{T}"/> 系へ直接分岐する override を焼き込む。既定は no-op。</summary>
    public virtual void SetDebugProp(string name, string type, System.Text.Json.JsonElement value) { }

    /// <summary>WorldPos を確定する (派生コントロールの Realize から呼ぶ)。</summary>
    protected void SetWorldPos(Point p) => WorldPos = p;

    /// <summary>再実体化せず位置だけずらした親 (エディタの埋め込みホスト等) が、この widget と全子孫の
    /// <see cref="WorldPos"/> を <paramref name="delta"/> だけ平行移動する。描画はノード transform で正しく
    /// 動くが、WorldPos は実体化時に焼かれるため programmatic ヒット (d.Click(widget)) 用に同期する。</summary>
    public void ShiftWorldPos(Point delta)
    {
        WorldPos = new Point(WorldPos.X + delta.X, WorldPos.Y + delta.Y);
        foreach (Widget c in DebugChildren()) c.ShiftWorldPos(delta);
    }

    /// <summary>添付プロパティを記録する。</summary>
    public void SetAttached(Attached a)
    {
        _attached ??= new();
        _attached[a.Key] = a.Value;
    }

    /// <summary>添付プロパティを型付きで取得する。未設定なら fallback。</summary>
    public T GetAttached<T>(string key, T fallback = default!)
        => _attached != null && _attached.TryGetValue(key, out object? v) && v is T t ? t : fallback;

    /// <summary>型付き添付プロパティを設定する。validation は設定時に実行される。</summary>
    public void SetAttached<T>(AttachedProperty<T> property, T value)
    {
        ArgumentNullException.ThrowIfNull(property);
        property.Validate(value);
        _attached ??= new();
        _attached[property] = value;
    }

    /// <summary>型付き添付プロパティを取得する。未設定なら property の既定値。</summary>
    public T GetAttached<T>(AttachedProperty<T> property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return _attached != null && _attached.TryGetValue(property, out object? value) && value is T typed
            ? typed
            : property.DefaultValue;
    }
}
