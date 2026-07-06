namespace Luxel.TwoD;

/// <summary>エンコード済みシーン (GPU 常駐の SoA バッファ群)。</summary>
public sealed class EncodedScene : IDisposable
{
    internal GpuBuffer Segments { get; }
    internal GpuBuffer Paths { get; }
    internal GpuBuffer Transforms { get; }
    internal GpuBuffer Styles { get; }
    internal GpuBuffer Clips { get; }
    internal GpuBuffer Order { get; }
    internal uint OrderCount { get; }

    internal EncodedScene(GpuBuffer segments, GpuBuffer paths, GpuBuffer transforms,
        GpuBuffer styles, GpuBuffer clips, GpuBuffer order, uint orderCount)
    {
        Segments = segments; Paths = paths; Transforms = transforms;
        Styles = styles; Clips = clips; Order = order; OrderCount = orderCount;
    }

    public void Dispose()
    {
        Segments.Dispose(); Paths.Dispose(); Transforms.Dispose();
        Styles.Dispose(); Clips.Dispose(); Order.Dispose();
    }
}

/// <summary>
/// コンピュートベースの 2D ベクターラスタライザ。SoA バッファ (Segment/Path/Transform/Style/Clip/Order)
/// を per-path 間接化したコンピュートシェーダで塗る。即時モード (Scene2D) と保持型 (RetainedCanvas) の
/// 共通バックエンド。
/// </summary>
public sealed class Rasterizer2D : IDisposable
{
    private readonly GpuDevice _device;
    private readonly GpuPipeline _bounds, _bin, _fine;
    private bool _disposed;

    // タイルビニング (RA-M1) の scratch。ラスタライザ 1 個 = 1 スレッドで直列使用が前提
    // (各 Render は自前 cmd + SubmitAndWait なので、次の DispatchRaster までに前の GPU 使用は完了)。
    private const uint TileSize = 16;
    private const uint TileCap = 64;   // タイルあたりのパスリスト容量。超過タイルは fine が全走査
    private GpuBuffer? _boundsBuf, _tileList, _tileCountBuf;
    private uint _boundsCap, _tilesCap;

    public Rasterizer2D(GpuDevice device)
    {
        _device = device;
        _bounds = device.CreateComputePipeline(GpuShaderCode.Load("raster2d_bounds"));
        _bin = device.CreateComputePipeline(GpuShaderCode.Load("raster2d_bin"));
        _fine = device.CreateComputePipeline(GpuShaderCode.Load("raster2d_fine"));
    }

    public GpuDevice Device => _device;

    /// <summary>即時モード: シーンを SoA バッファへ 1 回エンコードする (恒等変換/順次 Order)。</summary>
    public EncodedScene Encode(Scene2D scene)
    {
        (GpuSegment[] segs, GpuPath[] paths, GpuStyle[] styles) = PathEncoder.Encode(scene);
        if (segs.Length == 0) segs = new GpuSegment[1];
        if (paths.Length == 0) { paths = new GpuPath[1]; styles = new GpuStyle[1]; }

        var transforms = new[] { GpuTransform.Identity };   // 全パス共通の恒等変換
        var order = new uint[paths.Length];
        for (uint i = 0; i < order.Length; i++) order[i] = i;
        var clips = new GpuClip[1];                          // 即時モードはクリップ無し

        GpuBuffer segBuf = Upload(segs, 32);
        GpuBuffer pathBuf = Upload(paths, GpuPath.SizeBytes);
        GpuBuffer tfBuf = Upload(transforms, 32);
        GpuBuffer styBuf = Upload(styles, 16);
        GpuBuffer clipBuf = Upload(clips, 16);
        GpuBuffer orderBuf = Upload(order, 4);

        return new EncodedScene(segBuf, pathBuf, tfBuf, styBuf, clipBuf, orderBuf, (uint)paths.Length);
    }

    internal GpuBuffer Upload<T>(T[] data, int stride) where T : unmanaged
    {
        GpuBuffer buf = _device.Malloc((ulong)(Math.Max(1, data.Length) * stride), GpuMemoryKind.HostMapped);
        if (data.Length > 0) data.AsSpan().CopyTo(buf.Span<T>(data.Length));
        return buf;
    }

