using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static partial class DocsAdr
{
    [Story("ADR/0001-Record-Architecture-Decisions", Order = 72)]
    public static Widget Adr0001(StoryContext ctx) => WithDocFonts(Docs(ctx, $$"""
        # ADR-0001 — アーキテクチャ決定を ADR として Gallery に記録する

        - **Status**: Accepted
        - **Date**: 2026-07-08
        - **Deciders**: ikihiki

        ## Context

        Luxel は薄い GPU 抽象の上に多数のサブシステムを積む構成で、随所に「なぜこの設計なのか」という判断があります — 固定レイアウト (8B push 定数 + bindless heap)、vk/dx ピクセル一致という開発規律、docs を Gallery に一本化した方針、Effekseer を統合しない選択などです。

        こうした *理由* は現状、コード・docs・コミットログ・個々人の記憶に散在しています。docs は「現在の姿」を説明しますが、「なぜその姿を選び、何を捨てたか」は残りにくく、後から掘り起こすのに時間がかかります。

        ## Decision

        アーキテクチャ上の重要な決定を **ADR (Architecture Decision Record)** として、Gallery の独立章「**ADR**」に **1 決定 1 ページ**で記録します。既存の docs ページと同じ仕組み (`[Story]` + `Kit.Docs` + `WithDocFonts`) を使い、全文検索・`story:` リンク・デッドリンク検証・テーマ切替をそのまま共有します。

        - 番号は連番 `ADR-NNNN`、パスは `ADR/NNNN-短いタイトル`
        - 節は Status / Context / Decision / Alternatives / Consequences で固定
        - 決定を変えるときは元の ADR を**書き換えず**、新しい ADR を追加して古い方を `Superseded by ADR-NNNN` にする

        ## Alternatives

        - **別リポジトリや `docs/adr/` の素の Markdown** — docs は Gallery に一本化済み ([Docs/Gallery](story:Docs/Gallery)) で、二重管理になりデッドリンク検証も全文検索も効かない → 却下
        - **コミットメッセージ / PR 説明に理由を書く** — 検索性・一覧性が低く、決定単位で辿れない → 却下 (補助としては継続してよい)
        - **記録しない (コード + 記憶に頼る)** — まさに現状の課題そのもの → 却下

        ## Consequences

        - ✅ 決定の理由と却下案が 1 か所に線形に残り、Gallery の検索・ナビゲーション・ダークテーマをそのまま使える
        - ✅ docs と同じ執筆・レビュー・デッドリンク検証フローに乗る (新規 ADR は `[Story]` を足して `dotnet build` するだけ)
        - ⚠️ 決定ごとに 1 ページ書く軽い手間が増える。瑣末な決定まで ADR 化しない線引きが要る ([ADR/Overview](story:ADR/Overview) の「何を ADR にするか」が目安)
        - ⚠️ ADR は不変記録。変更は supersede 運用を守る必要がある
        """, toc: true, fences: DocsFences));
}
