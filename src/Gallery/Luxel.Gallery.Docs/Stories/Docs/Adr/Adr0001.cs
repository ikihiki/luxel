using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.DocKit.DocsKit;

using static Luxel.Gallery.Story;

namespace Luxel.Gallery.Stories;

[StoryMeta("Internals/ADR")]
public static partial class DocsAdr
{
    [Story]
    public static StoryResult Adr0001(StoryContext ctx)
    {
        ctx.Play(static d => d.Snap());   // 新スタック (MarkdownDoc) 描画の golden
        return $$"""
        # ADR-0001 — アーキテクチャ決定を ADR として Gallery に記録する

        {{Toc()}}

        - **Status**: Accepted
        - **Date**: 2026-07-08
        - **Deciders**: ikihiki

        ## Context

        Luxel は薄い GPU 抽象の上に多数のサブシステムを積む構成で、随所に「なぜこの設計なのか」という判断があります — 固定レイアウト (最大192Bのルート引数 + bindless heap)、vk/dx ピクセル一致という開発規律、docs を Gallery に一本化した方針、Effekseer を統合しない選択などです。

        こうした *理由* は現状、コード・docs・コミットログ・個々人の記憶に散在しています。docs は「現在の姿」を説明しますが、「なぜその姿を選び、何を捨てたか」は残りにくく、後から掘り起こすのに時間がかかります。

        ## Decision

        アーキテクチャ上の重要な決定を **ADR (Architecture Decision Record)** として、Gallery の独立章「**ADR**」に **1 決定 1 ページ**で記録します。既存の docs ページと同じ仕組み (`[Story]` + `Kit.Docs` + `WithDocFonts`) を使い、全文検索・`story:` リンク・デッドリンク検証・テーマ切替をそのまま共有します。

        - 番号は連番 `ADR-NNNN`、パスは `Internals/ADR/NNNN-短いタイトル`
        - 節は Status / Context / Decision / Alternatives / Consequences で固定
        - 決定を変えるときは元の ADR を**書き換えず**、新しい ADR を追加して古い方を `Superseded by ADR-NNNN` にする

        ## Alternatives

        - **別リポジトリや `docs/adr/` の素の Markdown** — docs は Gallery に一本化済み ([Internals/Gallery](story:Internals/Gallery)) で、二重管理になりデッドリンク検証も全文検索も効かない → 却下
        - **コミットメッセージ / PR 説明に理由を書く** — 検索性・一覧性が低く、決定単位で辿れない → 却下 (補助としては継続してよい)
        - **記録しない (コード + 記憶に頼る)** — まさに現状の課題そのもの → 却下

        ## Consequences

        - ✅ 決定の理由と却下案が 1 か所に線形に残り、Gallery の検索・ナビゲーション・ダークテーマをそのまま使える
        - ✅ docs と同じ執筆・レビュー・デッドリンク検証フローに乗る (新規 ADR は `[Story]` を足して `dotnet build` するだけ)
        - ⚠️ 決定ごとに 1 ページ書く軽い手間が増える。瑣末な決定まで ADR 化しない線引きが要る ([Internals/ADR/Overview](story:Internals/ADR/Overview) の「何を ADR にするか」が目安)
        - ⚠️ ADR は不変記録。変更は supersede 運用を守る必要がある
        """;
    }
}
