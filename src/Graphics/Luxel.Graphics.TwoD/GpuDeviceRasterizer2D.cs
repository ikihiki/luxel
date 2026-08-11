namespace Luxel.Graphics.TwoD;

/// <summary>GPUコマンド記録用の2D描画target。submit/waitは呼び出し側が行う。</summary>
public sealed class GpuRasterTarget2D : IRasterTarget2D
{
    public GpuRasterTarget2D(GpuCommandBuffer commandBuffer, GpuBuffer framebuffer, uint width, uint height)
    {
        CommandBuffer = commandBuffer ?? throw new ArgumentNullException(nameof(commandBuffer));
        Framebuffer = framebuffer ?? throw new ArgumentNullException(nameof(framebuffer));
        Width = width;
        Height = height;
    }

    public GpuCommandBuffer CommandBuffer { get; }
    public GpuBuffer Framebuffer { get; }
    public uint Width { get; }
    public uint Height { get; }
}

/// <summary>エンコード済みシーン (GPU 常駐の SoA バッファ群)。</summary>
public class GpuEncodedScene2D : IRasterScene2D
{
    private bool _disposed;
    internal GpuBuffer Segments { get; }
    internal GpuBuffer Paths { get; }
    internal GpuBuffer Transforms { get; }
    internal GpuBuffer Styles { get; }
    internal GpuBuffer Clips { get; }
    internal GpuBuffer Order { get; }
    internal uint OrderCount { get; }

    internal GpuEncodedScene2D(GpuDeviceRasterizer2D owner, GpuBuffer segments, GpuBuffer paths, GpuBuffer transforms,
        GpuBuffer styles, GpuBuffer clips, GpuBuffer order, uint orderCount)
    {
        Rasterizer = owner;
        Segments = segments; Paths = paths; Transforms = transforms;
        Styles = styles; Clips = clips; Order = order; OrderCount = orderCount;
    }

    public GpuDeviceRasterizer2D Rasterizer { get; }
    IRasterizer2D IRasterScene2D.Rasterizer => Rasterizer;

    public void Render(Camera2D camera, IRasterTarget2D target, bool transparent = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (target is not GpuRasterTarget2D gpu)
            throw new ArgumentException("GpuEncodedScene2D requires GpuRasterTarget2D.", nameof(target));
        Rasterizer.Render(gpu.CommandBuffer, this, camera, gpu.Width, gpu.Height, gpu.Framebuffer, transparent);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Segments.Dispose(); Paths.Dispose(); Transforms.Dispose();
        Styles.Dispose(); Clips.Dispose(); Order.Dispose();
    }
}

