using Luxel;
using Luxel.RenderGraph;
using Xunit;
// 名前空間 Luxel.RenderGraph と型 RenderGraph が同名なので、テスト内ではエイリアスで区別する。
using Rg = Luxel.RenderGraph.RenderGraph;

namespace Luxel.Tests;

/// <summary>
/// RenderGraph (RG-M1) の GPU 非依存テスト。Setup / 寿命解析 / バリデーション / 自動バリア計算の
/// 主要ロジックを検証する。Execute はテストモードでも走るが GPU を必要としない範囲のみ。
/// </summary>
public class RenderGraphTests
{
    private static BufferDesc Desc(ulong size = 64) => new(size, GpuMemoryKind.HostMapped);

    [Fact]
    public void InvalidHandle_IsNotValid()
    {
        Assert.False(BufferHandle.Invalid.IsValid);
        Assert.True(new BufferHandle(1).IsValid);
    }

    [Fact]
    public void Setup_AssignsDistinctIds()
    {
        var rg = new Rg();
        var a = rg.CreateBufferForTest(Desc(), "a");
        var b = rg.CreateBufferForTest(Desc(), "b");
        Assert.NotEqual(a.Id, b.Id);
        Assert.True(a.IsValid);
        Assert.True(b.IsValid);
    }

    [Fact]
    public void Lifetime_FirstWrite_AndLastRead_AreCorrect()
    {
        var rg = new Rg();
        var ui = rg.ImportBufferForTest("ui");
        var tmp = rg.CreateBufferForTest(Desc(), "tmp");
        var blr = rg.CreateBufferForTest(Desc(), "blr");
        var fin = rg.ImportBufferForTest("final");

        rg.AddPass("P0", PassQueue.Compute).Read(ui).Write(tmp).Execute(_ => { });
        rg.AddPass("P1", PassQueue.Compute).Read(tmp).Write(blr).Execute(_ => { });
        rg.AddPass("P2", PassQueue.Compute).Read(ui).Read(blr).Write(fin).Execute(_ => { });

        rg.CompileForTest();

        Assert.Equal((-1, 2), rg.GetLifetime(ui));    // import (writer なし), 最後の読み = P2
        Assert.Equal((0, 1), rg.GetLifetime(tmp));    // P0 で書き P1 で読まれる
        Assert.Equal((1, 2), rg.GetLifetime(blr));    // P1 で書き P2 で読まれる
        Assert.Equal((2, -1), rg.GetLifetime(fin));   // P2 で書き、以降読み無し
    }

    [Fact]
    public void PassBuilder_Read_RejectsWriteUsage()
    {
        var rg = new Rg();
        var b = rg.CreateBufferForTest(Desc(), "b");
        var pb = rg.AddPass("P", PassQueue.Compute);
        Assert.Throws<ArgumentException>(() => { pb.Read(b, ResourceUsage.StorageBufferWrite); });
    }

    [Fact]
    public void PassBuilder_Write_RejectsReadUsage()
    {
        var rg = new Rg();
        var b = rg.CreateBufferForTest(Desc(), "b");
        var pb = rg.AddPass("P", PassQueue.Compute);
        Assert.Throws<ArgumentException>(() => { pb.Write(b, ResourceUsage.StorageBufferRead); });
    }

    [Fact]
    public void PassBuilder_RejectsInvalidHandle()
    {
        var rg = new Rg();
        var pb = rg.AddPass("P", PassQueue.Compute);
        Assert.Throws<ArgumentException>(() => { pb.Read(BufferHandle.Invalid); });
        Assert.Throws<ArgumentException>(() => { pb.Write(BufferHandle.Invalid); });
    }

    [Fact]
    public void AddPass_AfterCompile_Throws()
    {
        var rg = new Rg();
        var b = rg.ImportBufferForTest("b");
        rg.AddPass("P", PassQueue.Compute).Read(b).Execute(_ => { });
        rg.CompileForTest();
        Assert.Throws<InvalidOperationException>(() => { rg.AddPass("Q", PassQueue.Compute); });
    }

    [Fact]
    public void CreateBuffer_ZeroSize_Throws()
    {
        var rg = new Rg();
        Assert.Throws<ArgumentException>(() => { rg.CreateBufferForTest(new BufferDesc(0, GpuMemoryKind.HostMapped), "z"); });
    }

