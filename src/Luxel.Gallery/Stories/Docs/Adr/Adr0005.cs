using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("ADR/0005-Docs-In-Gallery", Order = 76)]
    public static Widget Adr0005(StoryContext ctx) => DocNew(ctx, $$"""
        # ADR-0005 — ドキュメントとサンプルは Gallery に一本化する

        - **Status**: Accepted
        - **Date**: 2026-07-04
        - **Deciders**: ikihiki

        ## Context

        当初、ドキュメントはリポジトリの `docs/` (計画 MD 17 本)、サンプルは `src/Luxel.Samples` (CLI サンプル群) に分かれていました。ここに次の力学が働いていました:

        - **乖離** — MD のコード例・API 説明は実装の変化に追従せず、静かに古びる。CLI サンプルも回帰テストに乗っておらず、壊れても気づけない
        - **重複** — 同じ機能の説明が計画 MD・サンプル・Gallery のストーリーに三重化し、どれが正か曖昧になる
        - 一方で Gallery には**ストーリー = 実行可能な実例 + snap/e2e 回帰 + ソース焼き込み (StorySource)** という基盤がすでにあり、docs ページ (markdown + ライブ UI 埋め込み) の仕組みも育っていた

        ## Decision

        `docs/` と `Luxel.Samples` を**完全削除**し、ドキュメント・サンプル・回帰テストのすべてを **Luxel.Gallery に一本化**します:

        - **ドキュメント** = Gallery の Docs 章 (`Stories/Docs/Docs*.cs`) — markdown + hole によるライブ UI 埋め込み。計画 MD の「設計ノート」は現在形の仕様として吸収する
        - **サンプル** = 説明的デモストーリー — `StoryRef` で docs に埋め込み、`StorySource` (ジェネレーターがソースを焼き込む) で手書きコピーの乖離を根絶する
        - **API リファレンスは実行時生成** — `[UiComponent]` 由来のコントロール API と、`GenerateAssemblyApi` が XML doc ごと焼き込む型 API (Reference 章)。手書き一覧は作らない
        - **検証を docs にも敷く** — 起動時のデッドリンク検証 (`story:` / `#アンカー`)、play + golden の E2E 回帰、bench。docs ページも「テストされる成果物」にする
        - 今後の**計画文書はリポジトリに置かない** — `docs/` を復活させない

        ## Alternatives

        - **`docs/` の markdown を維持** — GitHub 上で読める利点はあるが、コード例の乖離・リンク切れ・実行不能という核心の課題が残る → 却下
        - **静的サイトジェネレーター (DocFX / Docusaurus 等)** — 別ツールチェーンの保守が増える上、ライブ UI 埋め込み (本物のカウンタ・knobs・GPU デモ) ができない。エンジンのドキュメントスタック (RichTextEditor / mermaid / 数式) のドッグフーディング機会も失う → 却下
        - **XML doc コメントのみ** — API 表は賄えるが、章立てのガイド・設計解説・動く実例は表現できない → 却下 (XML doc は Reference 章の材料として併用)
        - **Luxel.Samples を回帰テスト化して存続** — テストを敷いてもストーリーとの重複は残る。「実例は全部ストーリー」に寄せる方が一本化される → 却下

        ## Consequences

        - ✅ 正が 1 か所 — 実例は実行可能で golden 回帰に守られ、`StorySource` によりドキュメント上のコードが実装と乖離しない。リンク切れは起動時に検出される
        - ✅ docs 執筆がエンジン自身への品質圧になる — RichTextEditor・mermaid・数式・全文検索は docs の必要から鍛えられた (ドッグフーディング)
        - ✅ この ADR 章もタダ乗りできた ([ADR-0001](story:ADR/0001-Record-Architecture-Decisions)) — 検索・リンク検証・テーマ・回帰が最初から付いてくる
        - ⚠️ **GitHub 上でそのまま読めない** — docs を読むには Gallery を起動する (かソースを読む)。公開ドキュメントが必要になったら書き出し手段の検討が要る
        - ⚠️ 執筆に C# ビルドが要り、raw string の規約 (`$` の数 = hole の波かっこ数、段落は 1 ソース行、本文に引用符 3 連を含むページは区切りを 4 連に) を覚える必要がある
        - ⚠️ 計画文書のリポジトリ内の置き場が無くなる — 意図的な制約 (現在形の仕様として docs に吸収するか、リポジトリ外へ)
        """, toc: true);
}
