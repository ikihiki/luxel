# 複合コントロール計画 (CC: CompositeControl)

目標: **内部に独自の Signal (状態) を持ち、既存コントロールを宣言的に組み合わせる**コントロールを、
手書きの PerformLayout/RealizeCore なしで書けるようにする。WPF の UserControl /
Flutter の StatefulWidget / React の関数コンポーネント + hooks に相当する層。

---

## 現状のギャップ

UI を作る方法は今 2 つあり、その間が抜けている:

| 方法 | 状態 (Signal) | 子の組み合わせ | 何が足りないか |
|---|---|---|---|
| **① ベアファクトリ関数** (ストーリーがやる方法) | クロージャで持てる | DSL (`VStack()[...]`) で宣言的 | **コンポーネントにならない**: 生成ファクトリ/[UiParam] props/DebugProps/knobs/状態 signal (Hovered 等) が付かない。再利用は「関数を呼ぶ」だけで、ツリー上の 1 widget として振る舞わない |
| **② フル Widget 派生** (LiveCodeBlock/TableBlock) | フィールドで持てる | **手書き** | PerformLayout で子のオフセットを手計算、RealizeCore で子を 1 つずつ Realize — StackPanel/Grid が使えず、LiveCodeBlock は行の配置を全部手で書いた |

つまり「**状態は②のように持ち、子は①のように宣言したい**」が今回の要求。

## 設計: CompositeControl 基底

```csharp
/// <summary>既存コントロールを宣言的に組み合わせる複合コントロールの基底。
/// Build() が返したサブツリーへレイアウト/実体化を委譲する — PerformLayout/RealizeCore は書かない。</summary>
public abstract class CompositeControl : Widget
{
    private Widget? _root;

    /// <summary>サブツリーを宣言する (初回レイアウト前に 1 回。Rebuild() で作り直せる)。
    /// 状態を保ちたい子コントロールはフィールドに保持し、ここでは参照を組み込むこと。</summary>
    protected abstract Widget Build();

    /// <summary>複合コントロール自身の登録 (ヒット/アニメ/Effect) が要るときの任意フック。
    /// 登録はこの widget のスコープに入り、再実体化で破棄・再登録される。</summary>
    protected virtual void OnRealize(UiBuildContext ctx) { }

    /// <summary>構造が変わった (子の増減等) とき呼ぶ — Build() し直して dirty 伝播で部分再実体化。
    /// 値の変化はこれを呼ばず Bindable/getter 束縛で反映すること (細粒度更新)。</summary>
    protected void Rebuild() { _root = null; MarkNeedsRealize(); }

    protected sealed override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        _root ??= Build();
        _root.Layout(c, ctx, parentUsesSize: true);
        _root.Offset = default;
        Size = c.Constrain(_root.Size);
    }

    protected sealed override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        SetWorldPos(worldOrigin + Offset);
        OnRealize(ctx);
        _root!.Offset = Offset;               // 自分の配置をそのままルートへ
        _root.Realize(ctx, parent, worldOrigin);
    }

    public override IEnumerable<Widget> DebugChildren() => _root is null ? [] : [_root];
}
```

書き味 (LiveCodeBlock がこうなる):

```csharp
[UiComponent]
public sealed partial class LiveCodeBlock : CompositeControl, IDisposable
{
    private readonly Signal<string> _code;       // ← 内部状態 (独自 Signal)
    private readonly TextArea _editor;           // ← 状態を保つ子はフィールド (Rebuild を跨いで生存)
    private readonly Sparkline _wave;
    ...
    [UiCtor]
    internal LiveCodeBlock(FencePayload payload, float maxWidth, Action<IBlockPayload> commit)
    {
        _code = new Signal<string>(payload.Body);
        _editor = TextArea(_code, height: 46f);
        _wave = Sparkline(maxWidth, 44f);
        ...
    }

    protected override Widget Build()
        => VStack(spacing: 4)[
               HStack(spacing: 6)[Button(Run, "Run"), Button(Toggle, "Stop/Go", variant: Variant.Ghost)],
               _editor,
               _wave];

    protected override void OnRealize(UiBuildContext ctx)
        => ctx.AddAnimation(dt => { if (_playing) Advance(dt); return false; });
}
```

手書きだった PerformLayout (行の配置計算) と RealizeCore (子の逐次 Realize) が丸ごと消える。

## なぜ今の基盤でこれが「薄い基底 1 枚」で済むか

この設計は新機構をほぼ要らない — 今学期に敷いた土台がそのまま支える:

