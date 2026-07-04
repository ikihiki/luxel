# Luxel.RenderGraph 設計プラン (RFC)

**ステータス:** Draft (2026-06-30)
**前提:** [deep-research 報告 (2026-06-30)](https://www.gdcvault.com/play/1024612/FrameGraph-Extensible-Rendering-Architecture-in) 業界実装 4 種 (Frostbite FrameGraph / UE RDG / Unity URP RenderGraph / Granite) が同じ Setup/Compile/Execute 三相モデルに収束していることが一次ソースで確認済み。
**関連メモリ:** `luxel-project.md`

---

## 1. 動機

Luxel は現状、`Rasterizer2D` が **1 dispatch / フレーム**で 2D を完結させる。多段が必要になる典型ケースが既に視野に入ってきた:

- UI をオフスクリーンへレンダ → ぼかし / 影 / 角丸 → 最終フレーム合成
- 3D シーン内に world-space UI を貼る (RTT を 3D サーフェスへ)
- ポストプロセス連鎖 (トーン / ブルーム / SSAO / TAA)
- compute 多段 (downsample chain, separable blur, jump flooding, SDF 生成, タイル binning, OIT 解決, ノイズ除去)
- 将来の 3D は **ECS** ベース

これらを「パイプラインの段階構成」と「中間リソースの寿命/同期/エイリアシング」として一元管理する層を導入する。

## 2. 設計の核心結論

1. **シーングラフは導入しない。** 業界調査の結論として、シーングラフは「描画対象の階層管理」、レンダーグラフは「パスとリソース依存の DAG 管理」であり別レイヤ。Luxel には既に `RetainedCanvas`+`UiHost` (2D/UI 側) があり、3D 側は将来 ECS が同じ役割を担う。**追加すべきはレンダーグラフのみ**。
2. **レンダーグラフは scene-agnostic に設計する。** 入力は GPU ハンドル (`GpuBuffer` / `GpuTexture`) のみ。`RetainedCanvas` / ECS / その他何でも、同じ pass builder を呼ぶ形にする。
3. **業界標準の Setup / Compile / Execute 三相**を採用 (Frostbite/UE/Unity/Granite が同型)。Setup で builder + lambda、Compile で寿命解析 + バリア生成、Execute で per-pass コールバック。
4. **段階導入する。** 第 1 段で MVP (寿命解析 + 自動バリア)、第 2 段で transient aliasing と cross-queue、第 3 段で ECS 抽象との界面、第 4 段で 3D + ECS 実装。
5. **既存の `ResourceSystem` とは別物**。`ResourceSystem` はアセットの (型, uri) DAG。レンダーグラフはフレームの (pass, resource) DAG。両者は直交し、両方が共存する。

## 3. レイヤ全体図

```
┌─────────────────────────────────────────────────────────────┐
│  シーン層 (何を描くか)                                       │
│  ┌──────────────────────┐  ┌──────────────────────────────┐ │
│  │ 2D/UI:               │  │ 3D (将来):                    │ │
│  │ RetainedCanvas       │  │ ECS World (Arch 等)           │ │
│  │ UiHost / signals     │  │ Parent/Children + Transform   │ │
│  └──────────────────────┘  └──────────────────────────────┘ │
└──────────┬──────────────────────────┬───────────────────────┘
           │ IRenderExtractor         │ IRenderExtractor
           ▼                          ▼
┌─────────────────────────────────────────────────────────────┐
│  Extract 層 (橋渡し)                                         │
│  ・シーンの状態を毎フレーム GPU バッファへ書く               │
│  ・SoA レイアウト (transforms / styles / instances / clips)  │
│  ・bindless slot 割当                                        │
└─────────────────────┬───────────────────────────────────────┘
                      │ GPU ハンドル (BindlessIndex の集合)
                      ▼
┌─────────────────────────────────────────────────────────────┐
│  Luxel.RenderGraph (どう描くか) ── 新規                     │
│  ┌──────────────────────────────────────────────────────┐   │
│  │ Setup: builder.Read/Write/CreateTexture, pass lambda │   │
│  │ Compile: topo-sort, lifetime, barrier, alias, sched  │   │
│  │ Execute: per-pass callback (GpuCommandBuffer)        │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────┬───────────────────────────────────────┘
                      │ GpuCommandBuffer + 自動バリア
                      ▼
┌─────────────────────────────────────────────────────────────┐
│  Backend (既存): IGpuBackend (Vulkan 1.3 / D3D12)           │
│  bindless 統一レイアウト, Slang SPIR-V/DXIL                  │
└─────────────────────────────────────────────────────────────┘
```

**重要な不変量:** RenderGraph 層は上方向 (シーン層) を一切知らない。下方向 (Backend) も既存抽象のまま (= バックエンド変更ゼロ)。

## 4. API スケッチ (C#)

```csharp
// === リソースハンドル (論理) ===
public readonly struct TextureHandle { internal int Id; }
public readonly struct BufferHandle  { internal int Id; }

// === パス記述 ===
public enum PassQueue { Graphics, Compute, AsyncCompute }

public sealed class RenderGraph : IDisposable {
    public RenderGraph(GpuDevice device);

    // Setup ----------------------------------------------------
    public TextureHandle ImportTexture(GpuTexture tex, string name);          // External
    public BufferHandle  ImportBuffer(GpuBuffer buf, string name);            // External
    public TextureHandle CreateTexture(in TextureDesc desc, string name);     // Transient
    public BufferHandle  CreateBuffer(in BufferDesc desc, string name);       // Transient

    public PassBuilder AddPass(string name, PassQueue queue = PassQueue.Graphics);

    // Compile + Execute (1 フレーム) ---------------------------
    public void Execute(GpuCommandBuffer cmd);
}

public sealed class PassBuilder {
    public PassBuilder Read(TextureHandle h, ResourceUsage usage);
    public PassBuilder Read(BufferHandle  h, ResourceUsage usage);
    public PassBuilder Write(TextureHandle h, ResourceUsage usage);  // 同一 pass で読み書きは ReadWrite
    public PassBuilder Write(BufferHandle  h, ResourceUsage usage);
    public PassBuilder ColorAttachment(TextureHandle h, int slot, LoadOp load, StoreOp store);
    public PassBuilder DepthAttachment(TextureHandle h, LoadOp load, StoreOp store);
    public void Execute(Action<PassContext> body);                   // body は lambda
}

public readonly struct PassContext {
    public GpuCommandBuffer Cmd { get; }
    public int BindlessIndex(TextureHandle h);   // 物理 slot を解決
    public int BindlessIndex(BufferHandle  h);
    public (int w, int h) ViewportSize { get; }
}

public enum ResourceUsage {
    SampledTexture,          // Vk: SAMPLED, D3D: SRV
    StorageTextureRead,      // Vk: STORAGE r-only,  D3D: UAV r
    StorageTextureWrite,     // Vk: STORAGE w-only,  D3D: UAV w
    StorageTextureReadWrite, // 同 pass で R+W
    ColorAttachment,         // RT
    DepthAttachment,         // DSV
    UniformBuffer,           // root args bag
    StorageBufferRead,
    StorageBufferWrite,
    StorageBufferReadWrite,
    IndirectArgs,
    CopySrc, CopyDst,
}
```

### 使用例: UI → blur → composite (第 1 段サンプル)

```csharp
using var rg = new RenderGraph(device);
var ui  = rg.CreateTexture(new TextureDesc(w, h, GpuFormat.R8G8B8A8Unorm, Storage|Sampled), "ui");
var tmp = rg.CreateTexture(new TextureDesc(w/2, h/2, GpuFormat.R8G8B8A8Unorm, Storage|Sampled), "blurH");
var blr = rg.CreateTexture(new TextureDesc(w/2, h/2, GpuFormat.R8G8B8A8Unorm, Storage|Sampled), "blurV");
var swap = rg.ImportTexture(surface.AcquireImage(), "swap");

rg.AddPass("UI", PassQueue.Compute)
  .Write(ui, ResourceUsage.StorageTextureWrite)
  .Execute(ctx => canvas.Render(ctx.Cmd, cam, w, h, /*fbBindless*/ ctx.BindlessIndex(ui)));

rg.AddPass("BlurH", PassQueue.Compute)
  .Read(ui,  ResourceUsage.SampledTexture)
  .Write(tmp, ResourceUsage.StorageTextureWrite)
  .Execute(ctx => blurPipeline.Dispatch(ctx.Cmd, ctx.BindlessIndex(ui),  ctx.BindlessIndex(tmp), horizontal:true));

rg.AddPass("BlurV", PassQueue.Compute)
  .Read(tmp, ResourceUsage.SampledTexture)
  .Write(blr, ResourceUsage.StorageTextureWrite)
  .Execute(ctx => blurPipeline.Dispatch(ctx.Cmd, ctx.BindlessIndex(tmp), ctx.BindlessIndex(blr), horizontal:false));

rg.AddPass("Composite", PassQueue.Graphics)
  .Read(ui,  ResourceUsage.SampledTexture)
  .Read(blr, ResourceUsage.SampledTexture)
  .ColorAttachment(swap, 0, LoadOp.Clear, StoreOp.Store)
  .Execute(ctx => compositePipeline.Draw(ctx.Cmd, ctx.BindlessIndex(ui), ctx.BindlessIndex(blr)));

rg.Execute(cmd);   // ここで Compile + Execute 一気
```

設計のキー: **pass の execute lambda には `GpuCommandBuffer` と bindless index 解決関数しか渡さない**。シーン側 (`canvas`) は捕捉済みだが、レンダーグラフ層は知らない (型シグネチャ上)。

## 5. リソースモデル

| 種別 | 寿命 | 例 | aliasing |
|---|---|---|---|
| **External** | グラフ外 | swapchain image, 永続テクスチャ, TAA prev frame, アセット (`ResourceSystem` 出力) | 不可 |
| **Transient** | グラフ内 | UI offscreen, blur ping-pong, SSAO 中間 | 寿命非重複なら可 |

External は `ImportTexture` / `ImportBuffer`。`ResourceSystem` で読み込んだ `GpuTexture` はそのまま `ImportTexture` で渡せる (= レンダーグラフ層は ResourceSystem を知らないが連携可能)。

### bindless 統一レイアウトとの結合

Luxel 既存の「set0/binding0 storage buffer 配列 + 8B push 定数 (root args アドレス)」を**そのまま温存**する。レンダーグラフが追加するのは「論理ハンドル → 物理 BindlessIndex のマッピング」だけ。

- External リソースは作成時の `BindlessIndex` をそのまま使う (既存と同じ)。
- Transient リソースは Compile 相で物理 slot を確保し、`BindlessIndex` を割当 (毎フレーム再利用しても良いし、aliasing 後の物理 slot をそのまま使ってもよい)。
- Pass の lambda は `ctx.BindlessIndex(handle)` で解決した int を **Slang シェーダの root args buffer に書き込む** (現状のサンプルと同じ流儀)。
- Slang シェーダ側は完全に**バックエンド非依存・レンダーグラフ非依存**。`g_buffers[idx]` / `g_textures[idx]` を従来通り参照。

## 6. Compile 相のアルゴリズム

```
1. ImportTexture/CreateTexture/AddPass/PassBuilder.Read/Write を全て収集
2. Pass を DAG ノード、Resource を辺として依存グラフ構築
3. デッドパスカリング: External への書き込みに到達しない pass を除去
4. トポロジカルソート: queue 別に並べ替え (Graphics/Compute/AsyncCompute)
5. 各 Resource について first-write-pass と last-read-pass の pass range を計算
6. Transient pool から物理リソースを取得:
   - 第1段: 単純 LRU (同形・寿命終了で reuse)
   - 第2段: 寿命非重複の transient で memory aliasing
7. パス境界ごとに必要な barrier を計算:
   - Vulkan: synchronization2 (VkAccessFlags2 + VkPipelineStageFlags2)
   - D3D12: Enhanced Barriers (D3D12_BARRIER_*) — Vortice 4.x が必要なら段階導入
   - layout transition + access mask
   - 第2段: split barrier (begin/end 分離)
8. queue 境界での semaphore (第2段)
9. Pass の execute lambda を順次起動
```

第 1 段は **(1)〜(7) の最小実装 + aliasing なし**で開始。aliasing は計測可能な利得が見えてから第 2 段で。

## 7. シーン層との分離 (IRenderExtractor)

第 3 段で導入する界面:

```csharp
public interface IRenderExtractor {
    void Extract(GpuDevice device, ExtractContext ctx);
    // ctx は: 書き込み先 bindless buffer の slot 割当 / フレーム index / etc
}
```

- **2D/UI extractor**: 既存 `RetainedCanvas.Flush` を `IRenderExtractor.Extract` 実装に再ラップ。SoA バッファ (transforms/styles/clips/order/segments) を bindless slot に書き、その slot 群を `BufferHandle` として返す。
- **3D + ECS extractor (将来)**: ECS の query 結果 → instance SoA buffer。詳細は §8。

Extract 自体はレンダーグラフのパスとして表現できる (= `AddPass("Extract2D", PassQueue.Compute)` で `Write(bindlessSoA)`)。または毎フレームの「事前段階」として明示に呼び、その出力 buffer を ImportBuffer する。**どちらでも構わない**が、初期実装は後者 (事前段階) が単純。

## 8. ECS 連携設計 (将来の 3D)

### 8.1 採用方針

- **C# ECS ライブラリは別途選定**。候補と特性:
  - **Arch** — SIMD chunk + 高速 query, 開発活発, .NET 標準, bindless と相性◎
  - **Friflo Engine ECS** — 最速ベンチ, 階層 (TreeNode) サポート
  - **fennecs** — 関係 (relation) サポート
  - **DefaultEcs** — 老舗, 安定
  - 第 4 段で 1 つを採用 (POC で比較推奨)。
- **Bevy パターン (MainWorld / RenderWorld 二重化) は当面採用しない**。初期は単一 world で `Extract → RenderGraph` の直列で十分。性能要求が出てから二重化を検討。

### 8.2 ECS 側の責務

ECS は「シーン階層 + コンポーネントの保持」だけを担う:

```csharp
// 階層
struct Parent     { Entity Value; }
struct Children   { Entity[] Items; }
struct Transform  { Affine3 Local; }
struct GlobalTransform { Affine3 World; }   // propagate system が更新

// 描画コンポーネント
struct MeshRef    { ResourceHandle<GpuMesh> Mesh; }
struct MaterialRef{ ResourceHandle<Material> Material; }
struct Visible    { bool Value; }
struct LightSource{ LightKind Kind; Vector3 Color; float Range; }
```

System の例:
1. `TransformPropagateSystem` — Parent をたどって GlobalTransform を更新 (DAG 走査)。
2. `CullingSystem` — frustum culling、Visible 更新。
3. `Render3DExtractor : IRenderExtractor` — Visible な entity を query → `InstanceData[]` SoA を bindless buffer に書く。

### 8.3 bindless 統一レイアウトでの instance データ

Luxel の既存 2D SoA (`GpuTransform[]` 等) と**同じ流儀**で:

- `instances` (BindlessIndex = N) ← `(transformSlot, materialSlot, meshSlot)` の配列
- `transforms` (BindlessIndex = M) ← `Affine3[]` (世界変換)
- `materials` (BindlessIndex = K) ← マテリアル パラメータ
- `meshes` (BindlessIndex = L) ← 頂点/インデックス bindless 参照

Slang 側はバックエンド非依存に `g_buffers[instanceSlot]` を読み、indirect draw / draw indirect count で全 instance を 1 ドローで処理する流れ (gpu-driven rendering)。

### 8.4 ECS の階層 vs シーングラフ不要論

ECS は `Parent`/`Children` コンポーネントで階層を素直に表せる。**シーングラフという独立した抽象を導入する必要はない**。これは Bevy 等の前例で実証済み。UI 側の `RetainedCanvas` も同じく「階層を持つが汎用シーングラフではない」という位置付け。

### 8.5 UI 側に ECS を入れない理由

- UI は reactive signals + retained tree のほうが DSL/部分更新と相性が良い。
- 100 個のラベルを 100 entity にすると過剰でフレーム単位 query のオーバーヘッドが大きい。
- Bevy も UI は別系統 (`bevy_ui`)。
- Luxel でも `Luxel.UI` は signals 系のまま。3D 側だけ ECS。

## 9. Luxel.DevTools 統合

第 2 段で追加:

- `EngineDiagnostics.Emit("Luxel.RenderGraph", DiagRenderGraph)` を Compile 直後に発行。
- `DiagRenderGraph` = `{ Passes[], Resources[], Edges[], Barriers[], Aliases[], PassRangeMs }`。
- `DevToolsListener` が `LatestSlot<DiagRenderGraph>` で保持し、`GET /rendergraph` で配信。
- `wwwroot/index.html` に「レンダーグラフ」パネル追加:
  - SVG で DAG 描画 (passes=ノード, resources=辺)
  - pass culling / aliasing 後の最終グラフを表示
  - 各 pass 実測時間 (GPU timestamp、後述)
- 第 3 段で GPU timestamp query 統合 (Vulkan: `VkQueryPool(VK_QUERY_TYPE_TIMESTAMP)`, D3D12: `ID3D12QueryHeap`)。

Unity URP の Render Graph Viewer を参照仕様とする。

## 10. マイルストーン

| 段 | 名称 | スコープ | サンプル |
|---|---|---|---|
| **RG-M1** ✅ | MVP (明示パス + 自動バリア) | `RenderGraph` / `PassBuilder` / `BufferHandle` / Setup+Compile+Execute 三相 / 寿命解析 / 自動バリア (`GpuStage` 集約) / Transient プール / External Import | **22**: UI → blur → composite (vk/dx 一致) |
| **RG-M2** ✅ | Aliasing + culling + 物理単位 barrier | Transient aliasing (同形・寿命非重複で interval scheduling 共有) / デッドパスカリング (backward reachability) / 物理バッファ単位の barrier 追跡 (alias 境界のハザード検出) | **23**: 反復ブラー×4 + DeadPass (論理 5 transient → 物理 2 alias、DeadPass culled、vk/dx 一致) |
| **RG-M3** ✅ | IRenderExtractor + DevTools | `IRenderExtractor` + `ExtractContext` 抽象 / `EngineDiagnostics.RenderGraph` イベント + `DiagRenderGraph` payload / `GET /rendergraph` + ブラウザの DAG パネル (culled/aliased/External 色分け SVG) | **24**: HTTP /rendergraph 経由で JSON を 12 項目 HttpClient 検証 (vk/dx 12/12 一致) |
| **RG-M4** ✅ | 3D 基礎 + ECS 導入 | `Luxel.ThreeD` 新規 / 最小 ECS (`World`/`Entity`/`Query<T1,T2,...>`, 100 LoC, 依存ゼロ — Arch 等への置換は将来) / `LocalTransform`/`GlobalTransform`/`Parent`/`MeshRef`/`Color3D` / `TransformPropagateSystem` / `Render3DExtractor : IRenderExtractor` / `CubeMesh` 固定 / `cube_forward.slang` 頂点プル+instance bindless+簡易拡散 | **25**: ECS で 5×5 キューブ + 深度 + RenderGraph 1 pass (vk/dx 非背景 8726/65536 完全一致) |
| **RG-M5** ✅ (一部) | 3D + post-process 連鎖 | additive bloom (`compute_bloom_combine.slang`) を 3D graphics pass の後段 (`CopyTextureToBuffer`→compute blur→combine) に接続 / `ResourceUsage.CopyDest` を明示宣言することで auto-barrier が Copy→Compute を発行 / **D3D12 backend の `CopyTextureToBuffer` で `CopyDest → Common` 遷移を入れて Copy→Compute ハザード解消** | **26**: 3D forward + bloom 連鎖 4 パス vk/dx 65536/65536 完全一致 |
| **RG-M5b** ✅ | world-space UI | `UiQuadMesh` (6 頂点 + UV) + `ui_quad_3d.slang` (push constants の mvp で配置, pixel で UI bindless buffer を `Load` サンプリング) + `Read(uiBuf, SampledInPixelShader)` で auto-barrier 発行 | **27**: 2D UI を 3D の傾いた板に sampling、キューブ群と同じ RT へ vk/dx 21072/65536 完全一致 |
| **RG-M5c** ✅ | shadow map | R32Float カラー RT に z を書き出す `shadow_pass.slang` + `CopyTextureToBuffer` で bindless buffer 化 + `cube_with_shadow.slang` が pixel で `Load` 比較。backend テクスチャ抽象は不変、Aaltonen 流の compute-first 哲学に従う実装。**push constants を 128→192B に拡張** (mat4×2 が必要) | **28**: 床+浮遊キューブ群、vk/dx 灰色域 42213 完全一致、床に影 |
| **RG-M6** ✅ | TextureHandle + Texture aliasing + 動的解像度 | `TextureHandle` / `TextureDesc` / `TextureKind{Color, Depth}` / `TextureUsage` を追加、`ImportTexture`/`CreateTexture`、`PassBuilder.Read/Write(TextureHandle)`、Compile 相で `(W,H,Format,Kind)` グループの interval scheduling aliasing、`ResourceAccess` を buffer/texture 統一表現に refactor、`PassContext.Texture(handle)`、Compile 後の物理確保 = `CreateRenderTarget`/`CreateDepthTarget` で `_ownedTransientTextures` 保持 → Dispose で一括解放 | **29**: 2 視点の同形 RT+Depth を寿命非重複で 4 論理→2 物理に alias、vk/dx 10927/65536 完全一致 / **30**: 3 解像度 (256/192/128) で順次 RG 再構築、毎フレーム transient RT/Depth 新規確保、vk/dx 完全一致 |
| **RG-M6 (任意)** | 動的解像度 + 部分更新 | 動的解像度 (transient 再割当 + dynamic resolution scaling) / dirty propagation (RetainedCanvas) との結線 / 動的 pass 追加削除 | サンプル既存への追加 |

各段で **vk/dx 両バックエンドのピクセル一致 + テスト追加**を要件にする (Luxel の慣習通り)。

## 11. 検証戦略

- **単体テスト** (`tests/Luxel.Tests/RenderGraphTests.cs` 新規):
  - 寿命解析: 期待された pass range が出るか
  - トポロジカルソート: 依存違反がないか
  - デッドパスカリング: External に到達しない pass が除去されるか
  - バリア生成: read → write / write → read の境界で正しい access mask
  - aliasing: 寿命非重複 transient が同じ物理メモリを共有するか (RG-M2)
  - extractor: `RetainedCanvas` 経由でも直接でも結果が一致 (RG-M3)
- **GPU 統合**: サンプル 22-26 が vk/dx 一致 (既存サンプルの慣習通り)
- **DevTools 統合テスト**: `HttpClient` で `/rendergraph` を取得し、期待 DAG 構造を検証 (RG-M3)
- **性能**: フレーム時間, transient メモリ watermark を `Luxel.Frame` payload に追加して計測

## 12. ResourceSystem との関係 (混同注意)

| 軸 | `Luxel.Resources` (既存) | `Luxel.RenderGraph` (新規) |
|---|---|---|
| 単位 | アセット ((型, uri) ノード) | フレームのパス (pass × resource ノード) |
| 寿命 | 多フレーム (refcount + reload) | 1 フレーム (transient) + External |
| キャッシュ | (型, uri) ノード共有 | 毎フレーム再構築 (動的 DAG) |
| 実行器 | Io / Cpu / Gpu レーン | Graphics / Compute / AsyncCompute queue |
| 例 | `Load<GpuTexture>("hero.tex")` | `AddPass("BlurH")` |

**併用パターン**: ECS の `MaterialRef` が `ResourceHandle<Material>` を持ち、その中の `GpuTexture` をレンダーグラフが `ImportTexture` で External として取り込む。

## 13. オープン問題

1. **D3D12 Enhanced Barriers** (D3D12_BARRIER_*) を採用するか、従来の `ResourceBarrier` で十分か。Vortice のバージョン要件と Windows 10 RS5 以降の制約 (Agility SDK 必要)。Enhanced は Vulkan synchronization2 と意味論が近いため自動バリア実装が楽だが、Agility SDK 回避方針 (既存メモリ参照) と矛盾。**RG-M1 は従来 ResourceBarrier で開始**し、Enhanced は RG-M2 で再評価。
2. **Slang のシェーダリンク (module / interface / generic) でパス合成 (effect composer) を表現できるか**。これは今回未検証。RG-M1 段階では「pass ごとに別 Slang ファイル + 個別コンパイル」で開始し、後で linkage 統合を検討。
3. **動的解像度**: transient リソース寸法をフレーム間で変える際の物理リソース再利用ロジック。Pool ハッシュキーに format/extent を含める素直な実装で十分か。
4. **ECS ライブラリ選定の正式判断**: Arch / Friflo / fennecs / DefaultEcs の POC 比較。RG-M4 開始時に決定。
5. **MainWorld / RenderWorld 二重化**: 性能要求次第。当面採用しない。
6. **Frame Graph Viewer の UX**: SVG 自前描画 vs ライブラリ (vis-network, d3-dag) 採用。Luxel の DevTools は外部依存ゼロが慣習なので SVG 自前推奨。
7. **bindless slot 枯渇対策**: 1 フレーム内で transient が大量に生成されるケース。Slot 上限のモニタリングと alias 強制をどうするか。

## 14. 参考文献

- Yuriy O'Donnell, "FrameGraph: Extensible Rendering Architecture in Frostbite", GDC 2017 — https://www.gdcvault.com/play/1024612/FrameGraph-Extensible-Rendering-Architecture-in / [slideshare](https://www.slideshare.net/slideshow/framegraph-extensible-rendering-architecture-in-frostbite/72795495)
- Themaister, "Render graphs and Vulkan — a deep dive", 2017 — https://themaister.net/blog/2017/08/15/render-graphs-and-vulkan-a-deep-dive/
- Granite engine — https://github.com/Themaister/Granite/blob/master/renderer/render_graph.hpp
- Epic Games, "Render Dependency Graph in Unreal Engine" — https://dev.epicgames.com/documentation/unreal-engine/render-dependency-graph-in-unreal-engine
- Unity, "Render graph introduction (URP)" — https://docs.unity3d.com/6000.3/Documentation/Manual/urp/render-graph-introduction.html
- Unity, "Analyze a render graph in URP (Render Graph Viewer)" — https://docs.unity3d.com/6000.0/Documentation/Manual/urp/render-graph-view.html
- AMD GPUOpen, "Render Pipeline Shaders SDK" — https://gpuopen.com/learn/rps_1_0/ / https://github.com/GPUOpen-LibrariesAndSDKs/RenderPipelineShaders
- Vello architecture (Flatten/Binning/Coarse/Fine) — https://deepwiki.com/linebender/vello/1.1-architecture
- Bevy render architecture (cheatbook) — https://bevy-cheatbook.github.io/gpu/intro.html

## 15. 次のアクション

1. 本 RFC をレビュー・承認
2. `src/Luxel.RenderGraph/` プロジェクト雛形作成 (`net9.0`, `Luxel` core のみ参照)
3. **RG-M1 着手**: `RenderGraph` / `PassBuilder` / `TextureHandle`/`BufferHandle` の最小実装 + サンプル 22 (UI → blur → composite)
