using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — ランタイム章 (Resources / Platform / Input / Audio / Framework / DevTools)。
/// ページは $$""" (hole = 波かっこ 2 連) — C# コード例の波かっこ 1 連はリテラル。</summary>
public static class DocsRuntime
{
    [Story("Docs/Resources", Order = 50)]
    public static Widget Resources(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # リソース (Luxel.Resources)

        アセットのロードは **(型, uri) をキーとするノードキャッシュ**の上で動きます。
        同じ (型, uri) は常に同じノードを共有し、参照カウントとリロードをシステムが管理します。

        ## Load と ResourceHandle

        ```csharp
        using var resources = new ResourceSystem(
            ResourceSystemDefaults.BuiltinSources(assetRoot: "./assets"),
            ResourceSystemDefaults.BuiltinSteps());

        ResourceHandle<CpuImage> img = resources.Load<CpuImage>("hero.png");
        await img.Ready;                    // または IsReady をポーリング
        Use(img.Value);
        img.Dispose();                      // refcount 減 — 0 で自動 evict

        resources.Watch();                  // ファイル変更 → 自動リロード (Reloaded イベント)
        resources.Pump();                   // 毎フレーム: リロード反映と破棄の消化
        ```

        `Publish(uri, value)` で外部所有のオブジェクトも登録でき、`Republish` すると
        依存先へリロードが伝播します。GPU デバイスロストは `NotifyDeviceLost()` で全再ロード。

        ## 自動コンポーズ — Step の連鎖

        「`CpuImage` を作る Step (png デコード)」を登録しておくと、`Load<CpuImage>("a.png")` は
        自動的に `CpuImage ← byte[] ← FileSource` の依存ノードを連鎖生成します。変換の追加は
        `IResourceStep` の登録だけ — 呼び出し側は最終型で Load するだけです。

        - **Source** (`IResourceSource`) — スキーム別の byte[] ロード (file / http / メモリ VFS)
        - **Step** (`IResourceStep<TIn, TOut>`) — 型変換。拡張子とフラグメントで選ばれる
        - **フラグメント** — `model.glb#mesh/0` のように 1 ファイルから複数リソースを
          切り出せます (glTF のメッシュ/マテリアル分割がこれ)

        ## 3 レーン実行 (Io / Cpu / Gpu)

        Step の中では `await ctx.Io` / `ctx.Cpu` / `ctx.Gpu` でレーンを移動します。
        ファイル読みは Io、デコードは Cpu、アップロードは Gpu — レーン毎に並列度が制限され、
        大量ロードでもフレームを崩しません。初回ロードの publish は Pump 不要で直接反映される
        ため、初期化時は `Ready.Wait` で同期的に待てます (Gallery のデモもこの形)。

        ## パッケージの役割分担

        | プロジェクト | 役割 |
        | --- | --- |
        | Luxel.Resources | コア (ResourceSystem / Source / Step / VFS) |
        | Luxel.Imaging | ImageSharp による画像デコード (依存を隔離) |
        | Luxel.Assets / AssetsGpu | アセット型定義 / GPU アップロード Step |
        | Luxel.AssetRuntime | ECS 統合 (Render3DExtractSystem 等) |
        | Luxel.Gltf | glTF 2.0 → AssetDocument |

        ## RenderGraph との関係

        混同注意 — Resources は**多フレーム寿命のアセット** ((型, uri) DAG)、RenderGraph は
        **1 フレームのパス合成** ((pass, resource) DAG) です。併用は「Resources でロードした
        GpuTexture を `ImportTexture` で External として取り込む」形
        ([Docs/RenderGraph](story:Docs/RenderGraph) の対比表も参照)。
        """, toc: true, fences: DocsFences));

    [Story("Docs/Platform", Order = 51)]
    public static Widget Platform(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # プラットフォーム (Luxel.Platform, Windows)

        実 OS ウィンドウ・スワップチェーン提示・IME を提供します (CsWin32 で Win32/TSF を生成)。
        オフスクリーン描画 (snap / bench) とはここだけが違います。

        ## ウィンドウとマルチウィンドウ

        `Win32Window` (CreateWindowEx + WndProc + PeekMessage) を `WindowSystem` /
        `WindowManager` が束ねます。ウィンドウは複数持て、インスタンスは GWLP_USERDATA で
        引く正攻法 — 静的マップがないので**別スレッドの別ウィンドウ** (ネイティブ DevTools)
        とも共存します。マウス/ホイール/キー/WM_CHAR/リサイズは `UiHost` へ配線されます。

        ## スワップチェーン提示

        compute が書いた RGBA8 framebuffer を swapchain image へ**コピーして present** します
        (Vulkan = vkCmdCopyBufferToImage、D3D12 = CopyTextureRegion)。framebuffer 幅は
        64 の倍数にパディングして D3D12 の 256B 行整列を満たし、可視領域のみ提示。
        両バックエンド R8G8B8A8 で swizzle 不要、リサイズで再生成します。

        ## IME (TSF、自前 preedit)

        `TsfTextStore : ITextStoreACP` がフォーカス中のテキスト入力へ橋渡しします。
        `GetTextExt` がキャレット矩形を返すので変換候補ウィンドウがキャレット位置に出ます。
        **preedit 下線・変換対象節ハイライト・キャレットは自前描画** (`ImeComposition` モデル) —
        TSF の文書はフォーカス中ブロックに局所化されています ([Docs/Editor](story:Docs/Editor))。

        ## カーソル・右クリック・クリップボード

        - `CursorKind` (Arrow / IBeam / Hand / Resize) を `HitTarget.Cursor` で宣言 —
          hover 先のカーソルが WM_SETCURSOR で反映されます
        - 右クリックは `OnContext` → `ContextMenu.Open` (エディタ標準の切り取り/コピー/貼り付け)
        - クリップボードは `IClipboard` 抽象 (Win32 実装 + テスト用フェイク)。
          リッチテキストは plain + markdown の両形式で書き込みます
        """, toc: true, fences: DocsFences));

    [Story("Docs/Input", Order = 52)]
    public static Widget Input(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # 入力

        入力は 2 系統あります — **UI 入力** (UiHost がポインタ/キー/IME をコントロールへ配送)
        と、**ゲーム入力** (Luxel.Input — アクションマップとリバインド)。

        ## UiHost の配送順

        KeyDown は次の順で消費されます: **Esc (オーバーレイのディスミス) → Tab (フォーカス
        巡回) → フォーカス中コントロール → 未消費のときだけアプリ全域ショートカット**。
        エディタのタイプや Ctrl+B を奪わないための順序です。

        ## ショートカット (KeyGesture)

        `UiHost.RegisterShortcut(new KeyGesture(Key.D, Ctrl: true), action)` でアプリ全域の
        キーマップを登録します。この Gallery の `Ctrl+D` (テーマ切替) がこれです。
        フィールドフォーカス中の Ctrl+A は選択として消費され (奪わない)、Ctrl+D は
        TextField が消費しないので発火する — という両方向が成立します。

        ## ドラッグ & ドロップ

        `UiHost.BeginDrag(payload, ghost)` + `HitTarget.OnDragOver/OnDrop` — ゴーストが
        ポインタに追従し、ドロップ位置インジケータを受け手が描きます。実物は
        [ListView/Reorder](story:ListView/Reorder) (行の並べ替え) へ。

        ## ゲーム入力 (Luxel.Input)

        アクションマップ (コンテキスト単位の有効/無効 — 例: gameplay と menu) と
        **キーバインドの JSON リマップ** (保存/復元の round-trip) を提供します。
        Framework のフレームループ (`InputBus` / `InputStack`) に統合されます。

        ## programmatic 入力

        ウィンドウなしで `host.Click(x, y)` / `host.Char("Hi")` / `host.KeyDown(Key.Tab)` を
        直接呼べます — ユニットテストや snap 前の状態強制はこの経路です。
        """, toc: true, fences: DocsFences));

    [Story("Docs/Audio", Order = 53)]
    public static Widget Audio(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # オーディオ (Luxel.Audio)

        `IAudioBackend` (Windows は XAudio2) の上に、SFX ミキサ / BGM ソース / 3D 音源 /
        バスによる音量カスケードを提供します。**Volume / Pitch / Pan が Signal** なので、
        UI ともアニメーション (Transition) とも自然につながります。

        ## 使い方

        ```csharp
        using var backend = new XAudio2Backend();   // IAudioBackend (テストは NullAudioBackend)
        backend.Initialize();

        // SFX: fire-and-forget (voice はプールから借用)
        var mixer = new AudioMixer(backend);
        mixer.PlayOneShot(clickClip, volume: 0.8f, pitch: 1.2f);

        // BGM: ハンドル型。Volume/Pitch/Pan は Signal — UI の Slider と直結できる
        var bgm = new AudioSource(backend, bgmClip) { Bus = musicBus };
        bgm.Play(loop: true);
        bgm.Volume.Value = 0.5f;

        // 毎フレーム: mixer.Tick() / source.Tick() (Signal 値を voice へ転写)
        ```

        ## 3D 音源

        `AudioSource3D` はリスナー相対で実効音量とパンを計算します — MinDistance〜MaxDistance
        の距離減衰、パンはリスナーの Right ベクトルとの内積。毎フレーム
        `source.Update(listener)` を呼びます。

        ## バス (AudioBus)

        Parent/Children のカスケードで `EffectiveVolume = 自分 × 親` — BGM / SE / UI 音の
        カテゴリ音量を 1 つの Signal で制御できます。`AudioRegistry` に登録すると
        DevTools の Audio パネルに可視化されます。

        ## リソース連携

        `Load<AudioClip>("bgm.ogg")` で Resources 経由のロード/キャッシュに乗ります。
        対応形式は WAV / OGG (NAudio.Vorbis によるデコード)。

        > [!NOTE]
        > 現状は 16bit PCM の全展開クリップのみ (ストリーミングと Doppler は将来枠)。
        > Tick/Update を呼ばないと Signal の変更が voice に反映されません。
        """, toc: true, fences: DocsFences));

    [Story("Docs/Framework", Order = 54)]
    public static Widget Framework(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # フレームワーク (Luxel.Framework)

        アプリの骨格です。Microsoft.Extensions.Hosting の DI に GPU / Resources / Audio /
        Input を統合し、シーンの生成・切り替えとフレームループを一括管理します。

        ## LuxelHostBuilder と GameScene

        ```csharp
        var host = LuxelHostBuilder.Create(args)
            .UseVulkan()                    // or UseD3D12()
            .UseAudio()
            .UseResources("./assets")
            .AddScene<TitleScene>()         // 起動シーン
            .Build();
        await host.RunAsync();

        public sealed class TitleScene(SceneLoopServices loop) : GameScene(loop)
        {
            public override Task OnLoadAsync() { /* World/Surface/リソース準備 */ return Task.CompletedTask; }
            protected override void OnUpdate(UpdateContext ctx) { /* ゲームロジック */ }
            protected override void OnRender(RenderContext ctx) { /* ctx.RenderGraph へパス追加 */ }
        }
        ```

        ## 6 フェーズのフレームループ

        EarlyUpdate → Update → LateUpdate → PreRender → Render → PostRender。
        GameScene の virtual フックと **ECS World の system** が同じフェーズ軸で実行されます
        (`AddWorld(world)` で登録、独自フェーズも Priority で挿入可)。Render フェーズは
        1 フレームの RenderGraph を受け取り、パスを積むだけで提示まで面倒を見ます。

        ## UiSurface — 複数サーフェスとレート制御

        1 シーンに複数の描画対象 (HUD / ワールド内モニタ / ミニマップ) を持てます。
        `Kind` は ScreenSpaceOverlay / WorldSpaceQuad / CameraStacked、**`RateHz` で
        サーフェス毎に更新頻度を独立制御** (60Hz の HUD と 10Hz のミニマップが共存) します。
        Draw コールバックは RenderGraph のパスとして統合されます。

        ## シーン切替

        `SceneManager.SwitchAsync<TScene>()` — 現シーンの cancel → OnUnloadAsync →
        次シーンの OnLoadAsync → RunAsync の順で入れ替わります。

        ## ECS × Signal (Luxel.Ecs.Signal)

        `world.Signal<Position>(entity)` で component を Signal 化できます — component の
        変更が UI へ自動伝播し、スライダーと entity が直結します。

        ## AppWindow — 最小構成

        フル DI が要らない小さなアプリは `AppWindow` (device + font + サイズ) に
        `SetRoot(widget)` して `Run()` するだけです。この Gallery も WindowManager +
        UiHost の同じ部品でできています。
        """, toc: true, fences: DocsFences));

    [Story("Docs/DevTools", Order = 55)]
    public static Widget DevTools(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # DevTools (Luxel.DevTools)

        エンジンとの結合は 2 本だけです — **DiagnosticListener (読み) + EngineCommands (書き)**。
        この疎結合がそのままスレッド境界・プロセス境界になります。

        ```mermaid
        flowchart LR
        engine[エンジン] -->|EngineDiagnostics.Emit| listener[DevToolsListener]
        listener --> panels[パネル群 - 別窓/別スレッド]
        panels -->|EngineCommands.Enqueue| engine
        ```

        ## ネイティブ DevTools (別ウィンドウ + 別スレッド)

        `DevToolsApp.Launch(createDevice, listener, commands)` の 1 行で、**自前の GpuDevice を
        持つ専用 STA スレッド**に DevTools ウィンドウが立ちます。デバイスを共有しないので
        メインのフレームレートに影響しません。Luxel の UI システム自身で描かれた
        ドッグフーディングです。

        ## パネル

        Frame (画面ミラー) / Trees (widget ツリー) / Log / Stat (fps・ヒープ・GC の
        Sparkline ダッシュボード) / ECS (ライブ component 値) / Res (リソース依存グラフ) /
        GPU / Graph (RenderGraph の DAG — culled/aliased の色分け) / Audio / Input / Surf。
        操作は pause / resume / step (`engine.*` コマンドの Enqueue)。

        ## HTTP DebugServer

        `DebugServer` を任意ポートで立てると、E2E テストや AI エージェントから HTTP で
        観測・操作できます: `GET /windows` (ウィンドウ一覧)、`GET /winframe?id=N&format=png`
        (スクリーンショット)、`GET /rendergraph`、`POST /cmd` (`op` 付き JSON)。
        この Gallery の実窓 E2E もこの経路です。

        > [!TIP]
        > `/winframe` は arm → 次リクエストで取得の 2 段構えなので、直近の操作の反映は
        > **1 リクエスト遅れ**ます。「クリックが効かない」と誤診しないよう、取得を 2 回
        > 重ねてから判断してください。

        ## スレッド設計の規約

        signal は所有する島 (スレッド) のみが触り、スレッド間は Listener (volatile/lock 済) と
        EngineCommands (ConcurrentQueue) だけ — [ThreadStatic] やグローバル可変状態は
        使いません。テーマも UiHost 単位の signal 所有です (DevTools 島は自前テーマを持つ)。
        """, toc: true, fences: DocsFences));
}
