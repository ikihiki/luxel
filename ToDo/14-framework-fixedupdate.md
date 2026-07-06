# 14 — Framework: FixedUpdate フェーズ (固定タイムステップ + 描画補間)

## 概要

GameLoop に固定タイムステップの FixedUpdate フェーズを追加する。物理・キャラクター制御・決定的ゲームロジックの土台であり、ゲームエンジンとしての必須機能。現状は各デモが accumulator を手作りしており (KnockdownStory の固定 1/120 蓄積器)、エンジン機能への昇格が必要。

## 背景と現状

- **GameLoop のフェーズ** ([src/Luxel.Framework/Phase.cs](../src/Luxel.Framework/Phase.cs)): EarlyUpdate(100) → Update(200) → LateUpdate(300) → PreRender(400) → Render(500) → PostRender(600) の priority 順。GameScene ([src/Luxel.Framework/Scene.cs](../src/Luxel.Framework/Scene.cs)) が 1 フレーム内で Input polling → 各 Update フェーズ (`World.RunPhase` + virtual フック) → RenderGraph 生成 → Render → ResourceSystem.Pump / AudioMixer.Tick を順序保証。
- **時間**: `FrameTime` ([src/Luxel.Framework/FrameTime.cs](../src/Luxel.Framework/FrameTime.cs)) = (Frame, DeltaSeconds, TotalSeconds)。dt は最大 1/30 にクランプされる可変ステップのみ。
- **既存の固定ステップ**: PhysicsWorld は内部固定 1/60。KnockdownStory は story 側で固定 1/120 の蓄積器を手書き + 「最初のクリックまで停止」。**同じ蓄積器がデモごとに再発明されている**のが問題。
- Ecs の `Phase` は Framework の `Phase` と名前衝突 (using alias 必要 — 既知の罠)。

## 実装方針

### 1. フェーズ追加と蓄積器

- `Phase.FixedUpdate` を追加 (priority は EarlyUpdate と Update の間、例 150。Unity と同じく「Update より前に、溜まった分を回す」)。
- GameScene のフレーム処理に蓄積器を内蔵:
  ```csharp
  _accum += dt;                          // dt は既存のクランプ済み可変 dt
  int steps = 0;
  while (_accum >= FixedDt && steps < MaxStepsPerFrame)   // spiral of death 防止 (既定 4〜8)
  {
      World.RunPhase(Phase.FixedUpdate.Name, FixedDt);
      OnFixedUpdate(FixedDt);            // virtual フック (他フェーズと対称)
      _accum -= FixedDt; steps++;
  }
  Alpha = (float)(_accum / FixedDt);     // 描画補間係数 [0,1)
  ```
- `FixedDt` は設定可能 (既定 1/60)。`MaxStepsPerFrame` 超過時は余剰を捨てる (スローモーション化を選ぶか捨てるかは設定 — 既定は捨てる + 診断イベント。DevTools 側の受け皿 = ステップ回数/Alpha/超過の Perf 表示は [21](21-devtools-game-scale.md) D)。
- `FrameTime` に `FixedDeltaSeconds` / `Alpha` を足すか、FixedUpdate 用の context を分けるかは既存の UpdateContext の形に合わせる。

### 2. 描画補間 (alpha)

- FixedUpdate で動くエンティティは「前ステップ位置」と「現ステップ位置」を持ち、Render 時に `lerp(prev, curr, Alpha)` で描く — これをやらないと 60Hz 表示 + 1/60 固定でも微妙にガタつく。
- v1 は**ヘルパー提供に留める**: `InterpolatedTransform` コンポーネント (Prev/Curr を持ち、PreRender の system が lerp して LocalTransform/UiNode に書く) を Luxel.Ecs か Framework に 1 個。全エンティティ強制はしない。

### 3. PhysicsWorld との統合

- PhysicsStepSystem を FixedUpdate フェーズに載せ、**GameScene の蓄積器に一本化** (PhysicsWorld 内部の独自蓄積器は「単独で使う場合の既定」として残すか、FixedDt を外から注入できる口を開ける)。物理とゲームロジックが同じ刻みで進むのが本命の姿。
- KnockdownStory / PhysicsPlayground の手書き蓄積器を新機構へ移行 (golden が変わらないことを確認 — 同じ刻みなら変わらないはず)。

## 作業ステップ

1. Phase.FixedUpdate + GameScene 蓄積器 + OnFixedUpdate フック + Alpha。
2. 単体テスト (GPU 不要): dt 列を流して FixedUpdate 呼び出し回数が正確 (例: dt=0.035 → 1/60 で 2 回 + 余り) / MaxSteps クランプ / Alpha の値 / 決定性 (同じ dt 列 → 同じ状態)。
3. InterpolatedTransform ヘルパー + テスト。
4. PhysicsStepSystem の FixedUpdate 移行 + 既存物理デモの追従 (e2e golden 差分ゼロを確認)。
5. デモストーリー: 高速移動する箱を「補間なし/あり」並べて見せる (play は固定 dt なので golden 決定的)。Docs/Framework に FixedUpdate 節を追記。

## 罠・注意

- **play/E2E は固定 dt で全操作を流す** — 蓄積器がフレームレート非依存であることと相性が良いが、既存 play の Step 数と物理の進み方が変わらないよう、移行時は FixedDt を従来デモと同値 (Knockdown は 1/120) に合わせる。
- 蓄積器の float 誤差: `_accum` は double で持つ (0.1×n の蓄積誤差でテストが割れた Strudel の前例)。
- `Phase` の名前衝突 (Ecs/Framework) — 追加コードでは using alias。
- FixedUpdate 内から可変 dt (`time.DeltaSeconds`) を読める形にしない (誤用の温床)。context で FixedDt だけ渡す。

## スコープ外

- 全コンポーネントの自動補間、ネットワーク同期用のティック番号管理、リプレイ (→ [11](11-scripting-debug-tools.md) B と将来合流できる設計だが今回は独立)。
