using Luxel.Graphics.TwoD;

namespace Luxel.Tests;

/// <summary>
/// スプライトアトラス (タスク 18 ステップ 1) の GPU 不要・決定的テスト:
/// JSON パース / ピボット既定 / DestRect のピボット合わせ / ImageSubRect の GPU エンコード (SrcX/Y/W/H/Stride) /
/// SpriteAnimation のフレーム進行 (ループ巡回・非ループ飽和・境界)。
/// </summary>
public class SpriteAtlasTests
{
    private const string Json = """
    {
      "texture": "sprites.png",
      "sprites": {
        "player_idle_0": { "x": 0,  "y": 0,  "w": 32, "h": 32, "px": 16, "py": 32 },
        "player_run_0":  { "x": 32, "y": 0,  "w": 32, "h": 32 },
        "tile_grass":    { "x": 0,  "y": 64, "w": 16, "h": 16 }
      }
    }
    """;

    [Fact]
    public void FromJson_ParsesTextureAndRects()
    {
        SpriteAtlas atlas = SpriteAtlas.FromJson(Json);
        Assert.Equal("sprites.png", atlas.TextureUri);
        Assert.Equal(3, System.Linq.Enumerable.Count(atlas.Names));

        SpriteRect idle = atlas["player_idle_0"];
        Assert.Equal(new SpriteRect(0, 0, 32, 32, 16, 32), idle);
    }

    [Fact]
    public void FromJson_PivotDefaultsToZero()
    {
        SpriteAtlas atlas = SpriteAtlas.FromJson(Json);
        SpriteRect run = atlas["player_run_0"];
        Assert.Equal(0f, run.PivotX);
        Assert.Equal(0f, run.PivotY);
        Assert.Equal(32, run.X);
    }

    [Fact]
    public void FromJson_MissingTexture_Throws()
        => Assert.Throws<FormatException>(() => SpriteAtlas.FromJson("""{ "sprites": {} }"""));

    [Fact]
    public void FromJson_MissingRequiredField_Throws()
        => Assert.Throws<FormatException>(() =>
            SpriteAtlas.FromJson("""{ "texture": "t.png", "sprites": { "a": { "x": 0, "y": 0, "w": 8 } } }"""));

    [Fact]
    public void TryGet_MissingName_ReturnsFalse()
    {
        SpriteAtlas atlas = SpriteAtlas.FromJson(Json);
        Assert.False(atlas.TryGet("nope", out _));
        Assert.True(atlas.TryGet("tile_grass", out SpriteRect r));
        Assert.Equal(16, r.W);
    }

    [Fact]
    public void DestRect_AlignsPivotToPosition()
    {
        SpriteAtlas atlas = SpriteAtlas.FromJson(Json);
        // ピボット (16,32) を (100,200) に合わせ、2倍表示 → 左上 = (100-32, 200-64) サイズ 64x64
        RectF d = atlas.DestRect("player_idle_0", 100, 200, scale: 2f);
        Assert.Equal(68f, d.X);
        Assert.Equal(136f, d.Y);
        Assert.Equal(64f, d.W);
        Assert.Equal(64f, d.H);
    }

    [Fact]
    public void DestRect_ZeroPivot_TopLeftAtPosition()
    {
        SpriteAtlas atlas = SpriteAtlas.FromJson(Json);
        RectF d = atlas.DestRect("tile_grass", 10, 20);   // pivot 0, scale 1
        Assert.Equal(new RectF(10, 20, 16, 16), d);
    }