/// <summary>
/// コンピュートベースの 2D ベクターラスタライザ。SoA バッファ (Segment/Path/Transform/Style/Clip/Order)
/// を per-path 間接化したコンピュートシェーダで塗る。即時モード (Scene2D) と保持型 (RetainedCanvas) の
/// 共通バックエンド。
/// </summary>
public class GpuDeviceRasterizer2D : IRasterizer2D
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

    public GpuDeviceRasterizer2D(GpuDevice device)
        : this(device, name => GpuShaderCode.Load(name)) { }

    /// <summary>シェーダ供給元を注入する。browser/AOT の EmbeddedResource 利用など filesystem 非依存 host 向け。</summary>
    public GpuDeviceRasterizer2D(GpuDevice device, Func<string, GpuShaderCode> shaderProvider)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        ArgumentNullException.ThrowIfNull(shaderProvider);
        _bounds = device.CreateComputePipeline(shaderProvider("raster2d_bounds"));
        _bin = device.CreateComputePipeline(shaderProvider("raster2d_bin"));
        _fine = device.CreateComputePipeline(shaderProvider("raster2d_fine"));
    }

    public string Name => "GpuDevice";
    public Rasterizer2DCapabilities Capabilities => Rasterizer2DCapabilities.GpuCommandRecording
        | Rasterizer2DCapabilities.BindlessImages
        | Rasterizer2DCapabilities.RetainedIncrementalUpdates;

    IRasterScene2D IRasterizer2D.CreateScene(Scene2D scene) => Encode(scene);
    public IRasterScene2D CreateScene(RetainedCanvas canvas)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new GpuRetainedRasterScene2D(this, canvas ?? throw new ArgumentNullException(nameof(canvas)));
    }

    public GpuDevice Device => _device;

    /// <summary>即時モード: シーンを SoA バッファへ 1 回エンコードする (恒等変換/順次 Order)。</summary>
    public GpuEncodedScene2D Encode(Scene2D scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
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

        return new GpuEncodedScene2D(this, segBuf, pathBuf, tfBuf, styBuf, clipBuf, orderBuf, (uint)paths.Length);
    }

    internal GpuBuffer Upload<T>(T[] data, int stride) where T : unmanaged
    {
        GpuBuffer buf = _device.Malloc((ulong)(Math.Max(1, data.Length) * stride), GpuMemoryKind.HostMapped);
        if (data.Length > 0) data.AsSpan().CopyTo(buf.Span<T>(data.Length));
        return buf;
    }

    /// <summary>シーンを framebuffer (RGBA8) へラスタライズする。
    /// transparent=true で背景を premultiplied alpha に (未描画ピクセルは alpha=0)。</summary>
    public void Render(GpuCommandBuffer cmd, GpuEncodedScene2D scene, Camera2D camera,
        uint width, uint height, GpuBuffer framebuffer, bool transparent = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        if (!ReferenceEquals(scene.Rasterizer, this))
            throw new ArgumentException("The encoded scene belongs to a different GPU rasterizer/device.", nameof(scene));
        DispatchRaster(cmd, scene.Segments, scene.Paths, scene.Transforms, scene.Styles,
            scene.Clips, scene.Order, scene.OrderCount, camera, width, height, framebuffer, transparent);
    }

    /// <summary>SoA バッファ群を直接指定してラスタライズ (即時モード・保持型 共通)。
    /// 3 段: bounds (パス毎の画面 AABB 前計算) → bin (タイル毎パスリスト、描画順保存) →
    /// fine (自タイルのリストだけ走査) — per-pixel の全 order 走査 (O(px × paths)) を
    /// O(tiles × paths + px × tile内paths) へ落とす。</summary>
    internal void DispatchRaster(GpuCommandBuffer cmd, GpuBuffer seg, GpuBuffer path, GpuBuffer tf,
        GpuBuffer sty, GpuBuffer clip, GpuBuffer order, uint orderCount,
        Camera2D camera, uint width, uint height, GpuBuffer framebuffer, bool transparent = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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

internal sealed class GpuRetainedRasterScene2D : IRasterScene2D, IRetainedCanvasSink
{
    private readonly RetainedCanvas _canvas;
    private GpuBuffer? _segments, _paths, _transforms, _styles, _clips, _order;
    private int _segmentCapacity, _pathCapacity, _transformCapacity, _styleCapacity, _clipCapacity, _orderCapacity;
    private uint _orderCount;
    private bool _disposed;

    public GpuRetainedRasterScene2D(GpuDeviceRasterizer2D rasterizer, RetainedCanvas canvas)
    {
        Rasterizer = rasterizer;
        _canvas = canvas;
        _canvas.RegisterSink(this);
        FullSync(_canvas);
    }

    public GpuDeviceRasterizer2D Rasterizer { get; }
    IRasterizer2D IRasterScene2D.Rasterizer => Rasterizer;

    public void Render(Camera2D camera, IRasterTarget2D target, bool transparent = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (target is not GpuRasterTarget2D gpu)
            throw new ArgumentException("GPU retained scenes require GpuRasterTarget2D.", nameof(target));

        _canvas.Flush(gpu.Width, gpu.Height);
        Rasterizer.DispatchRaster(gpu.CommandBuffer, _segments!, _paths!, _transforms!, _styles!, _clips!, _order!,
            _orderCount, camera, gpu.Width, gpu.Height, gpu.Framebuffer, transparent);
        _canvas.EmitRenderDiagnostics();
    }

    public void FullSync(RetainedCanvas canvas)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Replace(ref _segments, ref _segmentCapacity, canvas.SegmentSnapshot(), 32);
        Replace(ref _paths, ref _pathCapacity, canvas.PathSnapshot(), GpuPath.SizeBytes);
        Replace(ref _transforms, ref _transformCapacity, canvas.TransformSnapshot(), 32);
        Replace(ref _styles, ref _styleCapacity, canvas.StyleSnapshot(), 16);
        Replace(ref _clips, ref _clipCapacity, canvas.ClipSnapshot(), 16);
        Replace(ref _order, ref _orderCapacity, canvas.OrderSnapshot(), 4);
        _orderCount = canvas.OrderCount;
    }

    public void WriteTransform(int index, GpuTransform value) => Write(_transforms, _transformCapacity, index, value);
    public void WriteStyle(int index, GpuStyle value) => Write(_styles, _styleCapacity, index, value);
    public void WriteClip(int index, GpuClip value) => Write(_clips, _clipCapacity, index, value);
    public void WriteSegment(int index, GpuSegment value) => Write(_segments, _segmentCapacity, index, value);
    public void WritePath(int index, GpuPath value) => Write(_paths, _pathCapacity, index, value);

    public void WriteOrder(uint[] order)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_order is null || order.Length > _orderCapacity)
            Replace(ref _order, ref _orderCapacity, order.Length > 0 ? order : new uint[1], 4);
        else if (order.Length > 0)
            order.AsSpan().CopyTo(_order.Span<uint>(_orderCapacity));
        _orderCount = (uint)order.Length;
    }

    private void Replace<T>(ref GpuBuffer? buffer, ref int capacity, T[] data, int stride) where T : unmanaged
    {
        buffer?.Dispose();
        buffer = Rasterizer.Upload(data, stride);
        capacity = Math.Max(1, data.Length);
    }

    private void Write<T>(GpuBuffer? buffer, int capacity, int index, T value) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (buffer is null || (uint)index >= (uint)capacity)
            throw new InvalidOperationException("Retained GPU buffer is not synchronized with its canvas.");
        buffer.Span<T>(capacity)[index] = value;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _canvas.UnregisterSink(this);
        _segments?.Dispose(); _paths?.Dispose(); _transforms?.Dispose();
        _styles?.Dispose(); _clips?.Dispose(); _order?.Dispose();
        _segments = _paths = _transforms = _styles = _clips = _order = null;
    }
}
