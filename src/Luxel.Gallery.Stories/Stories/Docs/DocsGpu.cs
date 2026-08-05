using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

using Luxel.Typography.TwoD;
namespace Luxel.Gallery.Stories;

/// <summary>docs — GPU 土台の章 (GpuDevice / TwoD / RenderGraph / ThreeD)。
/// ページは $$""" (hole = 波かっこ 2 連) — C# コード例の波かっこ 1 連はリテラル。</summary>
public static partial class DocsGpu
{
    [Story("Reference/Guides/GpuDevice", Order = 10)]
    public static Widget GpuDevice(StoryContext ctx) => DocNew(ctx, $$"""
        # GPU 抽象 (GpuDevice)

        `Luxel.Graphics` は *No Graphics API* の原則を、resource・arguments・pass・dependency の **portable semantics** と backend-specific lowering に分けた薄い抽象です。アプリのコードは backend 分岐を持たず、Vulkan / DirectX 12 は bindless・GPUVA・明示 barrier を fast path として使います。native WebGPU は同じ意味論を bind groups、argument ring、pass segmentation へ lower する明示 opt-in backend として追加する方針です。

        ## backend と capability

        | backend | 対象と位置づけ | 主な lowering |
        | --- | --- | --- |
        | Vulkan 1.3 | native desktop、既存 backend | descriptor indexing / BDA、push constants、synchronization2 |
        | DirectX 12 | native Windows、既存 backend | descriptor heap / GPUVA、root constants、transition/UAV barrier |
        | WebGPU | native Windows・Linux/X11、明示 opt-in | fixed bind groups、argument ring、pass/encoder segmentation |

        WebGPU は browser 互換モードや自動 fallback ではありません。対象範囲、portable baseline に含めない機能、required limits と診断契約は [Reference/Guides/WebGPU](story:Reference/Guides/WebGPU) を参照してください。

        以下の固定 bindless layout と `Span<T>` の例は Vulkan / DirectX 12 の既存 fast path を説明します。portable contract では native address/index/map 方法ではなく logical resource references と Upload / DeviceLocal / Readback のデータフローを扱います。

        ## 固定パイプラインレイアウト

        全パイプラインが同じレイアウトを共有します:

        - **ルート引数** — 小さな構造体を push 定数 (Vulkan) / root 32bit 定数 (D3D12) で inline 渡し。`SetRootArguments(args)` に構造体を渡すだけ
        - **bindless heap** — すべてのバッファ/テクスチャは作成時に `BindlessIndex` を持ち、シェーダは `g_buffers[index]` / `g_textures[index]` で参照

        レイアウトが 1 つなので、ディスクリプタの束ね直しも PSO の組み合わせ爆発も起きません。「どのリソースを使うか」はルート引数の中の index が全てです。

        ## メモリとバッファ

        `device.Malloc(bytes, GpuMemoryKind)` でバッファを確保します。`HostMapped` は CPU から `Span<T>` で書き込みやすい upload 用、`DeviceLocal` は GPU 専用、`HostCached` は GPU→CPU 読み戻し用です。`HostMapped` は write-combined / uncached の場合があるため、CPU readback は `HostCached` へ GPU copyしてから行います。

        ```csharp
        using var device = new GpuDevice(VulkanBackend.Create());   // dx も可
        using var input  = device.Malloc(n * sizeof(float), GpuMemoryKind.HostMapped);
        using var output = device.Malloc(n * sizeof(float), GpuMemoryKind.HostMapped);

        input.Span<float>(n)[i] = ...;                    // CPU マップへ直接書き込み

        using var pipeline = device.CreateComputePipeline(GpuShaderCode.Load("compute01"));
        using var cmd = device.MainQueue.StartCommandRecording();
        cmd.SetComputePipeline(pipeline)
           .SetRootArguments(new Args { Input = input.BindlessIndex, Output = output.BindlessIndex, Count = n })
           .Dispatch((n + 63) / 64)
           .Barrier(GpuStage.ComputeShader, GpuStage.All);
        cmd.Finish();
        device.MainQueue.SubmitAndWait(cmd);
        // output.Span<float>(n) を読み戻して検証
        ```

        ## Slang 統一シェーダ

        シェーダは Slang で 1 回書き、SPIR-V (Vulkan) と DXIL (D3D12) の両方へコンパイルします。通常ビルドは Git 管理済みの `shaders/compiled/` を検証して使い、shader変更時だけ `CompileLuxelShaderCache` で両形式を更新します。

        - `g_buffers[index]` は Vulkan では set0/binding0 の storage buffer 配列、D3D12 では u0/space1 の unbounded UAV テーブルに lower される
        - シェーダはレンダーグラフにもバックエンドにも依存しない

        > [!TIP]
        > DXIL は `-validator-version 1.7` で出力しています — DXC 既定版の DXIL は OS ランタイムに拒否されることがあります。

        ## コマンドとバリア

        `MainQueue.StartCommandRecording()` → fluent にコマンドを積み → `Finish()` → `SubmitAndWait(cmd)`。同期は `Barrier(srcStage, dstStage)` の **stage バリア**だけ — リソース個別の状態遷移管理はありません (bindless + 単純化された使用パターンが前提)。

        ## 描画 (graphics PSO)

        graphics も同じ流儀です。`CreateGraphicsPipeline(shader, GpuRasterDesc)` で深度テストやブレンドを宣言し、dynamic rendering (`BeginRendering`/`EndRendering`) で RT/Depth を直接指定、頂点は**頂点プル** (頂点レイアウト宣言なし — シェーダが bindless バッファから読む) です。

        {{StoryRef(ctx, "Examples/3D/Depth")}}

        {{StoryRef(ctx, "Examples/3D/Blend")}}

        `StorySource` でこのデモの実装をそのまま引用できます:

        {{StorySource("Examples/3D/Depth")}}

        ## テクスチャとレンダーターゲット

        `CreateTexture` (ピクセルアップロード) / `CreateSampler` / `CreateRenderTarget` / `CreateDepthTarget`。RT の内容は `CopyTextureToBuffer` で bindless バッファへ書き出し、後段の compute/pixel シェーダが `Load` で読みます — swapchain 提示も docs 内の GpuView 埋め込みも、すべてこの経路です。

        > [!WARNING]
        > D3D12 の `CopyTextureToBuffer` は行 256B 整列が必要です。RGBA8 なら **ターゲット幅を 64 の倍数**にしてください (このページのデモはすべて 256)。

        次: [Reference/Guides/TwoD](story:Reference/Guides/TwoD) — この GPU 抽象の上に 2D ベクターを載せます。
        """, toc: true);