    [Fact]
    public void Execute_DriversBarriers_InTestMode_DoesNotThrow()
    {
        // テストモードでは GpuCommandBuffer も用意できないので Execute は呼ばない。
        // 代わりに Compile が走り、Setup→Compile の繋ぎが破綻しないことを確認する。
        var rg = new Rg();
        var ui = rg.ImportBufferForTest("ui");
        var tmp = rg.CreateBufferForTest(Desc(), "tmp");
        rg.AddPass("P0", PassQueue.Compute).Read(ui).Write(tmp).Execute(_ => { });

        var compiled = rg.CompileForTest();
        Assert.Single(compiled.Order);
        Assert.Equal("P0", compiled.Order[0].Name);
        Assert.Equal(1, rg.PassCount);
    }

    [Fact]
    public void ResourceAccess_Read_DefaultsToStorageBufferRead()
    {
        var rg = new Rg();
        var b = rg.ImportBufferForTest("b");
        rg.AddPass("P", PassQueue.Compute).Read(b).Execute(_ => { });
        rg.CompileForTest();
        // 寿命解析が回ったことの裏返し。
        var (first, last) = rg.GetLifetime(b);
        Assert.Equal(-1, first);
        Assert.Equal(0, last);
    }

    [Fact]
    public void ReadWrite_RegistersAsBothReadAndWrite()
    {
        var rg = new Rg();
        var b = rg.ImportBufferForTest("b");
        rg.AddPass("P", PassQueue.Compute).ReadWrite(b).Execute(_ => { });
        rg.CompileForTest();
        // P で書き＝最初の書き手, P で読み＝最後の読み手
        var (first, last) = rg.GetLifetime(b);
        Assert.Equal(0, first);
        Assert.Equal(0, last);
    }

    [Fact]
    public void ResourceUsage_StageMapping()
    {
        // バリア計算の素になる stage マッピングのスナップショット。
        Assert.Equal(GpuStage.ComputeShader, ResourceUsage.StorageBufferRead.Stage());
        Assert.Equal(GpuStage.ComputeShader, ResourceUsage.StorageBufferWrite.Stage());
        Assert.Equal(GpuStage.ComputeShader, ResourceUsage.StorageBufferReadWrite.Stage());
        Assert.Equal(GpuStage.PixelShader, ResourceUsage.SampledInPixelShader.Stage());
        Assert.Equal(GpuStage.Copy, ResourceUsage.CopyDest.Stage());
        Assert.Equal(GpuStage.DrawIndirect, ResourceUsage.IndirectArgs.Stage());
    }

    [Fact]
    public void ResourceUsage_IsWriteClassification()
    {
        Assert.True(ResourceUsage.StorageBufferWrite.IsWrite());
        Assert.True(ResourceUsage.StorageBufferReadWrite.IsWrite());
        Assert.True(ResourceUsage.CopyDest.IsWrite());
        Assert.False(ResourceUsage.StorageBufferRead.IsWrite());
        Assert.False(ResourceUsage.SampledInPixelShader.IsWrite());
        Assert.False(ResourceUsage.UniformBuffer.IsWrite());
        Assert.False(ResourceUsage.IndirectArgs.IsWrite());
        Assert.False(ResourceUsage.CopySource.IsWrite());
    }

    [Fact]
    public void Tutorial_one_pass_external_outputs_remain_live()
    {
        var rg = new Rg();
        TextureHandle color = rg.ImportTextureForTest("present-color");
        TextureHandle depth = rg.ImportTextureForTest("present-depth");
        BufferHandle framebuffer = rg.ImportBufferForTest("present-framebuffer");

        rg.AddPass("DrawAndReadback", PassQueue.Graphics)
            .Write(color, TextureUsage.ColorAttachment)
            .Write(depth, TextureUsage.DepthAttachment)
            .Write(framebuffer, ResourceUsage.CopyDest)
            .Execute(_ => { });

        rg.CompileForTest();
        Assert.False(rg.IsPassCulled(0));
    }

