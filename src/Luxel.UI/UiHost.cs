using System.Diagnostics;
using Luxel.Animation;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.Diagnostics;

namespace Luxel.UI;

/// <summary>
/// ウィジェットツリーを保持型キャンバスに結びつけるホスト。
/// SetRoot でレイアウト(単一パス)→実体化(UiNode 生成 + signal 束縛)。
/// 入力 (Click/PointerMove) は前面優先でヒットテストし onClick/hover を発火する。
/// </summary>
public sealed class UiHost : IDisposable
{
    private readonly RetainedCanvas _canvas;
    private readonly VectorFont _font;
    private readonly LayoutContext _layoutCtx;
    private float _width, _height;

    private Widget? _root;
    private UiBuildContext? _build;
    private HitTarget? _hover;
    private HitTarget? _captured;   // ドラッグ捕獲中の対象 (PointerDown〜PointerUp)
    // フォーカスは参照で保持する — Focusables は部分 Realize (embed 差し替え等) で増減するため、
    // index 保持だと除去でズレて誤配送する。Current() が「まだ登録されているか」を検証する。
    private FocusTarget? _focusTarget;

    private readonly UiSetRequestSubscriber _uiSetSub;

    /// <summary>DevTools/リモート用の識別名 (<see cref="UiRegistry.Register"/> が設定)。
    /// ui.set リクエストの "ui" フィールドと照合される。null は「単一 host 前提」。</summary>
    public string? DebugName { get; set; }

    /// <summary>このホストのテーマ (所有 signal)。省略時はプロセス既定 <see cref="UiTheme.Current"/> を共有。
    /// 別スレッドの UI 島は必ず自前の signal を渡すこと (signal は所有スレッドのみが触る規約)。</summary>
    public Signal<Theme> Theme { get; }

    public UiHost(RetainedCanvas canvas, VectorFont font, float width, float height, Signal<Theme>? theme = null)
    {
        _canvas = canvas;
        _font = font;
        _width = width;
        _height = height;
        Theme = theme ?? UiTheme.Current;
        _layoutCtx = new LayoutContext { Font = font, Theme = Theme.Peek() };
        _uiSetSub = new UiSetRequestSubscriber(this);
    }

    /// <summary>DevTools からの ui.set リクエスト購読を停止し、実体化の登録 (Effect 等) を破棄する。</summary>
    public void Dispose()
    {
        _uiSetSub.Dispose();
        _build?.Root.Dispose();
    }

