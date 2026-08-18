using Luxel.Controls;

namespace Luxel.Particles.Gallery;

/// <summary>利用者向け ParticleView の日本語 Docs。</summary>
internal static class ParticleViewDocs
{
    private const string Identity = "global::Luxel.Particles.UI.ParticleView";

    internal static void Register(StoryCatalogBuilder builder, IReadOnlyList<GeneratedComponentStoryDescriptor> descriptors)
    {
        GeneratedComponentStoryDescriptor descriptor = descriptors.SingleOrDefault(item => item.ComponentType == Identity)
            ?? throw new InvalidOperationException($"Docs 対象のコンポーネントが見つかりません: {Identity}");
        builder.Add(new StoryInfo(descriptor.DocsPath, _ => Build(descriptor), Source: "日本語で執筆した ParticleView Docs。"), replaceGenerated: true);
    }

    private static StoryResult Build(GeneratedComponentStoryDescriptor descriptor)
    {
        var result = new StoryResult(1800, 1);
        result.AppendLiteral($"""
            # ParticleView

            ## 概要と用途

            `ParticleView` は二次元パーティクルシステムを UI ツリー内で表示するビューです。効果のプレビュー、設定画面、Gallery の対話確認に使います。

            ## 最小使用例

            ```csharp
            ParticleView(system, width: 480, height: 280)
            ```

            ## 状態の所有

            エミッター設定、粒子シミュレーション、時間進行は渡したパーティクルシステムが所有します。`ParticleView` は表示領域とホストへの接続を担い、業務状態やプリセットの永続化は行いません。

            ## 主なパラメーターとイベント

            パーティクルシステム、表示幅、高さ、背景などの表示指定が中心です。設定変更や再生制御はシステム側 API と外部 `Signal` で管理します。

            ## 操作・キーボード・アクセシビリティ

            基本は閲覧用で、標準のキーボード操作や支援技術向け意味表現は提供しません。再生・停止・再初期化・数値設定が必要な場合は、ラベル付きの通常 UI コントロールを別に用意してください。視覚効果だけに重要情報を依存させないでください。

            ## テーマとレイアウト

            描画範囲を決める幅と高さを明示し、周囲の `Border` で背景や境界を構成します。粒子色と背景色には十分なコントラストを確保し、UI テーマ変更時も視認性を確認します。

            ## 制約・能力・ライフサイクル

            フレーム更新と描画可能なホストが必要です。大量粒子では更新・描画コストが増えます。システムの開始、停止、リセット、乱数シード、資源破棄は所有者側で管理し、ビュー破棄後に更新購読を残さないでください。三次元パーティクル表示のためのビューではありません。

            ## API リファレンス

            """);
        result.AppendFormatted(new DocEmbed(global::Luxel.Gallery.UI.Kit.ApiTable("ParticleView", inherited: true, width: 760), DocEmbedKind.ControlApiTable, "ParticleView", IncludeInherited: true));
        result.AppendLiteral($"\n\n## 関連する Basic と Examples\n\n- [Basic](story:{descriptor.BasicPath})\n- [ParticleView の実例](story:Learn/Animation/Particles/ParticleViewSample)\n");
        return result;
    }
}
