# RetainedCanvas 増分更新計画 (IC: Incremental Canvas)

目標: **Content 差し替えとノード増減を「フル再構築」から「変わった分だけの GPU 書き込み」へ**。
毎フレーム動くコンポーネント (Sparkline/ライブ波形/エディタのタイプ) が、画面全体の再エンコード +
全バッファ再アップロードを引き起こす現状を解消する。

保持型 UI ツリー導入時 (2026-06-29) に「構造変更はフル再構築、増分 free-list は将来」と
据え置いた宿題の回収。EX-M2c (widget 層の部分 Realize) で widget 層は部分化されたが、
**canvas 層が最後の O(シーン全体) ボトルネック**として残っている。

---

## 現状の問題 (詳細)

### 更新の 3 クラスと現状の扱い

| 変更 | 現状 | コスト |
|---|---|---|
| Transform / Color / Opacity / Clip | **部分更新** (slot への in-place 書き込み、opacity はサブツリー伝播) | O(変更ノード) ✓ |
| Visible 切替 | **部分更新** (order バッファのみ再構成) | O(パス数) ✓ |
| **Content 差し替え** | `Invalidate()` → **フル再構築** | **O(シーン全体)** ✗ |
| **ノード増減** (AddChild/Remove) | `_dirtyStructure` → **フル再構築** | **O(シーン全体)** ✗ |
| Z 変更 | `_dirtyStructure` → **フル再構築** | 実際は order のみで済む ✗ |

### フル再構築 (`Rebuild`) が毎回やること

1. **CPU 再エンコード**: ツリー全体を DFS し、全ノードの World/EffectiveOpacity/slot を再割当、
   **全ノードの Scene2D を `PathEncoder.Encode` で線分化し直す**。エディタのテキストは
   グリフ輪郭 = パスなので、画面に文字が多いほど線分数が膨らむ (数千〜数万 GpuSegment)。
2. **GPU バッファ 6 本を破棄して作り直す**: `_seg/_path/_tf/_sty/_clip/_orderBuf` を毎回
   `Dispose()` → `Malloc(HostMapped)` → 全量 memcpy。バッファが変わるので **bindless index も
   毎回変わり**、ルート引数も更新される。アロケーション churn がフレーム毎に発生する。
3. **GC 圧**: `List.Clear + AddRange` の再充填、`ToArray()` ×6、`SortedChildren` の
   LINQ `OrderBy` がノード毎に allocator を回す。

### なぜ Content 差し替えがフル再構築になるのか

- `UiNode.Content` の setter は **dirty を一切マークしない** (Transform/Color と非対称)。
  コントロール側が `node.Content = scene; canvas.Invalidate();` を対で書く規約になっており、
  `Invalidate()` = `_dirtyStructure = true` = フル再構築しか経路がない。
- SoA レイアウト上、ノードの線分は `_segments` 内の**連続レンジ** (`GpuPath.SegStart/SegCount`) を
  占める。差し替え後のサイズが変わると隣を押し出すため、「詰め直し = 全再構築」で逃げていた。

### 実害 (今このコードベースで起きていること)

- **ライブ波形 1 本で毎フレーム全再構築**: LiveCodeBlock の Sparkline は Tick 毎に
  `SetValues` → `Content` 差し替え → `Invalidate()`。エディタ全文 + テーブル + 画像を含む
  シーン全体が**毎フレーム再エンコード + 全アップロード**される。sparkline 自体は
  ~100 線分 (3KB) なのに、書き込みは数千線分 + バッファ再確保。
- **エディタのタイプ 1 打鍵も全再構築**: TextArea/RichTextEditor の Refresh は編集ブロックの
  ノードだけ Content を差し替える (widget 層は部分更新済み) が、canvas 層で全再構築に落ちる。
  ED-M5 の性能ゲートは「モデル層で編集ブロック以外の Version 不変」を担保したもので、
  canvas 層のこのコストは既知のまま据え置いていた。
- **EX-M2c の部分 Realize も canvas 層では全再構築**: embed 再ホスト/ListView SetItems は
  ノード増減なので `_dirtyStructure`。widget 層で絞った意味が canvas 層で薄まる。
- DevTools のように**グラフが多数常時動く画面**では、フレーム毎の再構築が固定費になる。

### すでにあるもの (この計画が乗る土台)

