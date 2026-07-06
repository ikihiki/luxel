# 12 — メンテ: Docs の stale 記述修正 + dx golden 更新

## 概要

調査 (2026-07-06) で見つかった小粒の不整合 2 件。30 分〜1 時間級。他タスクの「ついで」に混ぜず、単独の小コミットで片付けるのに向く。

## A. Docs/Editor の CodeEditor ロードマップ記述が古い

- **場所**: [src/Luxel.Gallery/Stories/Docs/DocsText.cs](../src/Luxel.Gallery/Stories/Docs/DocsText.cs) の CodeEditor 節。「v2 送り」リストに**検索置換**が入ったままだが、E3b (commit 635d961) で実装済み。行操作 (複製/コメントトグル/行移動、E3a) も同様に実装済みなので、リストに残っていれば外す。
- **正しい現状 (2026-07-06 時点)**:
  - 実装済み: ガター / 現在行 / トークン色 / 補完ポップアップ (Ctrl+Space) / 診断波線 / ホバー / 行操作 (Ctrl+D 複製・Ctrl+/ コメント・Alt+↑↓ 移動) / 検索置換 / スクロールバー
  - 未実装 (v2 送り): マルチカーソル・矩形選択 (E3.5 として意図的延期) / ミニマップ / 折りたたみ / 複数ファイルタブ / git 差分ガター / スニペット / フォーマッタ
- 修正後、そのページの golden が変わるか確認 (本文変更が初期ビューポートに映る場合のみ再撮影が必要): `dotnet run --project src/Luxel.Gallery -- vk e2e "Docs"` → 差分が出たページだけ `--update`。
- ついでに Docs 全ページを対象に、実装済み機能が「将来」扱いのままの箇所がないか軽く grep (「スコープ外」「将来」「未実装」で検索し、CodeEditor/Scripting/Strudel 関連だけ目視確認) すると良い。

## B. dx (D3D12) golden の未更新分

- **経緯** (メモリ記録より):
  1. golden を画像リソースとして参照するストーリー (DocsMeta / DisplayControl / TextControl / GpuStories の SampleImage) の参照先を改名した際、vk は更新したが **dx golden 4 枚 (Authoring / TexturedQuad / ImageBlock / MarkdownEditor_Embeds 相当) が古いまま**。
  2. snap → E2E (play) 移行時、**対話 play の dx golden が未生成** (vk のみ生成した)。
  3. ※その後もサンプル画像 fixture は goldens/ から assets/sample-sparkline.png へ分離済みなど状況が動いている — まず現状確認から。
- **手順**:
  1. 現状確認: `dotnet run --project src/Luxel.Gallery -- dx e2e` を実行し、FAIL と「golden が存在しない play」を列挙。STALE (どの play も生成しない golden) も出るので合わせて確認。
  2. `dotnet run --project src/Luxel.Gallery -- dx e2e --update` で更新・生成。
  3. `--update` は**全 golden を再エンコードする** — `git diff --name-only -- goldens` で差分を確認し、意図分 (今回の dx 分) 以外を `git checkout --` で戻す。ピクセルが実際に変わったものだけ残す。
  4. 再実行して dx e2e 全緑 + `git status` がクリーン (意図分コミット後) を確認。vk 側も `-- vk e2e` で無傷を確認。
- **前提**: D3D12 で動く GPU (検証機は RTX 4080 SUPER)。tools/slang/bin/ に dxcompiler.dll / dxil.dll (DXIL 出力に必須)。

## 検証

- `dotnet run --project src/Luxel.Gallery -- vk e2e` / `-- dx e2e` 両方 PASS、STALE なし。
- `dotnet test` 無傷。