    [Fact]
    public void Tutorial_post_process_chain_keeps_dependencies_and_culls_dead_branch()
    {
        var rg = new Rg();
        TextureHandle scene = rg.CreateTextureForTest(new TextureDesc(801, 603, GpuFormat.Rgba8Unorm), "scene");
        BufferHandle pixels = rg.CreateBufferForTest(new BufferDesc(832UL * 603 * 4), "pixels");
        BufferHandle final = rg.ImportBufferForTest("final");
        BufferHandle dead = rg.CreateBufferForTest(new BufferDesc(64), "dead");

        rg.AddPass("DrawScene", PassQueue.Graphics).Write(scene, TextureUsage.ColorAttachment).Execute(_ => { });
        rg.AddPass("SceneReadback", PassQueue.Graphics).Read(scene, TextureUsage.CopySource)
            .Write(pixels, ResourceUsage.CopyDest).Execute(_ => { });
        rg.AddPass("PostProcess", PassQueue.Compute).Read(pixels).Write(final).Execute(_ => { });
        rg.AddPass("Dead", PassQueue.Compute).Write(dead).Execute(_ => { });

        rg.CompileForTest();
        Assert.False(rg.IsPassCulled(0));
        Assert.False(rg.IsPassCulled(1));
        Assert.False(rg.IsPassCulled(2));
        Assert.True(rg.IsPassCulled(3));
    }

    // === RG-M2: デッドパスカリング ============================================

    [Fact]
    public void DeadPass_NoExternalSink_IsCulled()
    {
        var rg = new Rg();
        var t = rg.CreateBufferForTest(Desc(), "t");
        rg.AddPass("Dead", PassQueue.Compute).Write(t).Execute(_ => { });
        rg.CompileForTest();
        Assert.True(rg.IsPassCulled(0));
    }

    [Fact]
    public void DeadPass_BackwardReachability_KeepsChainToExternal()
    {
        var rg = new Rg();
        var ext = rg.ImportBufferForTest("ext");
        var live = rg.CreateBufferForTest(Desc(), "live");
        var dead = rg.CreateBufferForTest(Desc(), "dead");

        rg.AddPass("LiveProducer", PassQueue.Compute).Write(live).Execute(_ => { });
        rg.AddPass("LiveSink", PassQueue.Compute).Read(live).Write(ext).Execute(_ => { });
        rg.AddPass("DeadProducer", PassQueue.Compute).Write(dead).Execute(_ => { });

        rg.CompileForTest();
        Assert.False(rg.IsPassCulled(0));
        Assert.False(rg.IsPassCulled(1));
        Assert.True(rg.IsPassCulled(2));
    }

    [Fact]
    public void DeadPass_TransitiveCulling()
    {
        // dead2 が dead1 を読むが、dead2 自身も誰にも読まれない → 両方 culled。
        var rg = new Rg();
        var ext = rg.ImportBufferForTest("ext");
        var dead1 = rg.CreateBufferForTest(Desc(), "dead1");
        var dead2 = rg.CreateBufferForTest(Desc(), "dead2");
        var live = rg.CreateBufferForTest(Desc(), "live");

        rg.AddPass("DeadA", PassQueue.Compute).Write(dead1).Execute(_ => { });
        rg.AddPass("DeadB", PassQueue.Compute).Read(dead1).Write(dead2).Execute(_ => { });
        rg.AddPass("LiveA", PassQueue.Compute).Write(live).Execute(_ => { });
        rg.AddPass("LiveB", PassQueue.Compute).Read(live).Write(ext).Execute(_ => { });

        rg.CompileForTest();
        Assert.True(rg.IsPassCulled(0));
        Assert.True(rg.IsPassCulled(1));
        Assert.False(rg.IsPassCulled(2));
        Assert.False(rg.IsPassCulled(3));
    }

    // === RG-M2: Transient aliasing ============================================

    [Fact]
    public void Aliasing_NonOverlappingSameSize_ShareSlot()
    {
        // 3 つの同形 transient が連続した非重複寿命を持つ → 同じ物理 slot に alias される。
        var rg = new Rg();
        var x = rg.ImportBufferForTest("x");
        var y = rg.ImportBufferForTest("y");
        var z = rg.ImportBufferForTest("z");
        var t1 = rg.CreateBufferForTest(Desc(), "t1");
        var t2 = rg.CreateBufferForTest(Desc(), "t2");
        var t3 = rg.CreateBufferForTest(Desc(), "t3");

        rg.AddPass("A", PassQueue.Compute).Write(t1).Execute(_ => { });
        rg.AddPass("B", PassQueue.Compute).Read(t1).Write(x).Execute(_ => { });
        rg.AddPass("C", PassQueue.Compute).Write(t2).Execute(_ => { });
        rg.AddPass("D", PassQueue.Compute).Read(t2).Write(y).Execute(_ => { });
        rg.AddPass("E", PassQueue.Compute).Write(t3).Execute(_ => { });
        rg.AddPass("F", PassQueue.Compute).Read(t3).Write(z).Execute(_ => { });

        rg.CompileForTest();
        // 全部 slot 0 を共有
        Assert.Equal(0, rg.GetPhysicalSlot(t1));
        Assert.Equal(0, rg.GetPhysicalSlot(t2));
        Assert.Equal(0, rg.GetPhysicalSlot(t3));
        Assert.True(rg.IsAliased(t1));
        Assert.True(rg.IsAliased(t2));
        Assert.True(rg.IsAliased(t3));
    }