- dirty 種別ごとの部分更新パス (transform/style/clip/order) と DFS 書き込み。
- 計測カウンタ: `LastTransformWrites/LastStyleWrites/LastSegmentBytesWritten/LastWasFullRebuild/
  LastOrderWrites` + `DiagFlush` (DevTools の Flush パネル/GPU パネルに表示済み)。
- バッファは HostMapped (staging 不要、Span 書き込み) — in-place 更新と相性が良い。
- `GpuPath` はローカル bbox を持つ (エンコード時計算) — in-place 書き換えで完結する。
- `GpuSegment.PathId` は線分→パスの逆参照 — レンジ移動時に線分側も書き直す必要がある (注意点)。

---

## 設計方針

**「slot は据え置き、レンジは容量付き」**。ノードの線分レンジに容量 (capacity) を持たせ、
収まる差し替えは in-place 書き込み、伸びたら末尾へ追記して旧レンジを空きにする。
空きが閾値を超えたら従来のフル再構築 = コンパクション。つまり **フル再構築は「まれな
アロケータのコンパクション」に降格**し、定常フレームは O(変わったノード) になる。

undo 不要・順序はパス slot でなく order バッファが持つ、という既存構造をそのまま使う:
- パス slot の中身を書き換えても order は不変 (同じ slot を指し続ける)。
- パス数が変わるときだけ order 再構成 (`RebuildOrder` は既にある軽量パス)。

---

## マイルストーン

### IC-M0: 計測ベースライン — **完了 (2026-07-03)**

- **計測基盤**: RetainedCanvas に累積統計 (`TotalFlushes/TotalRebuilds/TotalRebuildMicros/
  LastRebuildMicros/TotalUploadBytes/SegmentCount/PathCount` + `ResetStats`)。
  Gallery に **bench モード** (`-- vk|dx bench <story> [frames] [--type] [--click x y]`) —
  snap と同じ offscreen 決定駆動で N フレーム回し区間統計を出力。IC-M4 の回帰ゲートに再利用する。
- **ベースライン (Debug, RTX 4080 SUPER, 300 フレーム)**:

  | シナリオ | scene (seg/path) | フル再構築 | 再構築 CPU | アップロード | マネージド確保 |
  |---|---|---|---|---|---|
  | LiveCode 再生 (vk) | 8,063 / 189 | **300/300 = 100%** | avg **3.61 ms** | 268 KB/フレーム = **15.7 MB/s** | **2.16 MB/フレーム**, gen0 ×11 |
  | LiveCode 再生 (dx) | 同上 | 100% | 3.63 ms | 同上 | 同上 |
  | TextArea タイプ連打 (vk) | 18,962 / 404 | 100% | 3.47 ms | 400 KB/フレーム = 23.4 MB/s | 2.72 MB/フレーム, gen0 ×16 |
  | Embeds タイプ連打 (vk) | 21,590 / 495 | 100% | **3.66 ms** | **504 KB/フレーム = 29.5 MB/s** | **3.54 MB/フレーム**, gen0 ×21 |
  | Embeds 静止 (対照) | 8,915 / 196 | **0** (Step が描画スキップ) | 0 | 0 | ~0 |

  読み: 動く要素が 1 つでもあると毎フレーム 100% フル再構築で、**CPU ~3.5 ms/フレーム +
  0.3〜0.5 MB/フレームの GPU 書き込み + 2〜3.5 MB/フレームの GC 圧**を払っている。
  変更自体は波形 1 ノード (~3 KB) やタイプ 1 ブロック分でしかない。静止シーンの対照が 0 で
  あることから、コストは全て「Content 差し替え → フル再構築」経路に由来する。
  DevTools シナリオは「動くグラフ複数 = LiveCode と同クラス」なので個別計測は省略。
- ゲート: IC-M2 完了時に上記 bench で「定常フレームの再構築 0 回・アップロード = 変更ノード分
  (LiveCode で ~3 KB/フレーム オーダー)」。

### IC-M1: Content 差し替えの in-place 部分更新 (同容量以内) — **完了 (2026-07-03)**

**結果 (IC-M0 と同条件の bench)**:

| シナリオ | フル再構築 | 再構築 CPU | アップロード | マネージド確保 |
|---|---|---|---|---|
| LiveCode 再生 (vk) | 300→**0** | 1084ms→**0** | 78.6MB→**3.6MB** (268→**12.3 KB/フレーム** = 波形ノード分のみ) | 2.16→1.03 MB/フレーム |
| LiveCode 再生 (dx) | 同上 0 | 0 | 同上 | 同上 |
| タイプ連打 (TextArea/Embeds) | 変化なし (100%) | — | — | — (予定どおり: グリフ数変化でパス数が変わり fallback → IC-M2/M3) |

