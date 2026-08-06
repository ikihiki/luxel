using System.Runtime.InteropServices;
using Luxel.Graphics.RenderGraph;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// RenderGraph のデモ — Setup/Compile/Execute 三相、transient バッファの aliasing、
/// デッドパスカリング。グラフ層は scene-agnostic (GpuDeviceRasterizer2D / Scene2D を知らない —
/// ImportBuffer / CreateBuffer だけ)。
/// </summary>
public static class RenderGraphStories
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BlurArgs
    {
        public uint SrcIndex, DstIndex, Width, Height;
        public int DirX, DirY;
        public uint Pad0, Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositeArgs
    {
        public uint UiIndex, BlurIndex, DstIndex, Width, Height, SplitX, Pad0, Pad1;
    }

    /// <summary>反復ブラー 4 段 + 誰も読まないパスで **transient aliasing** と
    /// **デッドパスカリング** を実証。コンパイル結果 (物理バッファ数/実行パス数/alias) は
    /// Log パネルに出る。</summary>
    [Story("Examples/RenderGraph/Aliasing", Height = 320, Order = 131)]
    public static Widget Aliasing(StoryContext ctx)
        => Frame(GpuSceneBase.View(256, 256, new BlurScene(stages: 2, addDeadPass: true, log: ctx.Log), animated: false));

    /// <summary>分離ガウシアン × N 段の RenderGraph シーン。stages=2 で中間バッファの寿命が
    /// 重ならず物理 2 本に alias される (tmp1↔tmp2 / blur1↔blur2)。</summary>
    private sealed class BlurScene(int stages, bool addDeadPass = false, Action<string>? log = null)
        : GpuSceneBase
    {
        private GpuDeviceRasterizer2D _raster = null!;
        private GpuEncodedScene2D _encoded = null!;
        private GpuBuffer _ui = null!;
        private GpuPipeline _blur = null!, _composite = null!;

        protected override void OnInit()
        {
            // デモ UI (角丸矩形 5 枚) を Scene2D で作り、ui バッファへ 1 回ラスタライズ
            _raster = Track(new GpuDeviceRasterizer2D(Device));
            var scene = new Scene2D();
            scene.FillRoundedRect(Color2D.Rgba(60, 130, 240, 255), 20, 20, 100, 60, 12);
            scene.FillRoundedRect(Color2D.Rgba(230, 80, 100, 255), 140, 30, 90, 90, 16);
            scene.FillRoundedRect(Color2D.Rgba(40, 200, 120, 255), 30, 110, 120, 40, 8);
            scene.FillRoundedRect(Color2D.Rgba(255, 220, 60, 255), 170, 140, 60, 90, 10);
            scene.FillRoundedRect(Color2D.Rgba(180, 90, 220, 255), 20, 170, 130, 60, 14);
            _encoded = Track(_raster.Encode(scene));
            _ui = Track(Device.Malloc((ulong)(W * H * 4), GpuMemoryKind.HostMapped));
            _ui.Span<byte>((int)(W * H * 4)).Clear();
            _blur = Track(Device.CreateComputePipeline(GpuShaderCode.Load("compute_blur")));
            _composite = Track(Device.CreateComputePipeline(GpuShaderCode.Load("compute_composite")));

            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            _raster.Render(cmd, _encoded, Camera2D.Pixels, W, H, _ui);
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
        }

        protected override void OnRender(float time)
        {
            ulong fbBytes = (ulong)(W * H * 4);
            using var rg = new Luxel.Graphics.RenderGraph.RenderGraph(Device);   // グラフは 1 フレーム使い切り

            BufferHandle hUi = rg.ImportBuffer(_ui, "ui");
            BufferHandle hFinal = rg.ImportBuffer(OutBuffer, "final");

            // BlurH → BlurV を stages 回連鎖。tmpN/blurN は transient — 前段の寿命が終わった
            // 領域を後段が使い回す (同形なら物理 slot を共有)
            BufferHandle src = hUi;
            var chain = new List<BufferHandle>();
            for (int i = 0; i < stages; i++)
            {
                BufferHandle tmp = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), $"tmp{i + 1}");
                BufferHandle blr = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), $"blur{i + 1}");
                chain.Add(tmp);
                chain.Add(blr);
                BufferHandle s0 = src;
                rg.AddPass($"BlurH{i + 1}", PassQueue.Compute).Read(s0).Write(tmp)
                  .Execute(c => Dispatch(c, _blur, new BlurArgs
                  {
                      SrcIndex = c.BindlessIndex(s0),
                      DstIndex = c.BindlessIndex(tmp),
                      Width = W,
                      Height = H,
                      DirX = 1,
                      DirY = 0,
                  }));
                rg.AddPass($"BlurV{i + 1}", PassQueue.Compute).Read(tmp).Write(blr)
                  .Execute(c => Dispatch(c, _blur, new BlurArgs
                  {
                      SrcIndex = c.BindlessIndex(tmp),
                      DstIndex = c.BindlessIndex(blr),
                      Width = W,
                      Height = H,
                      DirX = 0,
                      DirY = 1,
                  }));
                src = blr;
            }

            BufferHandle hBlurred = src;
            rg.AddPass("Composite", PassQueue.Compute).Read(hUi).Read(hBlurred).Write(hFinal)
              .Execute(c => Dispatch(c, _composite, new CompositeArgs
              {
                  UiIndex = c.BindlessIndex(hUi),
                  BlurIndex = c.BindlessIndex(hBlurred),
                  DstIndex = c.BindlessIndex(hFinal),
                  Width = W,
                  Height = H,
                  SplitX = W / 2,
              }));

            if (addDeadPass)
            {
                // 誰も読まない → コンパイル相のカリングで実行されない (Log で確認できる)
                BufferHandle hDead = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), "dead");
                rg.AddPass("DeadPass", PassQueue.Compute).Write(hDead)
                  .Execute(_ => log?.Invoke("BUG: DeadPass が実行された (カリングされるはず)"));
            }

            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            rg.Execute(cmd);
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);

            if (log is not null)
            {
                log($"transient 論理 {chain.Count + (addDeadPass ? 1 : 0)} 本 → 物理 {rg.PhysicalTransientBufferCount} 本 (alias)");
                log($"実行パス {rg.LastExecutedPassCount} / 登録 {stages * 2 + 1 + (addDeadPass ? 1 : 0)}"
                    + (addDeadPass ? " — DeadPass culled" : ""));
                for (int i = 0; i + 1 < chain.Count; i += 2)
                    log($"tmp{i / 2 + 1}: slot {rg.GetPhysicalSlot(chain[i])}, blur{i / 2 + 1}: slot {rg.GetPhysicalSlot(chain[i + 1])}"
                        + (rg.IsAliased(chain[i]) ? " (aliased)" : ""));
            }
        }

        private void Dispatch<T>(PassContext c, GpuPipeline pipeline, T args) where T : unmanaged
            => c.Cmd.SetComputePipeline(pipeline).SetRootArguments(args)
                    .Dispatch((W + 7) / 8, (H + 7) / 8);
    }
}