    [Fact]
    public void Aliasing_OverlappingTransients_DistinctSlots()
    {
        // ping-pong (t1, t2) のように寿命が重なる → 別 slot。
        var rg = new Rg();
        var ext = rg.ImportBufferForTest("ext");
        var t1 = rg.CreateBufferForTest(Desc(), "t1");
        var t2 = rg.CreateBufferForTest(Desc(), "t2");

        rg.AddPass("P0", PassQueue.Compute).Write(t1).Execute(_ => { });
        rg.AddPass("P1", PassQueue.Compute).Read(t1).Write(t2).Execute(_ => { });
        rg.AddPass("P2", PassQueue.Compute).Read(t2).Write(ext).Execute(_ => { });

        rg.CompileForTest();
        Assert.NotEqual(rg.GetPhysicalSlot(t1), rg.GetPhysicalSlot(t2));
    }

    [Fact]
    public void Aliasing_DifferentSize_AreSeparateGroups()
    {
        // 異なる (Size, Kind) の transient は別グループ → 各グループ内で独立に slot 番号付け。
        var rg = new Rg();
        var ext = rg.ImportBufferForTest("ext");
        var small1 = rg.CreateBufferForTest(Desc(64), "small1");
        var small2 = rg.CreateBufferForTest(Desc(64), "small2");
        var big = rg.CreateBufferForTest(Desc(128), "big");

        rg.AddPass("A", PassQueue.Compute).Write(small1).Execute(_ => { });
        rg.AddPass("B", PassQueue.Compute).Read(small1).Write(big).Execute(_ => { });
        rg.AddPass("C", PassQueue.Compute).Read(big).Write(small2).Execute(_ => { });
        rg.AddPass("D", PassQueue.Compute).Read(small2).Write(ext).Execute(_ => { });

        rg.CompileForTest();
        // small1 と small2 は同形・寿命非重複 → 同じ slot 0 に alias
        Assert.Equal(rg.GetPhysicalSlot(small1), rg.GetPhysicalSlot(small2));
        Assert.True(rg.IsAliased(small1));
        Assert.True(rg.IsAliased(small2));
        // big は別グループの slot 0、small と物理共有はしない (グループが違う)
        Assert.False(rg.IsAliased(big));
    }

    // === RG-M6: Texture aliasing ============================================

    [Fact]
    public void Texture_InvalidHandle_IsNotValid()
    {
        Assert.False(TextureHandle.Invalid.IsValid);
        Assert.True(new TextureHandle(1).IsValid);
    }

