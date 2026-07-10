# 28 — dotnet test の E2E アダプタが Gallery ランナーと乖離 (golden 2 件 + stale 1 件)

## 概要

正規ゲート `dotnet run --project src/Luxel.Gallery -- vk e2e` は全緑 (116 plays, diff 0) なのに、`dotnet test` (tests/Luxel.E2e.Tests の E2ePlayTests) では **同じ golden 比較が 2 件落ちる**。アダプタ経路の描画がランナーとどこかで食い違っている。加えて stale golden が 1 件。

**発見**: 2026-07-10、Q45 (GE-0) 作業中。クリーンな HEAD (b0b8d2c) で再現することを確認済み — 現行の変更とは無関係。

## 症状 (再現手順つき)

1. `dotnet test tests/Luxel.E2e.Tests` (フルスイート) →
   - `Play(story: "Reference/Overview")` — `golden 差分: Reference_Overview.table.vk.actual.png`。**単独 (`--filter`) でも落ちる** = 決定的。actual と golden は目視でほぼ同一 (数 px 級の差)
   - `Play(story: "Docs/Strudel")` — `golden 差分: Docs_Strudel.vk.actual.png`。**単独では通り、フルスイートでのみ落ちる** = 走行順依存
2. `dotnet run --project src/Luxel.Gallery -- vk e2e` → 両ストーリー含め **diff 0 で全緑**

## 調査の当たり

- **Reference/Overview.table (決定的)**: アダプタ (E2ePlayTests fixture) とランナーで描画環境の何かが違う — フォント供給 (LoadBundled の解決パス)、DPI/サーフェス生成、GPU デバイス選択あたりから二分探索。actual.png はテスト実行後に `src/Luxel.Gallery/goldens/` に残るので、golden とのピクセル diff を取って差の場所 (テーブルのどの行/文字か) を特定するのが早い
- **Docs/Strudel (順依存)**: Strudel の `Session` が process-wide static でサイクル位置が走行順に依存する既知の性質 (Q30b の知見) が疑わしい。フルスイートで先行テストが Session を進める → Docs/Strudel のデモブロック描画が変わる、の線。ランナーは 1 プロセス 1 順序で安定している。対策候補: Docs/Strudel の snap 対象から順依存の要素を外す / play 冒頭で Session をリセットする口を作る
- **stale golden**: `Demos_Strudel_Repl.playing.vk.png` はどの play も生成しない (ランナーが毎回 STALE 警告)。生成していた play が Q30b の Repl 移行で snap を止めた名残。**削除するか、決定的に snap できる形で play を復活するか**を決めて片付ける

## 完了の定義

- `dotnet test tests/Luxel.E2e.Tests` フルスイートが GPU ありで全緑 (ランナーと同じ結果になる)
- STALE 警告ゼロ
- 原因と対策を Docs/Contributing の e2e 節に 1 行追記 (アダプタとランナーの環境差があるなら「何を揃えているか」)

## 罠

- golden を安易に --update しない — ランナー側は緑なので、update するとランナーが割れる (両者の描画が一致しない限り単一 golden は両立しない)。**先に描画環境を一致させる**
- Docs/Strudel の順依存は「テストの並び替え」で隠さない (xunit の並列/順序は環境で変わる)
