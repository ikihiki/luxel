# 15 — 永続化: ゲーム状態のセーブ/ロード + 設定ファイル機構

## 概要

2 つの永続化機能を追加する: **A. ゲーム状態 (ECS World) のセーブ/ロード**、**B. アプリ設定 (音量・画質・キーバインド) の読み書き**。どちらもゲームを出荷するための必須機能で、現状は API がゼロ。A と B は独立に着手できる。

## 背景と現状

- **転用できる資産がある**: DevTools 用に **per-entity コンポーネントを JSON にシリアライズする実装が GameScene に既にある** (DevTools emit 用、フィールドレベルの値書き換えにも対応)。セーブはこれの兄弟。
- **Friflo ECS 自体のシリアライザ**: Friflo.Engine.ECS には EntitySerializer (JSON) が組み込まれている — **最初に「Friflo 組み込みで足りるか」を調査**し、足りるなら独自フォーマットを発明しない (依存も増えない)。足りない場合 (Signal 連携コンポーネント、GPU ハンドル持ちコンポーネントの扱い) のみ薄いラッパを書く。
- **InputBindings は YAML/JSON 対応済み** (src/Luxel.Input/InputBindings.cs) — 設定機構はこれの置き場と統合を決めるだけで大部分が済む。
- **設定 UI の材料も揃っている**: Signal + TextField/Slider/Select/Switch — 設定画面はコントロールで組めるので、設定ストアが Signal を返せば双方向が自然に繋がる。

## A. ゲーム状態のセーブ/ロード

### 方針

1. **調査**: Friflo の EntitySerializer で World → JSON → World の往復ができるか、GPU ハンドル (MeshRef 等) や参照持ちコンポーネントがどう落ちるかを確認。
2. **API** (Luxel.Framework or Luxel.Ecs):
   ```csharp
   public static class WorldSave
   {
       public static string Serialize(World world, SaveOptions? opt = null);   // JSON 文字列
       public static void Deserialize(World world, string json);              // クリアして復元 or 追加
   }
   ```
   - ファイル IO は分離 (文字列 in/out) — **テストが file IO なしで書ける** (決定性の定石)。ファイル層は B の SettingsStore と共通の薄いヘルパで。
3. **シリアライズ対象の規約**: 「保存されるのは純データコンポーネントだけ」— GPU ハンドル/delegate/Signal 持ちは対象外とし、復元後に再構築する規約 (`[SaveIgnore]` 属性 or 型フィルタ)。復元フック `ISaveRestorable` (エンティティ実体化後に GPU 資源を遅延再取得 — 「GPU 資源はシーンの最初のフレーム内で遅延生成」の既存規約と同型)。
4. **バージョニング**: ルートに `{ "version": 1, "entities": ... }`。ロード時に version を見て migration デリゲートのチェーンを通す (v1 は枠だけ — version 不一致で明示エラーにしない設計だけ入れる)。

### テスト

- 往復テスト (GPU 不要): World に既知エンティティ群 → Serialize → 新 World に Deserialize → コンポーネント値一致。
- 除外規約: [SaveIgnore] 対象が JSON に出ない。
- 決定性: 同じ World から 2 回 Serialize して文字列一致 (エンティティ列挙順が安定か要確認 — 不安定なら Id 順にソート)。

## B. 設定ファイル機構

### 方針

1. **SettingsStore** (Luxel.Framework):
   ```csharp
   public sealed class SettingsStore
   {
       public Signal<T> Get<T>(string key, T fallback);   // 初回アクセスで登録、Signal は使い回し
       public void Save();                                 // 明示保存 (+ 変更デバウンス自動保存はオプション)
       public static SettingsStore LoadFrom(IFileStore files, string name);
   }
   ```
   - **Signal を返すのが肝**: 設定画面のコントロールに直結でき、購読側 (音量 → AudioMixer) も effect で反応できる。
2. **保存先**: 既定 `%APPDATA%/<ゲーム名>/settings.json` (LuxelHostBuilder にゲーム名/保存先の設定を追加)。開発中はリポジトリ外に書くこと (テスト成果物がリポジトリを汚さない)。
3. **IFileStore 抽象** (Read/Write/Exists): 単体テストはインメモリ実装 — file IO 非依存で決定的に ([01](01-scripting-scriptsystem-hot-reload.md) の IScriptSource と同じ判断)。
4. **InputBindings 統合**: バインディングファイルを SettingsStore 管轄のフォルダに置き、リバインドの永続化の受け皿にする (リバインド UI 自体は別タスク・Tier 2)。
5. **壊れたファイル耐性**: パース失敗 → 既定値で起動 + 破損ファイルを .bak 退避 (設定破損でゲームが起動しないのは最悪)。

### テスト

- インメモリ IFileStore で Get/Save 往復、既定値、破損 JSON → フォールバック、Signal の変更が Save に反映。

## デモ + Docs

- デモストーリー「Demos/Framework/SaveLoad」: 小さな ECS シーン (位置の違う箱数個) → Save ボタン → 箱を動かす → Load ボタン → 元に戻る (play: Click/Expect で状態復元を検証。ファイルはインメモリ IFileStore を使い決定的に)。
- 設定は「Demos/Framework/Settings」: Slider (音量) + Switch → SettingsStore → 再構築後も値が残る play。
- Docs/Framework に「永続化」節を追加。

## 罠・注意

- Friflo の JSON が内部表現 (構造 index 等) を含む場合、**バージョン間の安定性がない**可能性 — その場合は「コンポーネント名 → 値」の自前スキーマに寄せる (セーブデータはエンジン更新をまたいで生きる必要がある)。調査の主眼はここ。
- float の丸め: JSON 経由で float が bit 一致するか (R format)。決定性テストで顕在化する。
- Signal<T> の T が参照型のときの共有 (Load で新インスタンスに差し替わる) — 購読側は effect で読む規約に。

## スコープ外

- クラウドセーブ、セーブスロット UI、暗号化、自動 migration の実装 (枠のみ)。