snap 48/48 **ピクセル不変** (vk/dx) — warmup 中の波形更新は in-place 経路を通っており、
既存 golden との一致が正しさの証明になっている。実窓 E2E: アニメのフレーム差分 + タイプ描画 OK。
実装メモ:
- `UiNode.Content` setter が `MarkContentDirty` (Transform/Color と対称)。`SegStart/SegCapacity` を
  Rebuild で記録し、**新パス数 == PathCount かつ 新線分数 ≤ SegCapacity** なら線分 + パス slot を
  in-place 書き込み (`TryUpdateContentInPlace`) — order/他バッファ/bindless は不変。
  条件を満たさないノードが 1 つでもあれば従来のフル再構築へフォールバック。
- **`GpuSegment.PathId` はシェーダ未使用と確認** (raster2d_fine は path.SegStart/SegCount で走査) —
  in-place 書き込みに整合の追加作業なし。
- **手動 `Canvas.Invalidate()` を 10 箇所すべて削除** (TextField/TextArea/Text/TableBlock/Sparkline/
  Select/RichTextEditor×2/ColorPicker/ImageView) — AddChild/Remove は自動マークなので全箇所冗長だった。
  `Invalidate()` は「明示的な全再構築の脱出口」として存置 (doc 更新済み)。
- `UiNode.Z` setter を structure → **order dirty** に降格 (BuildOrder が Z を見るので order 再構成で正しい)。
- カウンタ `LastContentWrites` 追加、DevTools GPU パネルに `update content ×n` 表示。
- テスト 376 / snap 48/48 (vk/dx)。

**設計 (実装済みの内容):**

- `UiNode.Content` setter が `MarkContentDirty(this)` する (**Transform/Color と対称に**)。
  ノードに `SegStart/SegCapacity/PathStart/PathCount` を記録 (PathStart/Count は既存)。
- `Flush`: `_dirtyContent` のみなら各ノードを再エンコードし、
  **新線分数 ≤ SegCapacity かつ 新パス数 == PathCount** なら:
  - 線分を同レンジへ Span 書き込み (PathId 込み)、
  - パス slot を in-place 書き換え (SegCount/bbox/Kind/StrokeHalfWidth/image メタ)、
  - order/他バッファ/bindless index は**不変**。`LastContentWrites`/`LastSegmentBytesWritten` を更新。
  収まらなければ従来どおり `_dirtyStructure` へフォールバック (IC-M2 で解消)。
- **コントロールの手動 `Invalidate()` を一掃**: AddChild/Remove は元々 structure を自動マーク
  するので、setter が content をマークすれば手動呼び出しは全箇所冗長になる。残すとフル再構築を
  強制して IC-M1 を無効化するため、呼び出し側 (Sparkline/ListView/TableBlock/TextArea/
  RichTextEditor/TextField/ImageView 等) から機械的に削除。`Canvas.Invalidate()` 自体は
  「明示的な全再構築要求」として存置。
- 快速勝ち: `UiNode.Z` setter を `MarkStructureDirty` → `MarkOrderDirty` へ (BuildOrder が
  SortedChildren を見るので order 再構成だけで正しい)。
- **Sparkline はこれだけで完了**: 点数一定 → 容量常に一致 → 毎フレーム = 波形ノードの
  線分 memcpy のみ。

### IC-M2: 伸長対応 — **完了 (2026-07-03)。設計を「末尾追記+free-list」から「ノード毎スラック」へ変更**

計画時の free-list 案より単純な **per-node 容量スラック**で同じ目標を達成した:
- Rebuild が各ノードの線分レンジに +25% (最低 8)、パス slot に +25% (最低 2) の予約を足す。
  **order は実数 (PathCount) しか参照しない**ので、予約は GPU メモリを +25% 使うだけで
  per-pixel コスト (order 走査) は増えない。
- `TryUpdateContentInPlace` の条件を「パス数不変」→「**予約容量以内**」へ拡張。パス数が変わった
  ときだけ order を再構成 (軽量。パス数不変なら order も不変)。縮んだ分の旧 slot は中立化。
- order バッファも容量つき (×1.5) の in-place 書き込みへ — Visible 切替/パス数変化で
  バッファ再確保しない。
