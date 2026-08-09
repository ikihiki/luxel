using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>表示/埋め込み系コントロール (ImageView / ImageBlock / TableBlock / SurfaceView) のストーリー。</summary>
public static class DisplayControlStories
{
    [Story("Controls/ImageView/Basic", Height = 260)]
    public static Widget ImageViewBasic()
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
        return Frame(view);
    }

    private const string SampleImage = "src/Gallery/Luxel.Gallery/assets/sample-sparkline.png";
    private static Luxel.Resources.ResourceHandle<Luxel.Resources.CpuImage>? _imagePreload;

    [Story("Controls/ImageBlock/Basic", Height = 300)]
    public static Widget ImageBlockBasic(StoryContext ctx)
    {
        // snap (1 フレーム描画) の決定性のため画像を同期 preload — 実アプリでは不要
        // (ImageBlock はロード完了をポーリングし実寸へ再実体化する)
        _imagePreload ??= ctx.Resources.Load<Luxel.Resources.CpuImage>(SampleImage);
        try { _imagePreload.Ready.Wait(3000); } catch { /* 失敗時はプレースホルダ表示のまま */ }
        ctx.Play(static d => d.Snap());
        return Frame(ImageBlock(new ImagePayload(SampleImage, "サンプル画像"), ctx.Resources, 360));
    }

    [Story("Controls/TableBlock/Basic", Height = 260)]
    public static Widget TableBlockBasic(StoryContext ctx)
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
        return Frame(TableBlock(payload, 420,
            p => ctx.Log($"commit: {p.Rows.Count} 行")));
    }

    [Story("Controls/SurfaceView/Basic", Height = 300)]
    public static Widget SurfaceViewBasic(StoryContext ctx)
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
        return Frame(surface);
    }
}