    [Fact]
    public void Texture_Setup_AssignsDistinctIds()
    {
        var rg = new Rg();
        var a = rg.CreateTextureForTest(new TextureDesc(256, 256, GpuFormat.Rgba8Unorm), "a");
        var b = rg.CreateTextureForTest(new TextureDesc(256, 256, GpuFormat.Rgba8Unorm), "b");
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Texture_Aliasing_NonOverlappingSameFormat_ShareSlot()
    {
        var rg = new Rg();
        var outBuf = rg.ImportBufferForTest("outBuf");
        var rt1 = rg.CreateTextureForTest(new TextureDesc(128, 128, GpuFormat.Rgba8Unorm), "rt1");
        var rt2 = rg.CreateTextureForTest(new TextureDesc(128, 128, GpuFormat.Rgba8Unorm), "rt2");

        // rt1: pass0 write, pass1 read → [0..1]
        // rt2: pass2 write, pass3 read → [2..3]  ← 非重複
        rg.AddPass("P0", PassQueue.Graphics).Write(rt1, TextureUsage.ColorAttachment).Execute(_ => { });
        rg.AddPass("P1", PassQueue.Compute).Read(rt1).Write(outBuf).Execute(_ => { });
        rg.AddPass("P2", PassQueue.Graphics).Write(rt2, TextureUsage.ColorAttachment).Execute(_ => { });
        rg.AddPass("P3", PassQueue.Compute).Read(rt2).Write(outBuf).Execute(_ => { });

        rg.CompileForTest();
        Assert.Equal(rg.GetPhysicalSlot(rt1), rg.GetPhysicalSlot(rt2));
        Assert.True(rg.IsAliased(rt1));
        Assert.True(rg.IsAliased(rt2));
    }

    [Fact]
    public void Texture_Aliasing_DifferentFormat_NotShared()
    {
        var rg = new Rg();
        var outBuf = rg.ImportBufferForTest("outBuf");
        var rt1 = rg.CreateTextureForTest(new TextureDesc(128, 128, GpuFormat.Rgba8Unorm), "rt1");
        var rt2 = rg.CreateTextureForTest(new TextureDesc(128, 128, GpuFormat.R32Float), "rt2");

        rg.AddPass("P0", PassQueue.Graphics).Write(rt1, TextureUsage.ColorAttachment).Execute(_ => { });
        rg.AddPass("P1", PassQueue.Compute).Read(rt1).Write(outBuf).Execute(_ => { });
        rg.AddPass("P2", PassQueue.Graphics).Write(rt2, TextureUsage.ColorAttachment).Execute(_ => { });
        rg.AddPass("P3", PassQueue.Compute).Read(rt2).Write(outBuf).Execute(_ => { });

        rg.CompileForTest();
        // Format が違うので別グループ → グループ内 slot は両者とも 0 だが、aliased=false
        Assert.False(rg.IsAliased(rt1));
        Assert.False(rg.IsAliased(rt2));
    }

    [Fact]
    public void Texture_Aliasing_ColorAndDepth_SeparateGroups()
    {
        // ColorRT と DepthRT は同寸法でも別 Kind → 別グループ。
        var rg = new Rg();
        var outBuf = rg.ImportBufferForTest("outBuf");
        var color1 = rg.CreateTextureForTest(new TextureDesc(128, 128, GpuFormat.Rgba8Unorm, TextureKind.Color), "color1");
        var depth1 = rg.CreateTextureForTest(new TextureDesc(128, 128, GpuFormat.D32Float, TextureKind.Depth), "depth1");
        var color2 = rg.CreateTextureForTest(new TextureDesc(128, 128, GpuFormat.Rgba8Unorm, TextureKind.Color), "color2");
        var depth2 = rg.CreateTextureForTest(new TextureDesc(128, 128, GpuFormat.D32Float, TextureKind.Depth), "depth2");

        rg.AddPass("V1", PassQueue.Graphics)
          .Write(color1, TextureUsage.ColorAttachment).Write(depth1, TextureUsage.DepthAttachment)
          .Write(outBuf, ResourceUsage.CopyDest)
          .Execute(_ => { });
        rg.AddPass("V2", PassQueue.Graphics)
          .Write(color2, TextureUsage.ColorAttachment).Write(depth2, TextureUsage.DepthAttachment)
          .Write(outBuf, ResourceUsage.CopyDest)
          .Execute(_ => { });

        rg.CompileForTest();
        // 各カテゴリ内で 1 物理スロットに alias
        Assert.Equal(rg.GetPhysicalSlot(color1), rg.GetPhysicalSlot(color2));
        Assert.Equal(rg.GetPhysicalSlot(depth1), rg.GetPhysicalSlot(depth2));
        Assert.True(rg.IsAliased(color1));
        Assert.True(rg.IsAliased(color2));
        Assert.True(rg.IsAliased(depth1));
        Assert.True(rg.IsAliased(depth2));
    }

    [Fact]
    public void Texture_UsageMapping()
    {
        Assert.Equal(GpuStage.ColorOutput, TextureUsage.ColorAttachment.Stage());
        Assert.Equal(GpuStage.DepthStencil, TextureUsage.DepthAttachment.Stage());
        Assert.Equal(GpuStage.PixelShader, TextureUsage.SampledPixel.Stage());
        Assert.Equal(GpuStage.Copy, TextureUsage.CopyDest.Stage());

        Assert.True(TextureUsage.ColorAttachment.IsWrite());
        Assert.True(TextureUsage.DepthAttachment.IsWrite());
        Assert.True(TextureUsage.CopyDest.IsWrite());
        Assert.False(TextureUsage.SampledPixel.IsWrite());
        Assert.False(TextureUsage.CopySource.IsWrite());
    }
}