    [Fact]
    public void DrawSprite_EncodesSubRectImagePath()
    {
        SpriteAtlas atlas = SpriteAtlas.FromJson(Json);
        atlas.Bind(srcIndex: 7, atlasWidth: 128, atlasHeight: 128);

        var scene = new Scene2D();
        scene.DrawSprite(atlas, "player_run_0", 50, 60);   // rect (32,0,32,32) pivot 0, scale 1

        (_, GpuPath[] paths, _) = PathEncoder.Encode(scene);
        Assert.Single(paths);
        GpuPath p = paths[0];
        Assert.Equal(2u, p.Kind);          // image
        Assert.Equal(7u, p.SrcIndex);
        Assert.Equal(128u, p.SrcStride);   // アトラス全幅
        Assert.Equal(32u, p.SrcX);         // サブ矩形原点
        Assert.Equal(0u, p.SrcY);
        Assert.Equal(32u, p.SrcW);
        Assert.Equal(32u, p.SrcH);
        // dest bbox = ピボット 0 なので (50,60)-(82,92)
        Assert.Equal(50f, p.BMinX);
        Assert.Equal(60f, p.BMinY);
        Assert.Equal(82f, p.BMaxX);
        Assert.Equal(92f, p.BMaxY);
    }

    [Fact]
    public void ImageSubRect_FullImage_HasZeroOrigin()
    {
        var scene = new Scene2D();
        scene.ImageRect(srcIndex: 3, srcStride: 64, srcW: 64, srcH: 48, x: 0, y: 0, w: 64, h: 48);
        (_, GpuPath[] paths, _) = PathEncoder.Encode(scene);
        Assert.Equal(0u, paths[0].SrcX);
        Assert.Equal(0u, paths[0].SrcY);
        Assert.Equal(64u, paths[0].SrcStride);
    }

    // ---- SpriteAnimation ----

    [Fact]
    public void Animation_FrameAt_LoopWraps()
    {
        // 4 フレーム, 10fps → 0.0s=0, 0.15s=1, 0.45s=4→巡回0? floor(0.45*10)=4 % 4 = 0
        Assert.Equal(0, SpriteAnimation.FrameAt(0.00f, 10, 4, loop: true));
        Assert.Equal(1, SpriteAnimation.FrameAt(0.10f, 10, 4, loop: true));
        Assert.Equal(3, SpriteAnimation.FrameAt(0.35f, 10, 4, loop: true));
        Assert.Equal(0, SpriteAnimation.FrameAt(0.40f, 10, 4, loop: true));   // 巡回
        Assert.Equal(2, SpriteAnimation.FrameAt(0.62f, 10, 4, loop: true));   // floor(6.2)=6 %4 =2
    }

    [Fact]
    public void Animation_FrameAt_NoLoopSaturates()
    {
        Assert.Equal(0, SpriteAnimation.FrameAt(0.00f, 10, 4, loop: false));
        Assert.Equal(3, SpriteAnimation.FrameAt(0.40f, 10, 4, loop: false));
        Assert.Equal(3, SpriteAnimation.FrameAt(9.99f, 10, 4, loop: false));   // 末尾で飽和
    }

    [Fact]
    public void Animation_NegativeTime_ClampsToFirstFrame()
        => Assert.Equal(0, SpriteAnimation.FrameAt(-5f, 10, 4, loop: true));

    [Fact]
    public void Animation_Update_AccumulatesDeterministically()
    {
        var anim = new SpriteAnimation("run_", frameCount: 3, fps: 12f);
        for (int i = 0; i < 6; i++) anim.Update(1f / 12f);   // 6 フレーム分 → 6 % 3 = 0
        Assert.Equal(0, anim.Frame);
        Assert.Equal("run_0", anim.FrameName);
        anim.Update(1f / 12f);   // 7 → 1
        Assert.Equal("run_1", anim.FrameName);
    }

    [Fact]
    public void Animation_NonLoop_Finished()
    {
        var anim = new SpriteAnimation("boom_", frameCount: 3, fps: 10f, loop: false);
        anim.Update(0.19f);
        Assert.False(anim.Finished);   // frame 1
        anim.Update(0.10f);            // 0.29s → floor(2.9)=2 = 末尾
        Assert.True(anim.Finished);
        Assert.Equal("boom_2", anim.FrameName);
    }

    [Fact]
    public void Animation_InvalidArgs_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpriteAnimation("a", 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpriteAnimation("a", 3, 0));
    }
}
