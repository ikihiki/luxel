# 09 — glTF skin (スケルタル) / morph (ブレンドシェイプ) アニメーション

## 概要

glTF アニメーションのうち未対応の skin (ジョイント階層 + 頂点スキニング) と morph target (ブレンドシェイプ) を実装する。translation/rotation/scale の Track は v1 実装済み (Docs/Motion に「skin/morph は将来」と記載)。キャラクターアニメーションが動くようになる、3D 系では最も見栄えのするタスク。

## 背景と現状

- **glTF ロード**: src/Luxel.Gltf/ — シーン/メッシュ/アニメーション (T/R/S track) の読み込みは実装済み。skin (joints/inverseBindMatrices) と morph targets (POSITION/NORMAL の差分 + weights) の読み込みが未対応かは**最初にコードで確認** (パース済みで実行系だけ未対応の可能性もある)。
- **アニメーション基盤**: Luxel.Animation — 3 層 IR (Clip/Track/Player) + ターゲットアダプタ (.UI/.TwoD/.ThreeD)。glTF アニメは Track に写して Player で駆動する構造。morph weights は「float 配列への Track」として IR に乗るはず。
- **3D 描画**: Luxel.Ecs (Friflo) + LocalTransform/MeshRef + IRenderExtractor (SceneRenderExtractor) + RenderGraph (scene_pbr_lite 等の Slang シェーダ)。シェーダは shaders/ にあり、SPIR-V + DXIL 併存コンパイル。
- **デモの前例**: 3D/GltfBox・3D/GltfAnimated (scene_pbr_lite に indexBufIndex/instanceStart を追加し SceneRenderExtractor と対にした経緯あり)。

## 実装方針

### スキニングの実行場所: GPU (compute or vertex 内) を推奨

エンジンの核心が bindless + compute なので、スキニングは頂点シェーダ内 (storage buffer からジョイント行列を読む) が設計に素直。

1. **データ**: メッシュに JOINTS_0 (u8/u16×4) + WEIGHTS_0 (float×4) 属性を追加ロード。skin の joints (ノード index 列) + inverseBindMatrices。
2. **ジョイント行列の計算 (CPU)**: 毎フレーム、アニメ済みノード階層から `jointMatrix[i] = inverse(meshNodeWorld) * jointWorld[i] * inverseBind[i]` を組んで GpuBuffer (bindless) にアップロード。Transform 伝播は ECS に既にある — glTF ノード階層を ECS エンティティに写しているならそれを流用、独自階層なら glTF 側で解決。
3. **シェーダ**: scene_pbr_lite を拡張 (または `scene_pbr_skinned` を追加): 頂点で `pos = Σ w_k * jointMatrix[j_k] * pos` (法線は 3x3)。ルート引数にジョイントバッファの bindless index + 頂点あたり joints/weights の SoA バッファ index。**Write のないパスはデッドパスカリングされる**規約に注意。
4. **morph**: target 差分 (POSITION/NORMAL) を storage buffer に置き、頂点で `pos += Σ weight_t * delta_t[vertex]`。weights は Track (Player) から毎フレーム root 引数 or 小バッファへ。target 数は v1 では 8 個程度まで対応で十分。

### アニメーション IR への接続

- glTF animation の channel target が joints のとき: 既存 T/R/S Track がジョイントノードに当たるだけ (階層が写せていれば追加実装は薄い)。
- morph weights channel: `float[]` Track を IR に足す (無ければ)。CUBICSPLINE 補間の対応状況を確認、未対応なら LINEAR のみで開始し Docs に明記。

## 作業ステップ

1. 調査: Luxel.Gltf の skin/morph パース状況、Animation IR の Track 型、SceneRenderExtractor と scene_pbr_lite の受け渡し (indexBufIndex/instanceStart の前例)。
2. skin データのロード + ジョイント行列 CPU 計算 + 単体テスト (**GPU 不要でここまでテスト可**: 既知ポーズのジョイント行列を数値 assert。2 ボーンの手作り glTF を fixture に)。
3. スキニングシェーダ + 描画統合。vk/dx 両方でピクセル一致 (エンジンの規律)。
4. morph 同様 (差分バッファ + weights)。
5. デモ: 3D/GltfSkinned ストーリー — CC0 のスキン付きモデル (Khronos サンプルの RiggedSimple / AnimatedMorphCube が小さくて定番) を assets に追加。play は固定 dt で数フレーム Step → Snap (決定的)。
6. Docs/Motion (src/Luxel.Gallery/Stories/Docs/DocsMotion.cs) の「skin/morph は将来」を更新。

## 罠・注意

- **vk/dx ピクセル一致の規律**: シェーダは Slang 1 ソース、`g_buffers[]` bindless 規約 (生 64bit ポインタは DXIL 非対応)。行列の row/column major を既存シェーダに合わせる。
- テクスチャ・バッファのアップロードは 256B 行整列等の既存規約 (幅 64 倍数) — 頂点 SoA には関係ないが footprint 系 API を触るなら注意。
- glTF の joints は「skin ローカルの index」→ ノード index の間接参照。inverseBindMatrices の accessor は疎らな場合もある (無ければ単位行列)。
- スキン付きメッシュの AABB はアニメで動く — カリングやレイキャストがあるなら保守的に (v1 はバインドポーズ AABB × 余裕係数で可)。
- 新アセットを assets/ に追加する際は golden のライフサイクルと切断された場所に (goldens/ に画像 fixture を置かない — 前例あり)。

## スコープ外

- アニメーションブレンド/ステートマシンとの高度な統合 (Graph/StateMachine は既存 — 接続は動いてから)、GPU スキニングの compute プリパス化 (頂点内で足りる)、モーフ法線の高精度化。