1. **Realize テンプレート + RealizeScope (EX-M2a)**: `_root.Realize(ctx, ...)` は複合の
   スコープの子スコープになり、破棄/再実体化の寿命管理は自動。ctx.Effect/Own/AddAnimation も
   スコープ所有で漏れない。
2. **dirty 伝播 (EX-M2c)**: `Rebuild()` = `MarkNeedsRealize()`。サイズ不変ならその場で、
   変わるなら親へバブリング — 複合コントロール内の構造変化が画面全体を巻き込まない。
3. **canvas 増分更新 (IC)**: 再実体化後の毎フレーム更新も in-place。
4. **ジェネレーター**: `[UiComponent] partial` + `[UiParam] Bindable` フィールドは基底が
   Widget 派生なら既存のまま効く — 生成ファクトリ (Kit)/DebugProps/knobs/Tailwind が自動で付く。
5. **細粒度更新**: 値→表示は既存の Bindable/getter 束縛 (`Text(() => ...)`) がやる。
   Build() は「構造」だけを宣言する、が規約。

## 状態の 3 層 (規約として文書化する)

| 層 | 置き場所 | 更新経路 |
|---|---|---|
| **外部 props** | `[UiParam] Bindable<T>` フィールド | 呼び出し側/knobs/Tailwind が束縛。Effect で細粒度反映 |
| **内部の値状態** | `private Signal<T>` | Bindable/getter で子に配線 → 細粒度反映 (Rebuild 不要) |
| **内部の構造状態** | `private` フィールド (リスト等) | 変更したら `Rebuild()` — 明示 (Angular の markForCheck に相当) |

**状態を保つ子はフィールドに保持して Build() で参照を組み込む**のが鍵 — Rebuild はコンテナ
(StackPanel 等、状態なしで安い) だけ作り直し、TextArea や Sparkline のインスタンスは生き残る。
これは LiveCodeBlock が既に実践している形の一般化で、M2c の「同一インスタンス再ホスト」と同じ原理。

## 設計判断

- **Build は既定で非追跡 (1 回)**。React 的な「signal を読んだら自動再 Build」は
  opt-in の将来枠 (CC-M4) — 値変化のたびに構造を作り直すのは細粒度更新の逆行になりやすく、
  明示 `Rebuild()` の方が挙動が読める。まず明示で運用し、必要が実証されたら
  `Reactive.Effect` で Build 依存を捕捉する TrackedBuild を足す。
- **PerformLayout/RealizeCore は sealed**。逃げ道が要るケース (完全自前描画) は従来どおり
  Widget 直接派生 — 複合はあくまで「組み合わせ」専用に保つ。
- **keyed reconciliation はやらない**。リストの差分保存は「子をフィールド/リストで保持して
  Build で並べる」で足りる (Each はビルド時展開のまま)。仮想化リストは別コントロールの仕事。
- **入力/フォーカスは子のもの**。複合自身がヒットを持ちたいときだけ OnRealize で AddHit。

## 既知の注意点

- **Rebuild とフォーカス**: 子コントロールの FocusTarget は Realize 毎に新規作成が既定なので、
  Rebuild (再実体化) を跨ぐとフォーカスが外れる。TableBlock で導入した
  **FocusTarget 再利用パターン (`ctx.AddFocusable(既存インスタンス)`)** を TextField/TextArea にも
  展開すれば、複合の Rebuild でもフォーカスが生き残る (CC-M3 に含める)。
- **Margin/HAlign**: 複合自身の共通レイアウトプロパティは親コンテナが処理する (通常の widget と
  同じ)。Build ルートに Width/HAlign を付けたい場合は Build 内で普通に指定。
- **DebugChildren の深さ**: ルート 1 段を返す — ツリー表示は root 経由で子まで辿れる。

## マイルストーン

- **CC-M1: 基底 + 実証 — 完了 (2026-07-03)**。CompositeControl を Luxel.UI/CompositeControl.cs へ
  (設計どおり + `Root` プロパティをテスト用に公開)。LiveCodeBlock を移行 —
  **手書き PerformLayout (行配置計算) / RealizeCore (子の逐次 Realize) 約 30 行が
  `Build()` = `VStack(4)[HStack(6)[Run,Stop], _editor, _wave]` の 4 行に**。Tick 駆動は
  OnRealize フックへ。TextArea は `width: _maxW` を明示 (旧 Tight 制約の代替)。
  検証: **snap 48/48 ピクセル一致** (委譲レイアウトが手書きと同一ジオメトリの証明、vk/dx)、
  テスト 379 (+3 CompositeControlTests: Build 1 回/再レイアウトで再 Build しない/Rebuild で作り直し)、
  実窓 E2E (波形アニメ、タイプ → Enter → 新ライブブロック誕生 = factory 経由の複合が動作)。
