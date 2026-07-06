# 04 — Physics: 接触イベント + トリガーボリューム

## 概要

「何かに当たった」をゲームロジックが購読できるようにする: ①接触イベント (OnContactBegin/End 相当) と、②トリガーボリューム (物理応答なしで通過だけ検知する形状、OnTriggerEnter/Exit 相当)。ダメージ判定・ゴール判定・アイテム取得など、ゲームを作るのに事実上必須の機能。

## 背景と現状

- **ロードマップの記載場所**: [src/Luxel.Gallery/Stories/Docs/DocsGpu.cs](../src/Luxel.Gallery/Stories/Docs/DocsGpu.cs) の Docs/Physics「ロードマップ (v1 スコープ外)」節、項目 2。**着手前に必読** — 「Bepu の callbacks をキューイングして ECS へ変換する配管が必要」と設計方針まで書いてある。
- **フック地点**: [src/Luxel.Physics/Callbacks.cs](../src/Luxel.Physics/Callbacks.cs) — Bepu の `INarrowPhaseCallbacks` 実装。`ConfigureContactManifold` が全接触ペアで呼ばれる (マルチスレッド時は複数スレッドから並行に呼ばれる点に注意 — 既定は単スレッド)。
- 現状の構成: PhysicsWorld (Simulation 管理) / Components.cs (RigidBody 等) / PhysicsStepSystem (Step + Query 遅延登録)。body ↔ entity の対応表が PhysicsWorld にあるはず (transform 書き戻しに必要なので) — それをイベントの BodyHandle → Entity 逆引きに使う。

## 実装方針

### 1. イベント収集 (Callbacks → キュー)

- `ConfigureContactManifold` で接触ペア (CollidableReference の組) を**その場で処理せずキューに積む** (Docs 記載の方針どおり)。ロジック実行は Step 後にメインスレッドで。
- begin/end の判定: 「今ステップで接触したペア集合」を毎ステップ作り、前ステップ集合との差分で Begin (新規) / End (消えた) を出す。HashSet<(handleA, handleB)> のスワップで十分 (ペアは順序正規化: 小さい handle を先に)。
- 構造体キー + プーリングで GC を抑える (毎フレーム走る経路)。

### 2. トリガーボリューム

- コンポーネント案: `Trigger` (形状 + ポーズを持つ静的/キネマティック collidable、物理応答なし)。
- Bepu では「接触は検知するが力を発生させない」= `ConfigureContactManifold` で該当ペアの物理応答を false 返し (マニフォールドを無効化) しつつ、ペアはイベントキューへ積む。トリガー判定用に「この collidable はトリガー」の lookup (BodyHandle/StaticHandle → bool) を Callbacks に持たせる。

### 3. ECS への公開

- 案 A (推奨・最小): PhysicsWorld にイベントリスト公開 — `IReadOnlyList<ContactEvent> Events { get; }` (`ContactEvent { Entity A, B; ContactPhase Phase; }`、Phase = Begin/End)。PhysicsStepSystem が Step 後に差分計算して埋め、次の Step 冒頭でクリア。ゲーム側は Update system で `foreach (var e in physics.Events)`。
- 案 B: entity ごとのバッファコンポーネント。Friflo でのバッファ管理が煩雑なので v1 では A で良い。
- どちらでも「イベントはフレーム内で読み切り、持ち越さない」を規約に。

### 4. テスト + デモ + Docs

- 単体テスト (GPU 不要、決定的): 落下する箱が床に着く → Begin が 1 回 / 離れる (上向き速度を与える) → End。トリガー: 球がトリガーボリュームを通過 → Enter/Exit が出る + **速度が変わらない** (物理応答なしの検証)。
- デモ: 3D/PhysicsPlayground にゴールゾーン (トリガー通過でカウント表示) を足す、または専用ストーリー。play: クリックで弾発射 → Step → Expect (カウント変化)。初期絵は静止 (snap 決定的)。
- Docs/Physics: ロードマップ節から項目 2 を削除し、本文に「接触イベント」節 (購読方法のコード例 + トリガーの規約) を追加。

## 作業ステップ

1. Callbacks にペア収集 + トリガー lookup。PhysicsWorld に差分計算とイベントリスト。
2. Trigger コンポーネント + PhysicsStepSystem の登録経路。
3. 単体テスト (PhysicsTests に追加)。
4. デモストーリー + play + golden、Docs 更新。

## 罠・注意

- **handle → Entity の逆引き**が正確であること。body 削除 (現状は「リセット = 再構築」運用) 後に stale handle のイベントが出ないよう、差分集合も再構築時にクリア。
- `ConfigureContactManifold` は接触の**候補**段階でも呼ばれる — 実接触 (manifold に接触点があり depth > 0) だけ積むか、speculative を含めるかを決めて Docs に明記。ゲーム用途は実接触のみが無難。
- マルチスレッド (ThreadCount > 0) ではキューへの積み込みが並行になる — 既定単スレッドの間は List で良いが、コメントで前提を残す (ロードマップ項目 10 と関連)。
- KnockdownStory の「弾はエンティティ削除せず上限で場ごと再構築 (Slot パターン)」— 再構築とイベント状態のリセットの整合に注意。

## スコープ外

- 接触点の詳細 (位置/法線/深度) の公開 — Begin/End だけで v1 は十分。必要になったら ContactEvent に足す。
- キャラクターコントローラ (ロードマップ項目 4)。
