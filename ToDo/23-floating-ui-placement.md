# 23 — 浮遊 UI の共通化: anchored placement エンジン + Popup 統一

## 概要

浮遊 UI (ポップアップ/オーバーレイ) の配置を**単一の anchored placement エンジン**に統一し、画面端でのフリップ/シフト/クランプを一貫させる。決定は **ADR-0007** (`ADR/0007-Floating-Ui-Placement`) が正 — 着手前に読むこと。新スタックの補完ポップアップ (ToDo/22 S6c) はこの上に載せる。

## 背景と現状 (調査結果)

- **オーバーレイ層** [src/Luxel.UI/Overlay.cs](../src/Luxel.UI/Overlay.cs) + `UiHost.Place` ([src/Luxel.UI/UiHost.cs](../src/Luxel.UI/UiHost.cs) ~772、Z=1000): `OverlayEntry { Signal<bool> Open, Widget Content, OverlayPlacement Placement, Func<Rect>? Anchor, bool Modal, bool DismissOnOutside, float Gap, Margin }`。`ctx.RegisterOverlay(entry)` で登録 (realize 時)。ContentRect ベースの外側クリック閉じ + Esc first-refusal + modal scrim は実装済み。**Place は下↔上フリップ + X クランプのみ** (水平フリップ・shift-to-fit・side 配置・max-height/スクロール無し)
- **ContextMenu** [src/Luxel.Controls/ContextMenu.cs](../src/Luxel.Controls/ContextMenu.cs): この層を使わず Z=3000 の一点物 (`ConditionalWeakTable` で 1 個)。`Open`/`OpenForEditor`/`Close`。**端でクランプ/フリップしない**
- **既存の Below 系**: Overlays.cs の `Dropdown`/`Tooltip`、Select.cs、ColorPicker.cs — いずれも `RegisterOverlay(Below)` で Place のフリップ/クランプに乗っている (Select は幅固定で Stretch バグ回避)
- **CodeEditor 補完/ツールチップ** [src/Luxel.Controls/CodeEditor.cs](../src/Luxel.Controls/CodeEditor.cs) DrawPopup/DrawTip: **エディタ content 内の素の Scene2D ノード** → エディタのクリップに閉じ込められ画面外へ出られず、端でフリップしない
- **viewport サイズ**: `LayoutContext.ViewportW/H` (realize/layout 時)、`UiHost` の `_width/_height` (Place 内)

## 設計

### 配置エンジン (Luxel.UI、純粋・canvas 非依存)

```csharp
public enum PopupSide { Below, Above, Right, Left }
public enum PopupAlign { Start, Center, End }   // 交差軸のアンカーへの揃え

public sealed class AnchoredPlacement
{
    public PopupSide Side { get; init; } = PopupSide.Below;   // 希望 side
    public PopupAlign Align { get; init; } = PopupAlign.Start;
    public bool Flip { get; init; } = true;    // 入らなければ反対 side へ
    public bool Shift { get; init; } = true;   // 交差軸で画面内へずらす
    public float Gap { get; init; } = 6;       // アンカーとの隙間
    public float Margin { get; init; } = 8;    // 画面端との最小距離
    public float MaxWidth { get; init; }       // 0 = viewport 依存
    public float MaxHeight { get; init; }      // 0 = viewport 依存 (超過分は中身がスクロール)
}

public readonly record struct PopupSolve(Rect Rect, PopupSide Side, Size Constrained);

public static class PopupPlacer
{
    // アンカー矩形・中身サイズ・viewport から最適配置を解く (純関数)
    public static PopupSolve Solve(Rect anchor, Size content, Rect viewport, AnchoredPlacement p);
}
```

ソルバの規則: ①希望 side に Gap を空けて置く ②その side に入らなければ (viewport - Margin をはみ出す) 反対 side へフリップ (Flip 時) ③交差軸で画面内に収まるよう Align 起点からシフト (Shift 時) ④それでも viewport を超えるなら Constrained でサイズを詰める (呼び出し側が中身をスクロール)。戻り値の actualSide は矢印/アニメ方向に使う。

### OverlayEntry への統合

```csharp
OverlayEntry {
    ... 既存 ...
    AnchoredPlacement? Anchored { get; init; }   // 指定時は PopupPlacer.Solve を使う (OverlayPlacement を置換)
    bool CaptureFocus { get; init; }             // 開いたら中身へフォーカスを移す (任意)
}
```

`UiHost.Place` を: Anchored 指定があれば `PopupPlacer.Solve(anchor, content, viewport, Anchored)` を使い、無ければ従来の region 配置 (Center/Edge/Corner) にフォールバック。**2 ファミリ**: anchored (Side/Align/Flip/Shift) と region (Center/Edge/Corner)。

### 移行

- **ContextMenu**: Z=3000 の一点物をやめ、anchored Popup (Side=Below/Right、アンカー = クリック点の 0 サイズ矩形) へ。端でクランプ/フリップするようになる。API (`Open`/`OpenForEditor`/`Close`) は維持 (中身だけ差し替え)
- **Select / Dropdown / ColorPicker / Tooltip**: `OverlayPlacement.Below/Above` → `Anchored { Side=Below }` に置換 (挙動は同等 + 水平フリップ/shift が付く)
- **CodeEditor 補完 + ツールチップ**: これは**新スタックの補完 (ToDo/22 S6c) で対応** — 旧 CodeEditor は触らない。S6c の補完ポップアップを anchored Popup (アンカー = `ITextInput.CaretRect`) として実装し、エディタのクリップ外・画面端フリップを得る

## ステージ

1. **P1**: `PopupPlacer.Solve` + `AnchoredPlacement`/`PopupSide`/`PopupAlign` (Luxel.UI、純粋)。**単体テスト主体** — 下→上フリップ、右→左フリップ、交差軸シフト、max-height クランプ、Align 各種、Margin。golden 影響なし
2. **P2**: `OverlayEntry.Anchored` を足し `UiHost.Place` をソルバ経由に。既存 Below 系 (Select/Dropdown/Tooltip/ColorPicker) を Anchored へ移行。既存 golden を確認 (配置差分は意図分のみ --update)。任意で CaptureFocus
3. **P3**: ContextMenu を anchored Popup へ移行 (端クランプ/フリップ獲得)。story で端に寄せてフリップを golden 実証
4. **P4**: (S6c と合流) 新スタック補完ポップアップ + dwell ホバーを anchored Popup (CaretRect アンカー) で実装 → ToDo/22 S6c の完了

## 罠・注意

- 旧 CodeEditor は触らない (新スタックへ移行中)。旧の DrawPopup はそのまま
- Select の幅固定 (Stretch 行クランプ回避) の教訓を Popper でも踏襲 — anchored の中身が Stretch で潰れないよう幅を確定させる
- Place のリファクタは既存 modal/scrim/transition (UiStates) を壊さないこと。region ファミリは現状維持
- golden: Select/Dropdown/ColorPicker 系ストーリーの配置が数 px 動きうる → 意図差分として update
- viewport は `UiHost._width/_height`。headless テストでは 0 なので、ソルバ単体テストは viewport を引数で渡す (UiHost 非依存で書ける)

## スコープ外

- IME 候補ウインドウの自前描画 ([ADR-0008](story:ADR/0008-Custom-Ime-Candidates) / ToDo/24) — 本タスクの Popper を消費する別タスク
- サブメニュー (メニューのネスト) の自動 side 反転 — Right フリップの基盤は作るが、多段メニューの実戦投入は別途
- arrow (吹き出しの三角) — 必要になったら actualSide から足す