    /// <summary>path (dotted-index 例 "0.1.2") で tree を辿り Widget を返す。</summary>
    private Widget? FindByPath(string path)
    {
        if (_root is null || string.IsNullOrEmpty(path)) return null;
        string[] parts = path.Split('.');
        // Path は root=0 起点。最初の要素は 0 でルート、以降が子 index。
        if (parts.Length == 0 || parts[0] != "0") return null;
        Widget cur = _root;
        for (int i = 1; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out int idx)) return null;
            int cursor = 0;
            Widget? next = null;
            foreach (Widget c in cur.DebugChildren())
                if (cursor++ == idx) { next = c; break; }
            if (next is null) return null;
            cur = next;
        }
        return cur;
    }

    /// <summary>ui.set リクエストを受け取り、パスから widget を探して <see cref="Widget.SetDebugProp"/> を呼ぶ。
    /// <para>書込成功後は再実体化する: <see cref="Bindable{T}.SetOverride"/> の override Signal は遅延生成のため、
    /// 実体化済みの Effect には依存登録されておらず、そのままでは画面に反映されない (レイアウト系 prop も
    /// 単一パスのため再レイアウトが必要)。再実体化後は Effect が override signal を読むので、同じ prop への
    /// 以降の編集は部分更新で反映される。</para></summary>
    internal void HandleUiSetRequest(System.Text.Json.JsonElement el)
    {
        // 全 UiHost が購読しているため、"ui" 指定付きリクエストは名前が一致した host だけが適用する
        // ("ui" 省略は従来どおり全 host に配送 = 単一 host アプリの互換)。
        if (el.ValueKind == System.Text.Json.JsonValueKind.Object
            && el.TryGetProperty("ui", out System.Text.Json.JsonElement ui)
            && ui.ValueKind == System.Text.Json.JsonValueKind.String
            && ui.GetString() != DebugName)
            return;
        if (DispatchUiSet(_root, el) && _root is not null) SetRoot(_root);
    }

    /// <summary>root から tree を辿って ui.set を配送する共通ロジック (test でも使用可能)。</summary>
    public static bool DispatchUiSet(Widget? root, System.Text.Json.JsonElement el)
    {
        if (root is null) return false;
        try
        {
            string path = el.GetProperty("path").GetString() ?? "";
            string name = el.GetProperty("name").GetString() ?? "";
            string type = el.GetProperty("propType").GetString() ?? "";
            var val = el.GetProperty("value");
            Widget? w = FindByPathStatic(root, path);
            if (w is null) return false;
            w.SetDebugProp(name, type, val);
            return true;
        }
        catch { return false; }
    }

    internal static Widget? FindByPathStatic(Widget root, string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        string[] parts = path.Split('.');
        if (parts.Length == 0 || parts[0] != "0") return null;
        Widget cur = root;
        for (int i = 1; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out int idx)) return null;
            int cursor = 0;
            Widget? next = null;
            foreach (Widget c in cur.DebugChildren())
                if (cursor++ == idx) { next = c; break; }
            if (next is null) return null;
            cur = next;
        }
        return cur;
    }

    private sealed class UiSetRequestSubscriber : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly UiHost _host;
        private readonly List<IDisposable> _subs = new();
        private IDisposable? _all;

        public UiSetRequestSubscriber(UiHost host)
        {
            _host = host;
            _all = DiagnosticListener.AllListeners.Subscribe(this);
        }

        void IObserver<DiagnosticListener>.OnNext(DiagnosticListener dl)
        {
            if (dl.Name == EngineDiagnostics.SourceName)
                _subs.Add(dl.Subscribe(this, (Predicate<string>)(k => k == "Luxel.UiSetRequest")));
        }
        void IObserver<DiagnosticListener>.OnError(Exception error) { }
        void IObserver<DiagnosticListener>.OnCompleted() { }

        void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> kv)
        {
            if (kv.Key == "Luxel.UiSetRequest" && kv.Value is System.Text.Json.JsonElement el)
                _host.HandleUiSetRequest(el);
        }
        void IObserver<KeyValuePair<string, object?>>.OnError(Exception error) { }
        void IObserver<KeyValuePair<string, object?>>.OnCompleted() { }

        public void Dispose()
        {
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
            _all?.Dispose(); _all = null;
        }
    }

    public float Width => _width;
    public float Height => _height;
    /// <summary>いずれかの widget が focus を持っているか (Tab で選択された TextField 等)。
    /// Sample 側で gameplay input context の suspend 判定に使う。</summary>
    public bool HasFocus => Current() is not null;

    private float _renderScale = 1f;

    /// <summary>この UI がラスタライズされる DPI スケール (ホスト側が設定)。変更時は再実体化して
    /// SurfaceView 等の物理解像度バッファを作り直す。</summary>
    public float RenderScale
    {
        get => _renderScale;
        set
        {
            if (value == _renderScale || value <= 0) return;
            _renderScale = value;
            if (_root != null) SetRoot(_root);
        }
    }

    /// <summary>キャンバスサイズを変更し、現在のルートを再レイアウトする (ウィンドウリサイズ用)。</summary>
    public void Resize(float width, float height)
    {
        if (width < 1 || height < 1) return;
        if (width == _width && height == _height) return;
        _width = width; _height = height;
        EmitInput("resize", $"{width:0}x{height:0}");
        if (_root != null) SetRoot(_root);   // 再レイアウト + 再構築
    }

    /// <summary>widget を同じ場所へ部分再実体化する (再実体化境界)。旧スコープ (ノード/入力/Effect) を
    /// 破棄し、直近の制約で再レイアウト → 同じ親ノード/原点へ Realize し直す。
    /// **サイズが親レイアウトに影響しない (tight constraints 等) widget にだけ使うこと** —
    /// サイズが変わる変更は親の再レイアウトが必要 (SetRoot へ)。フォーカスは可能なら維持する。</summary>
    public bool ReRealize(Widget w)
    {
        if (_build is null || w.Scope is null || w.RealizedParent is null) return false;
        w.Scope.Release();
        _layoutCtx.Theme = Theme.Peek();
        _layoutCtx.ViewportW = _width;
        _layoutCtx.ViewportH = _height;
        w.Layout(w.LastConstraints, _layoutCtx);
        w.Realize(_build, w.RealizedParent, w.RealizedOrigin);
        // フォーカスは参照保持 — 破棄されていれば Current() が null を返す (自動解除)
        EmitTree();
        return true;
    }

    /// <summary>ルートを設定し、レイアウト + 保持型ツリーへ実体化する (構造の宣言的構築)。</summary>
    public void SetRoot(Widget root)
    {
        _root = root;
        // フォーカスは参照のまま保持する — FocusTarget を再利用するコントロール (TextField/TextArea/
        // TableBlock) は再実体化で同じインスタンスを再登録するので、SetRoot を跨いでフォーカスが
        // 生き残る (CompositeControl.Rebuild が SetRoot に縮退するケース等)。再登録されなければ
        // Current() の Contains 検証が null を返す (自動解除) ので無効参照は残らない。
        // 前世代の登録 (Effect/入力/ノード) をスコープごと破棄してから作り直す —
        // Effect を破棄しないと購読が世代分積もりリークする (RealizeScope の存在理由)。
        _build?.Root.Dispose();
        foreach (UiNode c in _canvas.Root.Children.ToArray()) _canvas.Remove(c);

        _layoutCtx.Theme = Theme.Peek();   // レイアウトの寸法トークンを最新テーマに (購読なし)
        _layoutCtx.ViewportW = _width;     // Length の vw/vh 基準 (論理サイズ)
        _layoutCtx.ViewportH = _height;
        root.Layout(Constraints.Tight(new Size(_width, _height)), _layoutCtx);
        root.Offset = new Point(0, 0);

        _build = new UiBuildContext { Canvas = _canvas, Font = _font, Theme = Theme, RenderScale = _renderScale, Host = this };
        root.Realize(_build, _canvas.Root, new Point(0, 0));
        RealizeOverlays();
        EmitTree();
    }

    // ---- デバッグ introspection (DevTools 用) ----

    /// <summary>現在の UI ツリーの snapshot を返す (型/矩形/詳細の階層)。未設定なら null。</summary>
    public DebugNode? DebugSnapshot() => _root == null ? null : BuildDebug(_root);

    private static DebugNode BuildDebug(Widget w)
    {
        var children = new List<DebugNode>();
        foreach (Widget c in w.DebugChildren()) children.Add(BuildDebug(c));
        DebugProp[]? props = null;
        var propList = w.DebugProps();
        if (propList is DebugProp[] arr) props = arr.Length > 0 ? arr : null;
        else
        {
            var list = new List<DebugProp>();
            foreach (var p in propList) list.Add(p);
            if (list.Count > 0) props = list.ToArray();
        }
        return new DebugNode(w.DebugType, w.WorldPos.X, w.WorldPos.Y, w.Size.Width, w.Size.Height, 0,
            w.DebugDetail, children.ToArray(), props);
    }

    /// <summary>UI ツリー snapshot を診断イベントとして発行する (購読者が居る時のみ構築)。</summary>
    public void EmitTree()
    {
        if (!EngineDiagnostics.IsEnabled(EngineDiagnostics.Tree)) return;
        DebugNode? snap = DebugSnapshot();
        if (snap != null) EngineDiagnostics.Emit(EngineDiagnostics.Tree, snap);
    }

    private static void EmitInput(string op, string info)
        => EngineDiagnostics.Emit(EngineDiagnostics.Input, new DiagInput(op, info));

    /// <summary>(x,y) でクリック。モーダルは背面をブロック、外側クリックはディスミス。</summary>
    public bool Click(float x, float y)
    {
        EmitInput("click", $"{x:0},{y:0}");
        if (_build == null) return false;

        OverlayEntry? modal = null;
        for (int i = _build.Overlays.Count - 1; i >= 0; i--)
            if (_build.Overlays[i].Open.Value && _build.Overlays[i].Modal) { modal = _build.Overlays[i]; break; }

        if (TryPick(x, y, null, out HitTarget t, out float lx, out float ly)
            && (modal == null || modal.ContentRect.Contains(x, y)))          // モーダルが背面をブロック
        {
            if (t.Focus != null) FocusTo(t.Focus);                           // クリックでフォーカス
            Guard(t.OnClickPos is null ? null : () => t.OnClickPos!(new PointerEvent(lx, ly, x, y)), "Click");
            Guard(t.OnClick, "Click");
            return true;
        }

        // ヒットなし / モーダルでブロック → 外側ディスミス
        for (int i = _build.Overlays.Count - 1; i >= 0; i--)
        {
            OverlayEntry o = _build.Overlays[i];
            if (o.Open.Value && o.DismissOnOutside && !o.ContentRect.Contains(x, y)) { o.Open.Value = false; return true; }
        }
        return false;
    }

    /// <summary>ポインタ押下。最前面のドラッグ対応ヒットがあれば捕獲して true
    /// (以後の <see cref="PointerMove"/> は矩形外でも OnDrag へ届く)。なければ従来どおり <see cref="Click"/>。</summary>
    public bool PointerDown(float x, float y)
    {
        EmitInput("pointerdown", $"{x:0},{y:0}");
        if (_build != null && TryPick(x, y, null, out HitTarget t, out float lx, out float ly))
        {
            if (t.Draggable)
            {
                _captured = t;
                _dragStartLx = lx; _dragStartLy = ly;   // 捕獲時の位置 — 以後の Drag/DragEnd の Start/Delta の基準
                _dragStartX = x; _dragStartY = y;
                if (t.Focus != null) FocusTo(t.Focus);
                Guard(t.OnDragStart is null ? null : () => t.OnDragStart!(new PointerEvent(lx, ly, x, y)), "DragStart");
                return true;
            }
            // 最適ヒットが非ドラッグ → 通常クリックへ (Click も同じ TryPick でこのヒットに届く)
        }
        return Click(x, y);
    }

    /// <summary>ポインタ解放。捕獲中なら OnDragEnd を発火して解放する。
    /// ペイロードドラッグ中ならドロップ先の OnDrop を先に発火する。</summary>
    public void PointerUp(float x, float y)
    {
        EmitInput("pointerup", $"{x:0},{y:0}");
        if (_dragPayload is object payload)
        {
            if (FindDropTarget(x, y, payload, out HitTarget? dt, out float dlx, out float dly) && dt != null)
                Guard(() => dt.OnDrop!(payload, DragEv(dlx, dly, x, y)), "Drop");
            EndDrag();
        }
        if (_captured is not HitTarget cap) return;
        _captured = null;
        if (ToLocal(cap.Node, x, y, out float lx, out float ly))
            Guard(cap.OnDragEnd is null ? null : () => cap.OnDragEnd!(DragEv(lx, ly, x, y)), "DragEnd");
        else Guard(cap.OnDragEnd is null ? null : () => cap.OnDragEnd!(DragEv(0, 0, x, y)), "DragEnd");
    }

    // ---- ペイロード付きドラッグ (QP-M4 アプリ内 D&D) ----
    private float _dragStartLx, _dragStartLy, _dragStartX, _dragStartY;   // 捕獲時の位置 (ローカル/画面絶対)

    /// <summary>捕獲中イベント用 — 開始位置は PointerDown で記録した値。</summary>
    private PointerEvent DragEv(float lx, float ly, float x, float y)
        => new(lx, ly, x, y, _dragStartLx, _dragStartLy, _dragStartX, _dragStartY);

    private object? _dragPayload;
    private TwoD.UiNode? _dragGhost;
    private float _ghostGrabX, _ghostGrabY;
    private HitTarget? _dropHover;

    /// <summary>ペイロードドラッグ中か。</summary>
    public bool IsDragging => _dragPayload != null;

    /// <summary>ペイロード付きドラッグを開始する — ドラッグ捕獲中のハンドラ (onDrag) から呼ぶ。
    /// ghost は canvas 直下 (Z=4000) でポインタに追従し、PointerUp で最前面の
    /// <see cref="HitTarget.OnDrop"/> 持ちヒットへドロップされる。</summary>
    public void BeginDrag(object payload, TwoD.Scene2D ghost, float grabX = 10, float grabY = 10)
    {
        if (_dragPayload != null || _build == null) return;
        _dragPayload = payload;
        _ghostGrabX = grabX; _ghostGrabY = grabY;
        _dragGhost = _canvas.AddChild(_canvas.Root);
        _dragGhost.Z = 4000;
        _dragGhost.Content = ghost;
        _dragGhost.Opacity = 0.85f;
    }

    private void EndDrag()
    {
        Guard(_dropHover?.OnDropHover is { } off ? () => off(false) : null, "DropHover");
        _dropHover = null;
        if (_dragGhost != null) _canvas.Remove(_dragGhost);
        _dragGhost = null;
        _dragPayload = null;
    }

    /// <summary>最適な「payload を受け入れる OnDrop 持ちヒット」を探す (深い子を優先)。</summary>
    private bool FindDropTarget(float x, float y, object payload, out HitTarget? target, out float lx, out float ly)
    {
        target = null; lx = 0; ly = 0;
        if (_build == null) return false;
        if (!TryPick(x, y, t => t.OnDrop is not null && (t.AcceptsDrop is null || t.AcceptsDrop(payload)),
            out HitTarget hit, out lx, out ly)) return false;
        target = hit;
        return true;
    }

    /// <summary>(x,y) へポインタ移動。ドラッグ捕獲中は OnDrag へ (矩形外も届く)、
    /// それ以外は最前面ヒットを hover 状態にし、他を解除する。</summary>
    public void PointerMove(float x, float y)
    {
        if (_build == null) return;
        if (_captured is HitTarget cap)
        {
            if (ToLocal(cap.Node, x, y, out float lx, out float ly))
                Guard(cap.OnDrag is null ? null : () => cap.OnDrag!(DragEv(lx, ly, x, y)), "Drag");
            if (_dragPayload is object payload)   // ペイロードドラッグ: ゴースト追従 + ドロップ先 hover
            {
                _dragGhost?.Transform = TwoD.Affine2D.Translate(x - _ghostGrabX, y - _ghostGrabY);
                FindDropTarget(x, y, payload, out HitTarget? over, out float dlx, out float dly);
                if (!ReferenceEquals(over, _dropHover))
                {
                    Guard(_dropHover?.OnDropHover is { } off ? () => off(false) : null, "DropHover");
                    _dropHover = over;
                    Guard(over?.OnDropHover is { } on ? () => on(true) : null, "DropHover");
                }
                if (over?.OnDropMove is { } dropMv) Guard(() => dropMv(payload, DragEv(dlx, dly, x, y)), "DropMove");
            }
            return;
        }
        HitTarget? hit = TryPick(x, y, null, out HitTarget picked, out float hlx, out float hly) ? picked : null;
        if (!ReferenceEquals(hit, _hover))
        {
            Guard(_hover?.OnHover is { } off ? () => off(false) : null, "Hover");
            _hover = hit;
            Guard(hit?.OnHover is { } on ? () => on(true) : null, "Hover");
        }
        CurrentCursor = hit?.EffectiveCursor ?? CursorKind.Arrow;
        Guard(hit?.OnMovePos is { } mv ? () => mv(new PointerEvent(hlx, hly, x, y)) : null, "Move");   // ヒット中は毎移動 (SurfaceView 等の転送用)
    }

    // ---- キーボード / フォーカス ----

    public void FocusNext() => MoveFocus(+1);
    public void FocusPrev() => MoveFocus(-1);

    private void MoveFocus(int dir)
    {
        if (_build == null || _build.Focusables.Count == 0) return;
        FocusTarget? cur = Current();
        cur?.OnFocus?.Invoke(false);
        int n = _build.Focusables.Count;
        int i = cur is null ? -1 : _build.Focusables.IndexOf(cur);
        i = i < 0 ? (dir > 0 ? 0 : n - 1) : ((i + dir) % n + n) % n;
        _focusTarget = _build.Focusables[i];
        _focusTarget.OnFocus?.Invoke(true);
        EmitInput("focus", $"#{i}");
    }

    /// <summary>キー押下を配送する。Tab はフォーカス移動、その他はフォーカス中の対象へ。消費したら true。</summary>
    public bool KeyDown(Key key, bool shift = false, bool ctrl = false, bool alt = false)
    {
        EmitInput("keydown", $"{key}{(shift ? "+shift" : "")}{(ctrl ? "+ctrl" : "")}{(alt ? "+alt" : "")}");
        bool consumed;
        if (key == Key.Escape)
        {
            // フォーカス中コントロールが先に消費できる (補完ポップアップを閉じる等) —
            // 消費しなければ従来どおりオーバーレイのディスミスへ回す
            consumed = false;
            try { consumed = Current()?.OnKey?.Invoke(new KeyEvent(key, shift, ctrl, alt)) ?? false; }
            catch (Exception ex) { UiError.Report(ex, "KeyDown"); consumed = true; }
            if (!consumed)
                for (int i = _build!.Overlays.Count - 1; i >= 0; i--)
                    if (_build.Overlays[i].Open.Value && _build.Overlays[i].DismissOnOutside)
                    { _build.Overlays[i].Open.Value = false; consumed = true; break; }
        }
        else if (key == Key.Tab) { if (shift) FocusPrev(); else FocusNext(); consumed = true; }
        else
        {
            try { consumed = Current()?.OnKey?.Invoke(new KeyEvent(key, shift, ctrl, alt)) ?? false; }
            catch (Exception ex) { UiError.Report(ex, "KeyDown"); consumed = true; }
        }
        // アプリ全域ショートカット (AP-M5) — フォーカス中コントロールが消費しなかったときだけ
        if (!consumed && _shortcuts.TryGetValue(new KeyGesture(key, ctrl, shift, alt), out Action? act))
        {
            Guard(act, "Shortcut");
            return true;
        }
        return consumed;
    }

    private readonly Dictionary<KeyGesture, Action> _shortcuts = new();

    /// <summary>アプリ全域ショートカットを登録する (同ジェスチャは上書き)。配送はフォーカス中
    /// コントロールがキーを消費しなかったときだけ — エディタのタイプ/キーバインドを奪わない。</summary>
    public void RegisterShortcut(KeyGesture gesture, Action action) => _shortcuts[gesture] = action;

    /// <summary>ショートカット登録を解除する (未登録は no-op)。</summary>
    public void UnregisterShortcut(KeyGesture gesture) => _shortcuts.Remove(gesture);

    private FocusTarget? Current()
        => _focusTarget is { } f && _build != null && _build.Focusables.Contains(f) ? f : null;

    /// <summary>hover 中ヒットのカーソル形状 (WM_SETCURSOR がプラットフォーム経由で参照)。</summary>
    public CursorKind CurrentCursor { get; private set; } = CursorKind.Arrow;

    /// <summary>右クリック (コンテキストメニュー要求) を配送する。最前面の OnContext 持ちヒットへ
    /// ローカル座標で渡す。クリック同様フォーカスも移す。</summary>
    public bool ContextClick(float x, float y)
    {
        EmitInput("context", $"{x:0},{y:0}");
        if (_build == null) return false;
        if (TryPick(x, y, t => t.OnContext is not null, out HitTarget t, out float lx, out float ly))
        {
            if (t.Focus != null) FocusTo(t.Focus);
            Guard(() => t.OnContext!(new PointerEvent(lx, ly, x, y)), "Context");
            return true;
        }
        return false;
    }

    /// <summary>フォーカスを特定対象へ移す。既にフォーカス済みなら何もしない —
    /// OnFocus(true) の再発火はフォーカス時初期化 (TextField のキャレット末尾移動 = 選択クリア等)
    /// を誤って再実行させる (例: 右クリック→コピーの間に選択が消える)。</summary>
    public void FocusTo(FocusTarget f)
    {
        if (_build == null || !_build.Focusables.Contains(f)) return;
        FocusTarget? cur = Current();
        if (ReferenceEquals(cur, f)) return;
        cur?.OnFocus?.Invoke(false);
        _focusTarget = f;
        f.OnFocus?.Invoke(true);
    }

    /// <summary>現在フォーカス中のテキスト入力面 (TSF/IME ブリッジ用)。</summary>
    public ITextInput? ActiveTextInput => Current()?.TextInput;
    /// <summary>フォーカス中 caret の矩形 (canvas 座標)。IME 候補配置用。</summary>
    public Rect? CaretRect => ActiveTextInput?.CaretRect;

    // ---- IME/TSF が UI 層を経由してテキストを操作する仲介 (Platform はこれだけ呼ぶ) ----
    public string InputText => ActiveTextInput?.Text ?? "";
    public (int start, int length) InputSelection => ActiveTextInput?.Selection ?? (0, 0);
    public void InputSelect(int start, int end) => ActiveTextInput?.Select(start, end);
    public void InputReplace(int start, int end, string s) => ActiveTextInput?.Replace(start, end, s);
    public void InputSetComposition(ImeComposition comp) => ActiveTextInput?.SetComposition(comp);
    public void InputCommit(string final) => ActiveTextInput?.CommitComposition(final);
    public void InputSetCompositionHighlight(int start, int length, int targetStart, int targetLength)
        => ActiveTextInput?.SetCompositionHighlight(start, length, targetStart, targetLength);

    /// <summary>文字入力をフォーカス中のテキスト対象へ送る。</summary>
    public void Char(string text) { EmitInput("char", text); Guard(Current()?.OnText is { } h ? () => h(text) : null, "Char"); }
    /// <summary>IME 編集中文字列を送る (単純版)。</summary>
    public void Compose(string text) { EmitInput("compose", text); Guard(Current()?.OnCompose is { } h ? () => h(text) : null, "Compose"); }
    /// <summary>IME 状態 (対象節含む) を送る。</summary>
    public void Compose(ImeComposition comp) { EmitInput("compose", comp.Text); Guard(Current()?.OnComposeEx is { } h ? () => h(comp) : null, "Compose"); }
    /// <summary>IME 確定文字列を送る。</summary>
    public void Commit(string text) { EmitInput("commit", text); Guard(Current()?.OnCommit is { } h ? () => h(text) : null, "Commit"); }

    /// <summary>ホイール。点を含む最前面のスクロール対象へ量を渡す (transform 追従判定)。</summary>
    public void Wheel(float x, float y, float delta)
    {
        if (_build == null) return;
        for (int i = _build.Scrollables.Count - 1; i >= 0; i--)
        {
            ScrollTarget s = _build.Scrollables[i];
            if (HitTest(s.Node, s.Rect, x, y, out float lx, out float ly))
            {
                if (s.OnScrollPos is not null) Guard(() => s.OnScrollPos!(lx, ly, delta), "Scroll");
                else Guard(s.OnScroll is { } h ? () => h(delta) : null, "Scroll");
                return;
            }
        }
    }

    /// <summary>ワールド座標をノードの「現在の」ローカル座標へ写す (ドラッグ捕獲中の変換用)。</summary>
    private static bool ToLocal(UiNode node, float x, float y, out float lx, out float ly)
    {
        lx = ly = 0;
        if (!node.ComputeWorldNow().TryInvert(out TwoD.Affine2D inv)) return false;
        System.Numerics.Vector2 p = inv.Apply(new(x, y));
        lx = p.X; ly = p.Y;
        return true;
    }

    /// <summary>ノードのローカル矩形へのヒット判定 (WPF/Flutter 式)。
    /// 「現在の」変換ツリーを逆に写して判定するため、スクロール/スライド/scale/Visible に自動追従する。
    /// 祖先クリップ (ScrollViewer のビューポート等) の外は不発。成功時はローカル座標を返す。</summary>
    /// <summary>点 (x,y) を含むヒットのうち**最適なもの**を選ぶ — ノードが深い (子) ほど優先し、
    /// 同深度は登録順が後 (前面) を優先する。これが「イベントの正しいバブリング」の核:
    /// 親コンテナ (エディタの選択ヒット等) を全面に張っても、その上に載る子 widget
    /// (埋め込みボタン等) が先に拾える。<paramref name="filter"/> で用途別に絞る (OnContext 持ち等)。</summary>
    private bool TryPick(float x, float y, Func<HitTarget, bool>? filter,
        out HitTarget hit, out float lx, out float ly)
    {
        hit = null!; lx = ly = 0;
        int bestDepth = -1, bestIdx = -1;
        float blx = 0, bly = 0;
        for (int i = 0; i < _build!.Hits.Count; i++)
        {
            HitTarget t = _build.Hits[i];
            if (t.Active is not null && !t.Active()) continue;
            if (filter is not null && !filter(t)) continue;
            if (!HitTest(t.Node, t.Rect, x, y, out float hx, out float hy)) continue;
            int depth = NodeDepth(t.Node);
            if (depth > bestDepth || (depth == bestDepth && i > bestIdx))
            { bestDepth = depth; bestIdx = i; hit = t; blx = hx; bly = hy; }
        }
        lx = blx; ly = bly;
        return bestIdx >= 0;
    }

    private static int NodeDepth(UiNode n)
    {
        int d = 0;
        for (UiNode? p = n.Parent; p is not null; p = p.Parent) d++;
        return d;
    }

    private static bool HitTest(UiNode node, Rect rect, float x, float y, out float lx, out float ly)
    {
        lx = ly = 0;
        if (!node.IsShown) return false;                                     // Visible=false (非選択タブ/閉オーバーレイ)
        if (!node.ComputeWorldNow().TryInvert(out TwoD.Affine2D inv)) return false;
        System.Numerics.Vector2 p = inv.Apply(new(x, y));
        if (!rect.Contains(p.X, p.Y)) return false;
        for (UiNode? a = node; a != null; a = a.Parent)
        {
            if (a.Clip is not RectClip rc) continue;
            if (!a.ComputeWorldNow().TryInvert(out TwoD.Affine2D ai)) return false;
            System.Numerics.Vector2 ap = ai.Apply(new(x, y));
            if (ap.X < rc.X || ap.X > rc.X + rc.W || ap.Y < rc.Y || ap.Y > rc.Y + rc.H) return false;
        }
        lx = p.X; ly = p.Y;
        return true;
    }

    /// <summary>このホストのアニメーション時計 (AS-M3)。<see cref="Tick"/> の頭で dt を加算する —
    /// PropertyStateMachine 等の絶対時刻ベースのアニメがホスト内で共有する時刻源。</summary>
    public ManualClock Clock { get; } = new();

    /// <summary>アニメーションを 1 ステップ進め (dt 秒)、溜まった dirty widget を部分 Realize する。
    /// throw したアニメーションは報告して除去する (エラー境界 — 他のアニメと UI は生かす)。</summary>
    public void Tick(float dt)
    {
        Clock.Advance(dt);
        FlushRealize();
        if (_build == null) return;
        for (int i = _build.Animations.Count - 1; i >= 0; i--)
        {
            bool done;
            try { done = _build.Animations[i](dt); }
            catch (Exception ex) { UiError.Report(ex, "Animation"); done = true; }
            if (done) _build.Animations.RemoveAt(i);
        }
    }

    /// <summary>ユーザーハンドラの例外を握って報告する (エラー境界 — 入力 1 回の失敗でアプリを落とさない)。</summary>
    private static void Guard(Action? a, string ctx)
    {
        if (a is null) return;
        try { a(); } catch (Exception ex) { UiError.Report(ex, ctx); }
    }

    /// <summary><see cref="Widget.MarkNeedsRealize"/> で溜まった dirty widget をまとめて部分 Realize する
    /// (通常は Tick 頭で自動、テストや即時反映が要る場面では直接呼べる)。1 フレーム内の多重変更は
    /// 1 回に畳まれ、祖先が dirty な widget は親の処理に包含されるためスキップする。
    /// 境界判定: 直近の制約で再レイアウトしてサイズ不変なら自分が境界 (<see cref="ReRealize"/>)、
    /// 変わるなら親の <see cref="Widget.OnChildNeedsRealize"/> へ、誰も吸収しなければ SetRoot に縮退。</summary>
    public void FlushRealize()
    {
        if (_build == null || _build.Dirty.Count == 0) return;
        Widget[] dirty = _build.Dirty.ToArray();
        _build.Dirty.Clear();   // 処理中の再 Mark は次フレームへ (無限ループ防止)
        foreach (Widget w in dirty)
        {
            if (w.Scope is not { IsDisposed: false }) continue;   // 破棄済み/未実体化 — もう画面に居ない
            bool covered = false;
            for (Widget? p = w.ParentWidget; p != null && !covered; p = p.ParentWidget)
                covered = Array.IndexOf(dirty, p) >= 0;
            if (covered) continue;

            _layoutCtx.Theme = Theme.Peek();
            _layoutCtx.ViewportW = _width;
            _layoutCtx.ViewportH = _height;
            Size old = w.Size;
            w.Layout(w.LastConstraints, _layoutCtx);
            if (w.Size == old) { ReRealize(w); continue; }   // サイズ据え置き → 自分が境界

            bool handled = false;
            for (Widget? p = w.ParentWidget; p != null && !handled; p = p.ParentWidget)
                handled = p.OnChildNeedsRealize(w);
            if (handled) { EmitTree(); continue; }
            if (_root != null) { SetRoot(_root); return; }   // 全再構築 — 残りの dirty も包含される
        }
    }

    // ---- オーバーレイ実体化 (最前面レイヤ) ----
    private void RealizeOverlays()
    {
        if (_build == null || _build.Overlays.Count == 0) return;
        UiNode layer = _canvas.AddChild(_canvas.Root);
        layer.Z = 1000;

        foreach (OverlayEntry e in _build.Overlays)
        {
            Constraints cc = e.Placement switch
            {
                OverlayPlacement.RightEdge or OverlayPlacement.LeftEdge => new Constraints(0, _width, _height, _height),
                OverlayPlacement.BottomEdge => new Constraints(_width, _width, 0, _height),
                _ => Constraints.LooseW(_width, _height),
            };
            Size cs = e.Content.Layout(cc, _layoutCtx, parentUsesSize: true);
            Point pos = Place(e, cs);
            e.ContentRect = new Rect(pos.X, pos.Y, cs.Width, cs.Height);

            // 開閉トランジション: open/closed の状態遷移 (AS-M3 — 状態機械へ統一)。
            // 初期は瞬時 (snap 不変)。Visible は開度 > 0 の間だけ true — 閉アニメ完了で描画順から除外。
            OverlayEntry e2 = e;
            float dur = e.Placement switch
            {
                OverlayPlacement.RightEdge or OverlayPlacement.LeftEdge or OverlayPlacement.BottomEdge => 0.22f,
                OverlayPlacement.Center => 0.18f,
                _ => 0.13f,
            };
            var vis = new UiStates(_build!, new TransitionTable().Default(new TransitionSpec(dur)))
                .AddState("closed", ("t", 0f))
                .AddState("open", ("t", 1f));
            vis.Start(e.Open.Peek() ? "open" : "closed");
            _build!.Effect(() => vis.Goto(e2.Open.Value ? "open" : "closed"));

            if (e.Modal)
            {
                UiNode scrim = _canvas.AddChild(layer);
                var ss = new TwoD.Scene2D(); ss.FillRect(TwoD.Color2D.White, 0, 0, _width, _height);
                scrim.Content = ss; scrim.Color = TwoD.Color2D.Rgba(0, 0, 0);
                _build.Effect(() => scrim.Opacity = 0.45f * vis.Float("t"));
            }

            UiNode holder = _canvas.AddChild(layer); holder.Z = 1;
            e.Content.Offset = new Point(0, 0);
            e.Content.Realize(_build, holder, pos);
            float cw = cs.Width, ch = cs.Height;
            OverlayPlacement pl = e.Placement;
            _build.Effect(() =>
            {
                float t = vis.Float("t");
                holder.Visible = t > 0.001f;
                holder.Opacity = t;   // EffectiveOpacity でサブツリーに継承される
                holder.Transform = pl switch
                {
                    // ダイアログ: 中心スケール 0.96 → 1
                    OverlayPlacement.Center => TwoD.Affine2D.Mul(
                        TwoD.Affine2D.Translate(pos.X + cw / 2, pos.Y + ch / 2),
                        TwoD.Affine2D.Mul(TwoD.Affine2D.Scale(0.96f + 0.04f * t, 0.96f + 0.04f * t),
                                          TwoD.Affine2D.Translate(-cw / 2, -ch / 2))),
                    // ドロワー: 端からスライドイン
                    OverlayPlacement.RightEdge => TwoD.Affine2D.Translate(pos.X + (1 - t) * cw, pos.Y),
                    OverlayPlacement.LeftEdge => TwoD.Affine2D.Translate(pos.X - (1 - t) * cw, pos.Y),
                    OverlayPlacement.BottomEdge => TwoD.Affine2D.Translate(pos.X, pos.Y + (1 - t) * ch),
                    // トースト: 下から浮き上がる
                    OverlayPlacement.CornerBottomRight => TwoD.Affine2D.Translate(pos.X, pos.Y + (1 - t) * 16),
                    // ドロップダウン/ツールチップ: 6px ドロップ + フェード
                    OverlayPlacement.Below => TwoD.Affine2D.Translate(pos.X, pos.Y - (1 - t) * 6),
                    OverlayPlacement.Above => TwoD.Affine2D.Translate(pos.X, pos.Y + (1 - t) * 6),
                    _ => TwoD.Affine2D.Translate(pos.X, pos.Y),
                };
            });
        }
    }

    private Point Place(OverlayEntry e, Size cs)
    {
        float W = _width, H = _height, cw = cs.Width, ch = cs.Height, g = e.Gap, m = e.Margin;
        static float Clamp(float v, float lo, float hi) => MathF.Max(lo, MathF.Min(v, hi));
        switch (e.Placement)
        {
            case OverlayPlacement.Below:
            {
                Rect a = e.Anchor!();
                float x = Clamp(a.X, 0, MathF.Max(0, W - cw));
                float y = a.Y + a.Height + g;
                if (y + ch > H) y = a.Y - ch - g;
                return new Point(x, y);
            }
            case OverlayPlacement.Above:
            {
                Rect a = e.Anchor!();
                float x = Clamp(a.X, 0, MathF.Max(0, W - cw));
                float y = a.Y - ch - g;
                if (y < 0) y = a.Y + a.Height + g;
                return new Point(x, y);
            }
            case OverlayPlacement.RightEdge: return new Point(W - cw, 0);
            case OverlayPlacement.LeftEdge: return new Point(0, 0);
            case OverlayPlacement.BottomEdge: return new Point(0, H - ch);
            case OverlayPlacement.CornerBottomRight: return new Point(W - cw - m, H - ch - m);
            default: return new Point((W - cw) / 2, (H - ch) / 2);   // Center
        }
    }
}
