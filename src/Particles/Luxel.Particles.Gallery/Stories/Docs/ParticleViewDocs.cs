using Luxel.Gallery.UI;

namespace Luxel.Particles.Gallery;

/// <summary>利用者向け ParticleView の構造化された日本語 Docs。</summary>
internal static class ParticleViewDocs
{
    private static readonly ControlDocsPage Page = new(
        "global::Luxel.Particles.UI.ParticleView",
        "ParticleView",
        "二次元 ParticleSystem を UI ツリー内で更新・描画するビューです。効果のプレビュー、設定画面、Gallery の対話確認に使います。",
        ["ParticleSystem の二次元エフェクトを通常の Widget レイアウトへ埋め込み、必要ならビュー自身に Update と Sync を任せる場合。"],
        ["三次元パーティクルを表示する場合や、ゲーム loop が既に同じ ParticleSystem の時間進行を所有している場合。"],
        [new("Canvas2D", "軽量な独自二次元図形を描画します。"), new("GpuView", "GPU レンダラーの出力を専用領域へ埋め込みます。")],
        "ParticleView(particles: system, viewWidth: 480, viewHeight: 280, animated: true)",
        "一つの ParticleView と、内部で一度生成される ParticleNode から構成します。",
        "`animated` が true の自動更新形と、初回 Sync だけを行う静的表示形があります。`circleSegments` で円形粒子の分割数を指定します。",
        "エミッター設定、粒子データ、乱数、プリセットは渡した ParticleSystem が所有します。`animated: true` の間は ParticleView が animation tick ごとの `Update` と `Sync` を所有します。",
        "表示専用で、ParticleView 自身はポインター操作を登録しません。再生、停止、再初期化、数値設定は隣接する通常コントロールから ParticleSystem へ適用します。",
        [],
        "focus、activation、dismissal はありません。表示の追加・削除と `animated` の選択は呼び出し側が行います。",
        new ControlDocsAccessibility(
            "隣接する文字で効果名、状態、目的を示します。",
            "専用の image、status、animation semantic role は公開しません。",
            "再生状態、経過時間、完了状態は支援技術へ公開しません。必要な状態は別の文字 UI へ反映します。",
            "粒子色と背景色のコントラストを利用テーマとエフェクトの双方で確認します。",
            "`animated: true` では毎 tick 更新されます。動きを減らす設定が必要なら `animated: false` または表示停止を呼び出し側で選びます。",
            "視覚効果だけに重要情報を依存させず、再生制御にはラベル付きの通常コントロールを用意します。"),
        new ControlDocsThemeLayout(
            "ParticleView は背景を描かないため、周囲の Border や surface で背景と境界をテーマ化します。",
            "親制約へ `viewWidth` と `viewHeight` を通して配置し、ローカル座標で粒子を描画します。",
            "表示幅と高さは最小 1 px に補正されます。エフェクトを確認できる有限寸法を明示します。"),
        new ControlDocsConstraints(
            "大量粒子や大きい `circleSegments` では Update、geometry、Sync のコストが増えます。三次元 ParticleSystem 用のビューではありません。",
            "ParticleSystem の開始、停止、reset、seed、資源寿命は所有者が管理します。Widget の animation callback は実体化スコープの寿命に従います。",
            "二次元描画と animation tick を提供する Gallery/UI ホストで利用します。ゲーム ECS では system を直接更新する構成を優先します。"),
        new ControlDocsApi(
            "ParticleView",
            "`particles`、`viewWidth`、`viewHeight`、`animated`、`circleSegments` が固有パラメーターです。",
            "固有イベントはありません。再生制御と設定変更は ParticleSystem API または外部状態を通じて行います。"),
        new ControlDocsStory("Controls/Rendering/ParticleView/Basic", "基本例", "ParticleView の最小構成を実行します。", StoryKind.Basic),
        [
            new("Controls/Rendering/ParticleView/Playground", "プレイグラウンド", "ParticleView の公開パラメーターを対話的に確認します。", StoryKind.Playground),
            new("Learn/Animation/Particles/ParticleViewSample", "ParticleView の実例", "粒子システムを UI へ埋め込む構成を確認します。", StoryKind.Unspecified),
        ]);

    internal static void Register(StoryCatalogBuilder builder, IReadOnlyList<GeneratedComponentStoryDescriptor> descriptors)
        => ControlDocsRenderer.Register(builder, descriptors, [Page],
            "日本語で執筆した構造化 ParticleView Docs。");
}