- 予約超え = 従来のフル再構築 (新しいスラックで再割当) — **フル再構築が「まれな
  コンパクション」に降格**という設計方針そのもの。free-list/断片化管理は不要になった
  (レンジはノードに固定で断片化しない)。

**結果 (bench, Debug)**:

| シナリオ | フル再構築 | 再構築 CPU | 備考 |
|---|---|---|---|
| TextArea タイプ連打 | 300 → **9 (3%)** | 1,010ms → **28ms** | 再構築はブロックがスラックを超えたときだけ (~33 打鍵に 1 回、償却) |
| LiveCode 再生 | **0** (IC-M1 のまま) | 0 | スラックで scene は 8k→10k segs (+28%、GPU メモリのみ) |
| Embeds タイプ連打 | 変化なし (100%) | — | RichTextEditor.RenderBlock が色ノードを作り直す = ノード増減 → IC-M3 |

snap 48/48 ピクセル不変 (vk/dx)、テスト 376。
既知の残コスト: in-place はノード単位の再エンコードなので、1 ブロックが巨大化すると
書き込み量はブロックサイズに比例する (TextArea の 1 段落 300 文字連打で avg 302KB/flush)。
実文書ではブロックが分かれるので実害は小さい — 必要になればコントロール側の分割で対応。

- セグメントバッファに**容量スラック**を持たせる (Rebuild 時に総量の +50% 等で確保、
  `_segUsed` カーソル管理)。伸びたノードは末尾へ新レンジを確保して書き、旧レンジは
  free-list へ (サイズ別ビン or 単純リスト)。新規確保は free-list 優先。
- パス数が変わる場合: 新パスを path バッファ末尾へ追記し (こちらも容量スラック)、
  旧 slot は `SegCount = 0` で中立化 + `RebuildOrder` (order は軽量再構成)。
  `GpuSegment.PathId` は新 slot で書く。
- **コンパクション**: 空き率 > 50% またはバッファ容量不足で従来の `Rebuild` を 1 回走らせ
  詰め直す (バッファは ×1.5 成長)。頻度は償却されて稀。
- これで**エディタのタイプ 1 打鍵**も「編集ブロックのノード分の書き込み + (行数変化時のみ)
  order 再構成」になる。
- order バッファも Dispose/Malloc をやめ容量つき in-place 書き込みへ (ついで)。

### IC-M3a: RichTextEditor の色ノード再利用 — **完了 (2026-07-03)**

Embeds タイプが 100% 再構築のままだった原因は canvas でなく**コントロール側**:
`RenderBlock` がキーストローク毎に色ノードを Remove/AddChild で作り直し structure dirty に
落としていた。**カーソル式のノード再利用**に変更 — 必要ノード列 (装飾/マーカー/色別テキスト) を
既存ノードへ順に上書き (Z/Color/Content 再設定)、足りなければ追加・余れば末尾を除去。
ノード数が変わらないキーストロークは Content 差し替えだけになり IC-M1/M2 の in-place が効く。
- 結果: **Embeds タイプ連打 = 再構築 300 → 9 (3%)、再構築 CPU 1,137ms → 37ms**。
  TextArea/LiveCode は据え置き (9/0)。snap 48/48 ピクセル不変 (vk/dx)、テスト 376。
- E2E (実窓): タイプ → "- " オートフォーマット (ListItem 化 = マーカーノード追加) →
  Ctrl+Z で段落復帰 (ノード減) — ゴーストなく描画健全。複数行ドラッグ選択 (選択矩形の
  パス数変化 = スラック内 in-place) も健全。
- あわせて空ノードのスラック下限を引き上げ (線分 8→16、パス 2→4) — 選択ハイライト等の
  0→数個の変化を in-place で受ける。

### IC-M3: ノード増減の増分化 (structure の降格) — **必要になったら (格下げ)**

M3a の結果、フル再構築が残るのは**低頻度のユーザー操作だけ**になった: Enter によるブロック
増減 (RebuildBlocks)、embed 差し替え/再ホスト、ListView SetItems、ブロックのスラック超過。
いずれも「1 操作 1 回 ~3.5ms」でありフレーム毎ではない — 60fps を害さない。
全バッファの末尾容量 + slot free-list という大きな手術に見合う恩恵が現状ないため、
大規模シーン (数万ノード) で Rebuild 自体が 1 フレーム予算を超える段階まで保留する。
(以下は当初設計のまま参考として残す)