    [Story("Reference/Guides/TwoD", Order = 11)]
    public static Widget TwoD(StoryContext ctx)
    {
        ctx.Play(static d => d.Snap());   // 積み重なった GPU デモ 7 本が device lost せず描画される回帰 golden
        return DocNew(ctx, $$"""
        # 2D ベクター (Luxel.Graphics.TwoD)

        GPU **コンピュートラスタライザ** (Vello 風) による 2D ベクター描画です。パスを三角形分割せず、線分のまま GPU に常駐させ、compute が画素ごとに巻き数/距離で被覆を計算して塗ります。バックエンド変更ゼロ (framebuffer は bindless バッファ)。

        ## 描けるもの

        塗り (NonZero/EvenOdd — 穴あき対応)、複数パス合成、ストローク (距離ベース・画面一定幅)、**ベクターテキスト** (TTF 輪郭 → パス → 塗り、日本語対応)、角丸:

        {{StoryRef(ctx, "Examples/2D/VectorPaths")}}

        ## Scene2D とパス構築

        ```csharp
        var scene = new Scene2D();
        scene.FillRoundedRect(Color2D.Blue, 40, 40, 120, 80, 12);
        using (var jp = VectorFont.LoadSystemJapanese())
            jp.AppendText(scene, "こんにちは", 50, 120, 28, Color2D.Black);

        using var raster = new GpuDeviceRasterizer2D(device);
        using var encoded = raster.Encode(scene);                 // GPU へ 1 回
        raster.Render(cmd, encoded, Camera2D.Pixels, w, h, fb);   // ズームは Camera2D.Create(...)
        ```

        ## Camera2D — スムーズズーム

        ワールド座標で 1 回 `Encode` したら、`Camera2D` を変えるだけで連続拡縮できます — 再エンコードも再三角形分割もありません。ベクターなので拡大してもエッジが崩れないことを knob で確かめられます:

        {{StoryRef(ctx, "Examples/2D/Map", knobs: true)}}

        ## CameraRig2D — 追従カメラ (ゲームフィール)

        低レベルの `Camera2D` (ズーム/パン affine) の上に、ゲーム向けの高レベルコントローラ `CameraRig2D` があります。毎フレーム追従対象 (`Target`) を与えて `Update(dt, viewportW, viewportH)` し、`Camera(w, h)` で `Camera2D` を得ます。**デッドゾーン** (中央のこの矩形内では動かない)・**指数平滑** (フレームレート非依存の `1 - exp(-dt/tau)`、`* 0.1f` 方式は dt 依存なので使わない)・**ワールド境界クランプ** (画面端が `WorldBounds` を出ない、ワールドが画面より小さい軸は中央固定)・**画面シェイク** (`Shake(amp, duration, seed)`、固定シード xorshift なので golden 決定的) を持ちます。追従はターゲットが動いた後 = LateUpdate が定位置です。

        {{StoryRef(ctx, "Examples/2D/CameraRig")}}

        ## スプライトアトラス — 1 テクスチャに複数スプライト

        2D ゲームの素材は 1 枚のアトラス (テクスチャに複数スプライトを詰めた画像) に UV 矩形で参照するのが定石です。`SpriteAtlas` は名前 → `SpriteRect` (px 矩形 + ピボット) の辞書を持ち、JSON 定義 (`SpriteAtlas.FromJson`、リソース DAG なら `resources.Load<SpriteAtlas>("sprites.atlas.json")`) から読みます。テクスチャを密 (行ピッチ = 幅) に bindless バッファへ上げたら `Bind(srcIndex, atlasW, atlasH)` で GPU 情報を注入し、`scene.DrawSprite(atlas, "player_idle_0", x, y, scale)` でピボットを (x,y) に合わせて描きます。フレーム列は `SpriteAnimation` (名前プレフィクス + fps、固定 dt 積算で決定的) が現フレーム名を返します。

        ```csharp
        SpriteAtlas atlas = SpriteAtlas.FromJson(json);   // { "texture":"...", "sprites": { "run_0": {"x":0,"y":0,"w":32,"h":32,"px":16,"py":32} } }
        atlas.Bind(atlasBuf.BindlessIndex, atlasW, atlasH);
        scene.DrawSprite(atlas, "run_0", 100, 200, scale: 2f);

        var anim = new SpriteAnimation("run_", frameCount: 6, fps: 12f);
        anim.Update(dt);                                  // 固定 dt を積算 (wall-clock 禁止)
        scene.DrawSprite(atlas, anim, playerX, playerY);  // 現フレームを描く
        ```

        GPU 側は `GpuPath` の image シェイプ (`ImageSubRect`) がアトラスの**サブ矩形**を直接サンプリングします — `srcStride` = アトラス全幅 (行ピッチ)、`srcX/srcY` = サブ矩形原点、`srcW/srcH` = サイズ。clamp 付きサンプリングでサブ矩形境界に閉じるため、隣接スプライトへ滲みません (1:1 表示は nearest 相当)。image シェイプは GPU 専用 (Skia CPU バックエンドは非対応) です。

        {{StoryRef(ctx, "Examples/2D/Sprites")}}

        ## タイルマップ — グリッド描画 + AABB 衝突

        `TileSet` (スプライトアトラス + タイル px サイズ + id → スプライト名/衝突フラグ) と `TileMap` (int グリッド、0 = 空) で直交タイルマップを組みます。マップは自前 CSV (`TileMap.FromCsv`) か Tiled の `.tmj` 最小 import (`TileMap.FromTiledJson` — 外部エディタで編集できる) から読めます。描画は**チャンク単位** (既定 32×32 タイル) で、`TileMapLayer` が `RetainedCanvas` 上に**可視チャンクだけ UiNode 実体化**し、`SetTile` で dirty になったチャンクだけ再構築します (静的チャンクはジオメトリ不変 → スクロールは `Camera2D` 側)。大マップでも画面周辺のチャンクしか焼きません。

        衝突は物理エンジン非依存の純 AABB グリッドクエリです。`QueryAabb(rect)` は矩形に重なる衝突タイルを列挙し、`Sweep(box, delta, out hitX, out hitY)` は衝突タイルへめり込まない移動可能量を X→Y の軸分離で返します (プラットフォーマの定番)。純ロジックなので決定的にテストできます。

        ```csharp
        var tileSet = new TileSet(atlas, 16, 16, [new(1, new TileDef("grass", Solid: true)), new(2, new TileDef("wall", Solid: true))]);
        TileMap map = TileMap.FromCsv(tileSet, csv);
        var layer = new TileMapLayer(canvas, canvas.Root, map);
        layer.Update(cameraWorldView);                    // 可視チャンクを実体化/更新

        Vector2 moved = map.Sweep(playerBox, velocity, out bool hitX, out bool hitY);   // 壁の手前で止まる
        ```

        下のデモは壁柱へ右移動するプレイヤー (赤) が `Sweep` で壁の手前に切り詰められる様子です (アウトラインが意図した移動先):

        {{StoryRef(ctx, "Examples/2D/Tilemap")}}

        ## パーティクル — 標準 VFX システム

        爆発・煙・キラキラ等の VFX は `ParticleSystem` (プロジェクト `Luxel.Particles`) で作ります。エミッタは `Emit(pos, count)` (バースト) と `SetEmission(pos, rate)` (連続放出)、シミュレーションは寿命/速度/重力/抗力を `Update(dt)` で積分します。座標は Vector3 (2D は z=0) でコードパスは 1 本、乱数は固定シード xorshift・時間は固定 dt なので**決定的** (golden 安定)。内部は SoA の固定長配列 (GC ゼロ)、生存パーティクルは発生順で連続に並び、寿命切れは前方詰めで除去します (描画順が変わらない)。

        パラメータは判別共用体 `ParticleValue` (`Const` / `Range(min,max)` / `Curved(from,to,curve)`) に統一し、寿命に沿う色は `ParticleColor` (start→end を `ICurve` で補間、α 含む) で表します。乱流・引力等のフォースフィールドはエンジンに入れず、毎ステップ速度 span を加工する `Forces` フックでゲーム側が実装します。

        2D 描画は `Luxel.Particles.TwoD` の `ParticleNode` — `RetainedCanvas` の 1 ノードに `ContentColors` + `ReserveContent` で容量を確保しきり、毎フレーム生存パーティクルを Content 差し替えで描きます (Breakout の手法を部品化。容量内なら構造 Rebuild なし)。

        ```csharp
        var config = new ParticleConfig(
            Life: ParticleValue.Range(0.4f, 0.9f), Speed: ParticleValue.Range(60, 160),
            SpreadRadians: MathF.PI, BaseAngle: -MathF.PI / 2, Gravity: 260, Drag: 0.6f,
            Size: 5f, Color: new ParticleColor(yellow, redTransparent), Shape: ParticleShape.Quad);
        var ps = new ParticleSystem(config, capacity: 120, seed: 0x2024);
        var node = new ParticleNode(canvas, canvas.Root, ps);
        ps.Emit(new Vector3(x, y, 0), 90);
        ps.Update(dt);                                    // 固定 dt (wall-clock 禁止)
        node.Sync();                                      // 生存パーティクルを描き直す
        ```

        {{StoryRef(ctx, "Examples/2D/Particles")}}

        3D は `Luxel.Particles.ThreeD` の `ParticleBillboards` — 生存パーティクルを `RenderBuffer<T>` の instance 配列に詰め、`billboard.slang` が SV_InstanceID から各粒子をカメラ向きの quad (right/up 軸で展開) に開きます。深度テストあり・書き込み無し + アルファブレンドで、描画順は発生順 (半透明ソートは v1 でやらない割り切り)。`Spherical: true` の設定で +Y 軸まわりの円錐 (π で全球) に 3D 放出します。

        設定は JSON からも読めます (`ParticleConfigJson.FromJson`、リソース DAG なら `resources.Load<ParticleConfig>("explosion.particle.json")`)。DAG の watch/reload に乗るので「JSON 保存 → 実行中のゲームでエフェクトが変わる」ライブ編集が既存機構のタダ乗りで成立します。

        {{StoryRef(ctx, "Examples/3D/Particles")}}

        ## IRasterizer2D / RetainedCanvas — backend切替と部分更新

        `RetainedCanvas` はフレーム間で保持するノードツリーとCPU display-listだけを所有します。描画backendはcomposition rootで `IRasterizer2D` として選び、`CreateScene(canvas)` が同じツリーの変更を追跡するsessionを返します。GPU sessionはTransform / Style / Clip / Order / Segmentを分離したbufferを持つため、**移動 = transformだけ、色変更 = styleだけ**を書き換えます。

        ```csharp
        using IRasterizer2D raster = useCpu
            ? new SkiaRasterizer2D()
            : new GpuDeviceRasterizer2D(device);
        using var canvas = new RetainedCanvas();
        using IRasterScene2D rasterScene = raster.CreateScene(canvas);

        UiNode panel = canvas.AddChild(canvas.Root);
        panel.Transform = Affine2D.Translate(40, 40);
        panel.Content = new Scene2D().FillRoundedRect(Color2D.White, 0, 0, 400, 240, 16);

        if (raster is GpuDeviceRasterizer2D)
            rasterScene.Render(Camera2D.Pixels, new GpuRasterTarget2D(cmd, fb, w, h)); // callerがsubmit
        else
        {
            var cpuTarget = new SkiaRasterTarget2D(w, h);
            rasterScene.Render(Camera2D.Pixels, cpuTarget); // 復帰時に同期RGBAが読める
            ReadOnlyMemory<byte> rgba = cpuTarget.Pixels;
        }
        ```

        sessionはrasterizerより先に破棄します。異なるbackendのsession/target混在は例外になります。`GpuDeviceRasterizer2D`はcommand recording・bindless image・retained incremental updateを提供し、`SkiaRasterizer2D`は同期CPU RGBAを提供します。現在のimage shapeはbindless GPU resourceなのでSkiaでは `NotSupportedException` です。

        - `UiNode`: ローカル変換 / 色 / 不透明度 / 矩形クリップ / Z / 子 / Content。setter が dirty を伝播
        - クリップは祖先と交差して適用 (スクロール / パネル)
        - 描画順はツリー pre-order + 兄弟内 Z の order バッファ (奥→手前 alpha 合成)
        - 部分更新量は `LastTransformWrites` / `LastStyleWrites` / `LastSegmentBytesWritten` で観測できます

        ## 設計ノート: 増分更新 — 「slot 据え置き、レンジは容量付き」

        Content 差し替え (タイプ中のエディタ、ライブ波形) を O(シーン全体) のフル再構築にしないため、ノードの線分レンジに**容量 (capacity)** を持たせています。収まる差し替えは in-place 書き込み、伸びたら末尾へ追記して旧レンジを空きに。空きが閾値を超えたときだけフル再構築 = **まれなコンパクション**に降格します。

        - パス slot の中身を書き換えても描画順 (order バッファ) は不変
        - パス数が変わるときだけ order を再構成 (軽量パス)
        - 定常フレームのコストは O(変わったノード) — 回帰は bench で監視 (使い方は Gallery の Docs 章の Contributing ページへ)

        ## GPU rasterizerの現在構成

        現行 GPU pipeline は **bounds → 16×16 tile bin → fine raster** の3 compute passです。bounds passがscreen-space AABBを作り、bin passがpainter orderをtileごとに絞り、fine passが4×4スーパーサンプルでfill/stroke coverageとpremultiplied alpha合成を計算します。tile容量超過時だけcorrectnessを保つ全order走査へfallbackします。実装を追う場合は [Internal](story:Learn/Grapics/2D/Internal/Overview) へ進んでください。

        次: [Reference/Guides/RenderGraph](story:Reference/Guides/RenderGraph) — 多段パスの合成へ。
        """, toc: true);
    }
}
