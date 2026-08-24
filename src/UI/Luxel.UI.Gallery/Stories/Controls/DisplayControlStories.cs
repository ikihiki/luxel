using Luxel.Controls;
using Luxel.Graphics;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.UI.Gallery.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>表示/埋め込み系コントロール (ImageView / ImageBlock / TableBlock / SurfaceView) のストーリー。</summary>
[StoryMeta("Controls")]
public static class DisplayControlStories
{

    [Story(Path = "Controls/Rendering/Canvas2D/Basic", ArgsEnabled = false,
        ShortDescription = "保持型 Scene2D へ静的な図形を描き、UI レイアウト内へ埋め込む基本例です。",
        LongDescription = "固定サイズと静的 draw delegate を使い、背景、グリッド、図形、線が同じ Canvas2D ノードで決定的に描画されます。")]
    public static StoryResult Canvas2DBasic() => Canvas2D(360, 210, draw: scene =>
    {
        scene.FillRoundedRect(Color2D.Rgba(15, 23, 42, 255), 0, 0, 360, 210, 12);
        for (int x = 24; x < 360; x += 24) scene.FillRect(Color2D.Rgba(148, 163, 184, 28), x, 0, 1, 210);
        for (int y = 24; y < 210; y += 24) scene.FillRect(Color2D.Rgba(148, 163, 184, 28), 0, y, 360, 1);
        scene.FillRoundedRect(Color2D.Rgba(59, 130, 246, 255), 34, 38, 118, 72, 12);
        scene.FillCircle(Color2D.Rgba(245, 158, 11, 255), 244, 76, 38);
        scene.StrokeLine(Color2D.Rgba(226, 232, 240, 255), 44, 164, 316, 132, 4);
    });

    [Story(Path = "Controls/Rendering/GpuView/Basic", ArgsEnabled = false,
        ShortDescription = "GpuView が所有する描画先を単色でクリアし、GPU callback の最小契約を示します。",
        LongDescription = "リソースやアニメーションを持たない一回限りの callback で、color target の clear、framebuffer への copy、submit を決定的に実行します。")]
    public static StoryResult GpuViewBasic() => GpuView(
        320,
        200,
        static (device, surface, _) =>
        {
            using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
            command.BeginRendering(surface.ColorTarget, null, 0.08f, 0.22f, 0.36f, 1f)
                .EndRendering();
            surface.CopyColorToFramebuffer(command);
            command.Finish();
            device.MainQueue.Submit(command);
            return GpuViewRenderResult.Ready;
        },
        animated: false);

    [Story(Path = "Controls/Rendering/ImageView/Basic",
        ShortDescription = "CPU 上で生成した RGBA 勾配を転送し、画像データと表示寸法を分離する基本例です。")]
    public static StoryResult ImageViewBasic()
    {
        // CPU の RGBA を SetPixels — 実体化前でも可 (pending 保持)。表示は widget サイズへ nearest 拡縮
        ImageView view = ImageView(192, 144);
        const int w = 64, h = 48;
        byte[] px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                px[i] = (byte)(x * 255 / w);
                px[i + 1] = (byte)(y * 255 / h);
                px[i + 2] = 160;
                px[i + 3] = 255;
            }
        }
        view.SetPixels(w, h, px);
        return view;
    }

    private const string SampleImage = "src/Gallery/Luxel.Gallery/assets/sample-sparkline.png";
    private static Luxel.Resources.ResourceHandle<Luxel.Resources.CpuImage>? _imagePreload;

    [Story(Path = "Controls/Rendering/ImageBlock/Basic",
        ShortDescription = "ResourceSystem から画像を読み込み、代替テキスト付きの文書ブロックとして表示します。")]
    public static StoryResult ImageBlockBasic(StoryContext ctx)
    {
        // snap (1 フレーム描画) の決定性のため画像を同期 preload — 実アプリでは不要
        // (ImageBlock はロード完了をポーリングし実寸へ再実体化する)
        _imagePreload ??= ctx.Resources.Load<Luxel.Resources.CpuImage>(SampleImage);
        try { _imagePreload.Ready.Wait(3000); } catch { /* 失敗時はプレースホルダ表示のまま */ }
        ctx.Play(static d => d.Snap());
        return ImageBlock(new ImagePayload(SampleImage, "サンプル画像"), ctx.Resources, 360);
    }

    [Story(Path = "Controls/Rendering/TableBlock/Basic",
        ShortDescription = "列揃えを持つ表を直接編集し、確定後の payload を commit へ返す基本例です。")]
    public static StoryResult TableBlockBasic(StoryContext ctx)
    {
        // GFM pipe table のブロック widget。セルをクリックして直接編集、Tab/Enter で移動、
        // 最下段 Enter で行追加 — 編集確定ごとに commit が呼ばれる
        var payload = new TablePayload(
            [
                ["名前", "値", "備考"],
                ["alpha", "1", "最初の行"],
                ["beta", "2", ""],
                ["gamma", "3", "最後の行"],
            ],
            [TableAlign.Left, TableAlign.Right, TableAlign.Left]);
        return TableBlock(payload, 420,
            p => ctx.Log($"commit: {p.Rows.Count} 行"));
    }

    [Story(Path = "Controls/Rendering/SurfaceView/Basic",
        ShortDescription = "独立した子 UiHost を埋め込み、入力、フォーカス、状態を親ツリーから分離します。")]
    public static StoryResult SurfaceViewBasic(StoryContext ctx)
    {
        // iframe 相当の埋め込みサーフェス — 子 RetainedCanvas + 子 UiHost + 専用 framebuffer。
        // フォーカス/オーバーレイ/状態は子側に閉じ、入力はローカル座標で転送される
        SurfaceView surface = SurfaceView(320, 200);
        Signal<bool> on = new(false);
        surface.SetContent(
            Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 10, padding: new Thickness(12))
            [VStack(8)[
                Heading("子 UiHost", 2),
                Label("この矩形の中は独立した UI ツリー"),
                HStack(8)[Switch(on), Button(_ => ctx.Log("child click"), "子のボタン")]]]);
        return surface;
    }
}