    /// <summary>シーンを framebuffer (RGBA8) へラスタライズする。
    /// transparent=true で背景を premultiplied alpha に (未描画ピクセルは alpha=0)。</summary>
    public void Render(GpuCommandBuffer cmd, EncodedScene scene, Camera2D camera,
        uint width, uint height, GpuBuffer framebuffer, bool transparent = false)
        => DispatchRaster(cmd, scene.Segments, scene.Paths, scene.Transforms, scene.Styles,
            scene.Clips, scene.Order, scene.OrderCount, camera, width, height, framebuffer, transparent);

    /// <summary>SoA バッファ群を直接指定してラスタライズ (即時モード・保持型 共通)。
    /// 3 段: bounds (パス毎の画面 AABB 前計算) → bin (タイル毎パスリスト、描画順保存) →
    /// fine (自タイルのリストだけ走査) — per-pixel の全 order 走査 (O(px × paths)) を
    /// O(tiles × paths + px × tile内paths) へ落とす。</summary>
    internal void DispatchRaster(GpuCommandBuffer cmd, GpuBuffer seg, GpuBuffer path, GpuBuffer tf,
        GpuBuffer sty, GpuBuffer clip, GpuBuffer order, uint orderCount,
        Camera2D camera, uint width, uint height, GpuBuffer framebuffer, bool transparent = false)
    {
        uint tilesX = (width + TileSize - 1) / TileSize;
        uint tilesY = (height + TileSize - 1) / TileSize;
        uint tiles = tilesX * tilesY;
        EnsureScratch(orderCount, tiles);

        var args = new RasterArgs
        {
            A = camera.A,
            B = camera.B,
            C = camera.C,
            D = camera.D,
            E = camera.E,
            F = camera.F,
            Width = width,
            Height = height,
            SegIndex = seg.BindlessIndex,
            PathIndex = path.BindlessIndex,
            TransformIndex = tf.BindlessIndex,
            StyleIndex = sty.BindlessIndex,
            ClipIndex = clip.BindlessIndex,
            OrderIndex = order.BindlessIndex,
            OrderCount = orderCount,
            FbIndex = framebuffer.BindlessIndex,
            BgMode = transparent ? 1u : 0u,
            TilesX = tilesX,
            TileCap = TileCap,
            BoundsIndex = _boundsBuf!.BindlessIndex,
            TileListIndex = _tileList!.BindlessIndex,
            TileCountIndex = _tileCountBuf!.BindlessIndex,
        };
        cmd.SetComputePipeline(_bounds)
           .SetRootArguments(args)
           .Dispatch(Math.Max(1, (orderCount + 63) / 64), 1)
           .Barrier(GpuStage.ComputeShader, GpuStage.ComputeShader)
           .SetComputePipeline(_bin)
           .SetRootArguments(args)
           .Dispatch((tiles + 63) / 64, 1)
           .Barrier(GpuStage.ComputeShader, GpuStage.ComputeShader)
           .SetComputePipeline(_fine)
           .SetRootArguments(args)
           .Dispatch((width + 7) / 8, (height + 7) / 8)
           .Barrier(GpuStage.ComputeShader, GpuStage.All);
    }

    /// <summary>ビニング scratch を必要サイズまで成長させる (縮めない)。DeviceLocal — CPU は触らない。</summary>
    private void EnsureScratch(uint orderCount, uint tiles)
    {
        if (_boundsBuf is null || orderCount > _boundsCap)
        {
            _boundsCap = Math.Max(256, System.Numerics.BitOperations.RoundUpToPowerOf2(Math.Max(1, orderCount)));
            _boundsBuf?.Dispose();
            _boundsBuf = _device.Malloc((ulong)_boundsCap * 16, GpuMemoryKind.DeviceLocal);
        }
        if (_tileList is null || tiles > _tilesCap)
        {
            _tilesCap = Math.Max(1024, System.Numerics.BitOperations.RoundUpToPowerOf2(tiles));
            _tileList?.Dispose();
            _tileList = _device.Malloc((ulong)_tilesCap * TileCap * 4, GpuMemoryKind.DeviceLocal);
            _tileCountBuf?.Dispose();
            _tileCountBuf = _device.Malloc((ulong)_tilesCap * 4, GpuMemoryKind.DeviceLocal);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bounds.Dispose(); _bin.Dispose(); _fine.Dispose();
        _boundsBuf?.Dispose(); _tileList?.Dispose(); _tileCountBuf?.Dispose();
    }
}