- **CC-M2: ツール統合 — 完了 (2026-07-03)**。**SearchField** (Luxel.Controls/SearchField.cs) 新設 =
  状態 3 層の見本: query signal (値状態、TextField と双方向)、絞り込み候補リスト (構造状態 →
  変わったときだけ Rebuild)、[UiParam] Bindable&lt;int&gt; MaxSuggestions (外部 props)。
  候補の購読は `OnRealize` の `ctx.Effect` — Rebuild 後に再登録され同じ候補を読むので発振しない。
  [UiComponent] 生成ファクトリ (`SearchField(signal, candidates)`) がそのまま効き、Props ツリーに
  `SearchField → StackPanel V → StackPanel H → TextField/Button` が出る (introspection ✓)。
  ストーリー SearchField/Basic + golden (snap 49/49 vk/dx)。
  E2E: タイプ → 候補が開き絞り込まれる (ライブ Rebuild)、候補行クリック → 確定 + 候補が閉じる。
- **CC-M3: フォーカス生存 — 完了 (2026-07-03、CC-M2 の E2E で必要性が実証されたため同時実施)**。
  E2E で「Rebuild 後リングは残るがタイプが届かない」を実地確認 — 原因は 2 つ:
  ① TextField/TextArea が FocusTarget を Realize 毎に新規作成 → **再利用パターンへ移行**
  (TableBlock と同じ `_focus ??= new FocusTarget {...}; ctx.AddFocusable(_focus)`)。
  ② 複合のサイズが変わる Rebuild は祖先に吸収者がなく **SetRoot に縮退**するが、SetRoot が
  `_focusTarget = null` で無条件にフォーカスを消していた → **参照を保持したままにする**
  (再登録されなければ Current() の Contains 検証が自動解除するので無効参照は残らない)。
  E2E: "s"→"w"→"i" と 3 回の Rebuild をまたいで連続タイプが届き、キャレット/リングも生存。
- **CC-M4: TrackedBuild — 完了 (2026-07-03)。ユーザー判断で既定を反転**:
  「通常は Rebuild を呼ぶタイミングが分からない」ため **既定 = 自動追跡 (TrackBuild = true)**、
  パフォーマンス制御用に **opt-out (false = 手動 Rebuild のみ)** を残す。
  - **Reactive.Track(body, onInvalidate)** 新設: 本体を 1 回だけ依存追跡付きで実行し、
    読んだ signal が変化したら本体を再実行せず onInvalidate を呼ぶ (Flutter の markNeedsBuild 相当)。
    ReactiveEffect.Execute が再実行前に依存をクリアする性質により、通知後は自然にワンショット化
    (次の Build が新しい購読を張る)。
  - CompositeControl: `_root ??= Build()` を Track で包む。購読の寿命は
    **世代タグ付きスコープ所有** — Rebuild 直後の再実体化では「旧スコープ破棄」が新しい購読の
    後に走るため、世代一致時のみ解除 (リークなし、現役購読は誤殺しない)。
  - SearchField から OnRealize の購読ボイラープレートを削除 — Build 本体で
    `_matches = Filter(_query.Value)` と読むだけで自動 Rebuild。
  - 検証: テスト 382 (+3: 自動 Rebuild / ワンショット→再購読 / opt-out で自動なし)、
    snap 49/49 不変 (vk/dx)、実窓 E2E (swi 連続タイプ → 絞り込み → クリック確定、フォーカス生存)。
  - 規約 (doc コメントに記載): 値→表示は getter/Bindable 束縛のまま (Build の依存にならない =
    細粒度更新)。Build 本体での `.Value` 読みだけが構造の依存。追跡させたくない読みは `Peek()`。

## リスク

- **PerformLayout 委譲と parentUsesSize**: ルートの自然サイズをそのまま Size にする —
  Stretch 系の意味論はコンテナ側の既存規則に従う。既存 widget と同じ挙動になるよう
  CC-M1 でレイアウトテストを足す。
- **Rebuild 中の古い参照**: Commit コールバック等がコンテナの子 index を掴むと Rebuild でズレる —
  「参照 (フィールド) を掴む」規約で回避 (RichTextEditor の Block 参照解決と同じ教訓)。
- **二重 Realize**: _root は複合の RealizeCore からのみ Realize される (親コンテナは複合自身を
  子として扱う) — 混在しないことをテストで担保。