- `AddChild`: 新ノードの transform/style/clip/パス/線分を各バッファ末尾 (or free-list) へ
  追記し、`RebuildOrder`。フル再構築しない。
- `Remove`: サブツリーの slot 群を free-list へ返し、パスを中立化、`RebuildOrder`。
- transform/style slot にも free-list (16B/32B 固定なので単純)。クリップは共有が絡むので
  v1 は「増減があったフレームはクリップのみ再構築」でも可 (数が少ない)。
- 効果: **EX-M2c の部分 Realize / ListView SetItems / embed 再ホストが canvas 層でも
  O(変更サブツリー)** になり、widget 層の部分化と対称になる。
- `SortedChildren` の LINQ を in-place ソート/キャッシュに置換 (Rebuild/BuildOrder の GC 減)。

### IC-M4: 検証 + 性能ゲート — **実質完了 (各マイルストーンで随時実施)**

- bench (`-- vk|dx bench`) が回帰ゲート: LiveCode 0 再構築 / TextArea・Embeds タイプ 3% (償却)。
- snap 48/48 ピクセル不変 (vk/dx) を全マイルストーンで維持 — warmup が in-place 経路を通るので
  golden 一致が正しさの証明。テスト 376。実窓 E2E (タイプ/オートフォーマット/undo/ドラッグ選択/
  アニメ)。DevTools GPU パネルに `update content ×n` 表示。
- 最終結果まとめ (Debug, 300 フレーム, vk):

  | シナリオ | フル再構築 (前→後) | 再構築 CPU (前→後) | アップロード (前→後) |
  |---|---|---|---|
  | LiveCode 再生 | 100% → **0%** | 1,084ms → **0** | 78.6MB → **3.6MB** |
  | TextArea タイプ | 100% → **3%** | 1,010ms → **30ms** | 117MB → 89MB* |
  | Embeds タイプ | 100% → **3%** | 1,137ms → **37ms** | 148MB → 82MB* |

  *タイプはキャレットブロック全体の再エンコード書き込みが残る (ブロックサイズ比例、実文書では小)。

(旧計画のゲート記述は上記で満たした)

- モデルテスト (GPU 不要な範囲はエンコーダ/レンジ管理を分離してテスト):
  同容量 in-place / 伸長 → 追記 / 空き再利用 / コンパクション閾値 / Z=order のみ。
- GPU 検証: 既存サンプル 09 (保持型) に content 差し替えケースを追加 —
  「SetValues 相当で `LastWasFullRebuild == false` かつ `LastSegmentBytesWritten ==
  そのノードの線分バイト数`」。vk/dx 両方。
- snap 48/48 **ピクセル不変** (描画結果は同一でなければならない)。
- E2E (実窓): LiveCode 再生中の DiagFlush で再構築 0/秒、エディタタイプ連打で同様。
  DevTools GPU パネルが `update segments ×n` を表示。IC-M0 のベースラインと比較を記録。

---

## リスクと対処

- **GPU 読み取り中の HostMapped 書き込み**: 既存の transform/style 部分更新と同じモデル
  (フレームループは Flush → Dispatch → 待機が直列)。新たなハザードは増えない。
  マルチウィンドウ (WindowManager) も canvas 毎に直列。
- **`GpuSegment.PathId` の整合**: レンジ移動時は線分を必ず再書き込みする (再エンコード済み
  データを書くので自然に満たされる)。in-place でも PathId を含めて書く。
- **断片化の暴走**: コンパクション閾値 (空き率) + 成長率 ×1.5 で償却。DiagFlush に
  空き率カウンタを足して DevTools で観察可能にする。
- **手動 Invalidate の残骸**: 1 箇所でも残るとそのフレームはフル再構築に落ちて気づきにくい。
  IC-M1 で `Invalidate()` に Debug ログ (診断イベント) を仕込み、定常フレームで呼ばれて
  いないことを E2E で確認する。
- **SurfaceView (子 canvas)**: 子 RetainedCanvas も同じ機構がそのまま効く。追加作業なし。
- **回転クリップ/Z 大量変更**等のエッジ: 既存セマンティクス (AABB クリップ、Z は order) を
  変えない。挙動が変わる最適化はしない。

## やらないこと (スコープ外)

- タイルビニング/解析的 AA などラスタライザ本体の高速化 (別課題として既録)。
- シェーダ変更 — SoA レイアウト/RasterArgs は不変で、CPU 側の更新粒度だけを変える。
- keyed reconciler (widget 層の話。canvas 層はレンジ管理で十分)。
