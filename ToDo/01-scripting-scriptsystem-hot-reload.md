# 01 — Framework ScriptSystem: .csx ゲームロジック + hot reload

## 概要

Luxel.Scripting (Roslyn C# スクリプト) を Luxel.Framework の実アプリに接続する。`.csx` ファイルでゲームロジック (system) を書き、World/Phase に登録して実行し、ファイル変更で hot reload できるようにする。Scripting P3 の明示的な残タスクであり、scripting 投資を「Gallery 内デモ」から「エンジン機能」に昇格させる本丸。

## 背景と現状

- **ScriptHost** ([src/Luxel.Scripting/ScriptHost.cs](../src/Luxel.Scripting/ScriptHost.cs)): Roslyn Scripting の薄いラッパ。`Run(code, globals) → ScriptResult` (Success/ReturnValue/Diagnostics/Exception/ExceptionLine)、`Check(code)`。コンパイルはソース文字列キーでキャッシュ (再 Run は再コンパイルしない)。`OpenSession(globals) → ScriptSession` で継続 REPL (前の Submit の変数が次で見える)。
  - **罠**: `WithEmitDebugInformation(true)` には `WithFileEncoding(UTF8)` 必須。実行時例外の行番号は FilePath("script.csx") + 正規表現 `script\.csx:line (\d+)` で抽出。
- **ScriptWorkspace** (src/Luxel.Scripting/ScriptWorkspace.cs): 補完/診断/ホバー用の in-proc 言語サービス。今回のタスクでは直接は使わない (エディタ統合は既に E4 で済み)。
- **Framework** (src/Luxel.Framework/): `LuxelHostBuilder` (DI) → `GameLoop` → `GameScene`、`Phase` (フェーズ別 system 実行)、`SceneManager`、`UiSurface`。ECS は Friflo (`world.AddSystem(Phase.Update.Name, action)` で delegate system を登録できる — BreakoutStory 参照)。
- **DI 登録簿**: Gallery 側は [src/Luxel.Gallery/GalleryServices.cs](../src/Luxel.Gallery/GalleryServices.cs) に ScriptHost/ScriptWorkspace/ICodeLanguage を AddSingleton 済み。Refs/Usings/ScriptGlobals もここに一元化。
- **Docs の記載**: Docs/Scripting (src/Luxel.Gallery/Stories/Docs/DocsRuntime.cs 内) に「面②: Framework の .csx ゲームロジック + hot reload (P3)」と方針記載済み。
- **未着手の理由**: FileSystemWatcher による hot reload は file IO が絡み決定性テストが困難 — 抽象を切る設計が必要だった。

## 実装方針

### 1. ScriptSystem 本体 (src/Luxel.Scripting または Luxel.Framework 拡張)

配置は要判断: Luxel.Framework に Roslyn 依存を入れたくなければ `Luxel.Scripting` 側に Framework 参照のブリッジクラスを置く (CsharpCodeLanguage が Controls+Scripting の橋を Gallery に置いたのと同じ判断。ただし ScriptSystem はエンジン機能なので Gallery ではなくライブラリ側に)。新プロジェクト `Luxel.Scripting.Framework` も選択肢。

```csharp
public sealed class ScriptSystem
{
    // scriptSource: パス or 論理名。IScriptSource (下記) から本文を取る
    public ScriptSystem(ScriptHost host, IScriptSource source, object globals);
    public void Attach(World world);   // スクリプトが返した system 登録を World/Phase へ
    public bool Reload();              // 再コンパイル → 成功時のみ差し替え、失敗時は旧を維持し診断を公開
    public ScriptResult? LastResult { get; }
}
```

- **スクリプトの契約**: `.csx` の最後の式が「system 登録記述子」を返す形にする。例: `Systems(Update: (World w, float dt) => {...}, Render: ...)` のようなヘルパーを globals に生やす。裸の delegate 1 個 (`(World, float) => void`) を Update 扱いにする最小形から始めてよい。
- **globals**: `World`、`FrameTime`、`Log(string)` あたり。既存 ScriptGlobals ([src/Luxel.Gallery/Stories/ScriptGlobals.cs](../src/Luxel.Gallery/Stories/ScriptGlobals.cs)) を参考に、Framework 用は別型。
- **失敗時の安全性が最重要**: Reload でコンパイル失敗/実行時例外 → 旧 system を動かし続け、診断 (行番号付き) を signal で公開。ゲームを止めない。

### 2. hot reload の決定性: IScriptSource 抽象

```csharp
public interface IScriptSource
{
    string Read();                 // 現在の本文
    event Action? Changed;         // 変更通知 (テストでは手動発火)
}
```

- 実装 A: `FileScriptSource` — FileSystemWatcher + デバウンス (保存直後の連続イベント/ロック中ファイルに注意: リトライ付き読み込み)。
- 実装 B: `MemoryScriptSource` — テスト/デモ用。`Set(text)` で Changed を発火。
- GameLoop への接続: Changed をフラグに立て、**フレーム先頭 (Drain 相当のタイミング) で Reload** — フレーム途中の差し替えをしない。

### 3. デモストーリー + Docs

- Framework アプリをストーリー内で駆動する仕組みは実装済み ([src/Luxel.Gallery/Stories/FrameworkAppStory.cs](../src/Luxel.Gallery/Stories/FrameworkAppStory.cs) — GPU はホスト借用 `UseGpuDevice(instance)`、ペーシングは SceneLoopServices.WaitFrame 差し込み)。これを土台に「Demos/Scripting/HotReload」ストーリー: 左に CodeEditor (LanguageService = CsharpCodeLanguage)、右に実行中シーン。編集 → Apply (= MemoryScriptSource.Set) → 挙動が変わるのを play で検証。
- play 例: 初期コード (箱が右へ動く) を Snap → SetText で速度を変える → Apply → Step(n) → Expect (位置の差で反映確認) → 構文エラーを入れて Apply → Expect (旧ロジックが生存 + 診断表示)。
- Docs/Scripting に「ScriptSystem」節を追記 (面②を「実装済み」へ)。

## 作業ステップ

1. IScriptSource + MemoryScriptSource + FileScriptSource を実装 (単体テスト: Changed 発火、リトライ)。
2. ScriptSystem: コンパイル → 登録記述子 → World への Attach。単体テスト (GPU 不要): MemoryScriptSource で system が動く / Reload 成功で挙動が変わる / Reload 失敗で旧が生きる+診断が出る。
3. GameLoop 統合 (フレーム先頭 Reload)。
4. デモストーリー + play + golden。GalleryServices に登録追加 (ScriptSystem 用 globals)。
5. Docs/Scripting 更新。

## 検証

- `dotnet test` — 新規テストは tests/Luxel.Tests/ScriptSystemTests.cs (GPU 不要で書けるはず。ScriptingTests.cs が手本)。
- `dotnet run --project src/Luxel.Gallery -- vk e2e --update "Scripting"` で golden 生成 → 2 回実行してハッシュ一致 (決定性)。

## 罠・注意

- Roslyn の初回コンパイルは 1〜2 秒 — GalleryServices.Provider は Lazy なので初回参照タイミングに注意 (play の Step 数に効く場合は事前ウォームアップ)。
- 編集ごとのアセンブリはプロセス終了まで残る (v1 割り切り)。hot reload を高頻度に回すデモではメモリが伸びる — collectible AssemblyLoadContext は本タスクのスコープ外だが、Docs に既知事項として書く。
- DI ストーリーの結線: `ctx.SetServices(GalleryServices.Provider)` は GalleryHost.BuildCurrent / GalleryApp.Select / DocsIndex.Build / E2ePlayTests.E2eCatalog.Discover の 4 箇所 — 忘れると play が列挙されず golden も出ない。
- Friflo クエリは `world.Query<T>().ForEachEntity((ref T c, Entity e) => ...)`。`Phase` は Ecs/Framework で名前衝突 (using alias)。

## スコープ外

- collectible ALC によるアンロード、サンドボックス、外部デバッガアタッチ (→ [11](11-scripting-debug-tools.md))。
