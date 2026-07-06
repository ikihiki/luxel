# 05 — Physics: メッシュ/凸包コライダー

## 概要

glTF などの実メッシュ形状で衝突できるようにする: 静的地形は Bepu の `Mesh` (三角形スープ)、動的剛体は `ConvexHull`。現状はプリミティブ形状 (箱/球など) のみで、実アセットの地形・小物と物理を組み合わせられない。

## 背景と現状

- **ロードマップの記載場所**: [src/Luxel.Gallery/Stories/Docs/DocsGpu.cs](../src/Luxel.Gallery/Stories/Docs/DocsGpu.cs) の Docs/Physics「ロードマップ (v1 スコープ外)」節、項目 3。着手前に必読。
- **Luxel.Physics**: src/Luxel.Physics/ (PhysicsWorld / Components / PhysicsStepSystem / Callbacks)。形状はコンポーネント (RigidBody 等) の記述子から Bepu shape を作っているはず — 生成箇所を読んで、shape 種別を増やす形にする。
- **メッシュデータの供給元**: Luxel.Gltf (glTF ロード) / Luxel.Assets / Luxel.Resources (リソース DAG)。3D デモは MeshRef コンポーネント + IRenderExtractor で描画している (KnockdownStory / glTF デモ参照)。**描画用メッシュデータ (頂点/インデックス) を物理用に取り出す口**がどこにあるか最初に調査すること — CPU 側に頂点が残っているか、GPU アップロード後に捨てているかで設計が変わる。

## 実装方針

### 1. 静的メッシュ (地形) — Bepu `Mesh`

- Bepu の `Mesh` は三角形配列 + `BufferPool` から構築 (`new Mesh(triangles, scale, pool)`)。三角形は `Triangle` struct。
- コンポーネント案: `StaticMeshCollider { MeshSource }` — MeshSource は「頂点+インデックスの CPU 配列」への参照 (glTF ロード結果 or プロシージャル)。リソース DAG を通すなら (型,uri) ノードとして `PhysicsMesh` 型を足し、glTF → 三角形抽出ステップを 1 つ書く (Docs/Resources のステップ規約に従う)。最小実装は「CPU 配列を直接渡すファクトリ」からで良い。
- 登録は Statics (`Simulation.Statics.Add`)。

### 2. 動的凸包 — Bepu `ConvexHull`

- `ConvexHullHelper.CreateShape(points, pool, out center)` で頂点群から凸包を生成。**戻りの center オフセットに注意**: Bepu は重心原点に平行移動した形状を返すため、描画メッシュとの位置合わせに center 分の補正が要る (RigidBody の pose とレンダリング transform の対応に写す)。
- コンポーネント案: `RigidBody.DynamicHull(points, ...)` 相当のファクトリ追加。
- 凹メッシュの凸分解は**やらない** (ロードマップにも「凹メッシュは凸分解が必要」とだけ記載 — v1 スコープ外として Docs に明記)。

### 3. テスト + デモ + Docs

- 単体テスト (GPU 不要): ①プロシージャルな三角形スープ (例: 波打つ地形 4×4 グリッド) に球を落とし、固定 dt で静定 → y が地形の高さ近傍で止まる。②四面体の頂点群から ConvexHull を作った剛体が床で静定 → 貫通していない。
- デモ: glTF 地形 (既存 glTF デモのアセットを流用可) + 球を落とす「3D/PhysicsMesh」ストーリー。初期静止 → クリックで開始 (snap 決定的の定石)。
- Docs/Physics: ロードマップ節から項目 3 を削除し、「メッシュコライダー」節 (静的 Mesh / 動的 ConvexHull の使い分け、凸分解はスコープ外の旨) を追加。

## 作業ステップ

1. 調査: shape 生成箇所 (Components → Bepu shape の変換) と、glTF ロード結果から CPU 頂点/インデックスを取れるかを確認。
2. StaticMeshCollider (三角形スープ直渡し) + テスト。
3. ConvexHull ファクトリ + center 補正 + テスト。
4. glTF からの三角形抽出 (ヘルパー or リソースステップ) + デモストーリー + play + golden。
5. Docs 更新。

## 罠・注意

- Bepu の `Mesh` は BufferPool のメモリを保持する — PhysicsWorld の Dispose で解放する経路を確認 (リークしがち)。
- glTF の座標系/スケール: SceneRenderExtractor が使っている変換と同じものを物理にも適用しないと、絵と当たりがずれる。
- Mesh (静的) vs Mesh (動的) — Bepu で動的メッシュは非推奨 (重い + 品質問題)。動的は ConvexHull のみサポートとし、Docs に明記。
- 3D golden の再現性: 物理を含む play は「最初の入力まで停止」+ 固定 1/120 蓄積器 (KnockdownStory の定石) を踏襲。

## スコープ外

- 凸分解 (V-HACD 等)、Compound shape (ロードマップ項目 5)、キャラクターコントローラ (項目 4)。
