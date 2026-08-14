using Luxel.Diagnostics;

namespace Luxel.Graphics.RenderGraph;

/// <summary>
/// レンダーグラフ (RG-M1: Setup/Compile/Execute 三相 + バッファ寿命解析 + 自動 stage バリア)。
/// パスとリソース依存を builder で宣言し、<see cref="Compile"/> でデッドパスカリング/物理リソース割当/バリア計算を行い、
/// <see cref="Execute"/> で per-pass コールバックを順に駆動する。
///
/// 設計上、シーン側 (RetainedCanvas/UI/ECS) は一切知らない。入力は GPU ハンドル (BufferHandle) のみ。
/// </summary>
public sealed class RenderGraph : IDisposable
{
    private static long s_nextGraphId;

    private readonly long _graphId = Interlocked.Increment(ref s_nextGraphId);
    private readonly GpuDevice? _device;
    private readonly List<GpuBuffer> _ownedTransientBuffers = new();
    private readonly List<GpuTexture> _ownedTransientTextures = new();
    private readonly List<ResourceRecord> _resources = new() { null! };   // index 0 = invalid sentinel
    private readonly List<PassRecord> _passes = new();
    private readonly Dictionary<RenderResourceSlotId, SymbolicSlotBinding> _symbolicSlots = new();
    private readonly HashSet<RenderResourceVersionId> _exports = new();
    private int _nextPassKey;
    private bool _compiled;
    private CompiledGraph? _compiled_;
    private bool _disposed;

    public RenderGraph(GpuDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    /// <summary>
    /// テスト専用コンストラクタ。GPU デバイスなしで Setup と寿命解析だけを検証する。
    /// Execute は不可 (物理リソース未割当)。
    /// </summary>
    internal RenderGraph()
    {
        _device = null;
    }

    // === Setup 相 ===========================================================

    /// <summary>External な既存 <see cref="GpuBuffer"/> をグラフに取り込む。寿命/解放はユーザー責任。</summary>
    public BufferHandle ImportBuffer(GpuBuffer buffer, string name = "external")
    {
        ThrowIfCompiled();
        var rec = new ResourceRecord
        {
            Name = name,
            Kind = ResourceKind.ExternalBuffer,
            Id = _resources.Count,
            ExternalBuffer = buffer ?? throw new ArgumentNullException(nameof(buffer)),
        };
        _resources.Add(rec);
        return new BufferHandle(rec.Id, _graphId);
    }

    /// <summary>Transient バッファを宣言する。物理メモリは Compile 相で割り当てる。</summary>
    public BufferHandle CreateBuffer(BufferDesc desc, string name = "transient")
    {
        ThrowIfCompiled();
        if (desc.SizeBytes == 0) throw new ArgumentException("SizeBytes は 0 不可", nameof(desc));
        var rec = new ResourceRecord
        {
            Name = name,
            Kind = ResourceKind.TransientBuffer,
            Id = _resources.Count,
            TransientBufferDesc = desc,
        };
        _resources.Add(rec);
        return new BufferHandle(rec.Id, _graphId);
    }

    /// <summary>External な既存 <see cref="GpuTexture"/> をグラフに取り込む。寿命/解放はユーザー責任。</summary>
    public TextureHandle ImportTexture(GpuTexture texture, string name = "externalTex")
    {
        ThrowIfCompiled();
        var rec = new ResourceRecord
        {
            Name = name,
            Kind = ResourceKind.ExternalTexture,
            Id = _resources.Count,
            ExternalTexture = texture ?? throw new ArgumentNullException(nameof(texture)),
        };
        _resources.Add(rec);
        return new TextureHandle(rec.Id, _graphId);
    }

    /// <summary>Transient テクスチャを宣言する。物理リソースは Compile 相で割り当て、同形なら aliasing。</summary>
    public TextureHandle CreateTexture(TextureDesc desc, string name = "transientTex")
    {
        ThrowIfCompiled();
        if (desc.Width == 0 || desc.Height == 0) throw new ArgumentException("Width/Height は 0 不可", nameof(desc));
        var rec = new ResourceRecord
        {
            Name = name,
            Kind = ResourceKind.TransientTexture,
            Id = _resources.Count,
            TransientTextureDesc = desc,
        };
        _resources.Add(rec);
        return new TextureHandle(rec.Id, _graphId);
    }

    /// <summary>stable symbolic slot を buffer handle に束縛する。</summary>
    public void DeclareBuffer(RenderResourceSlotId slot, BufferHandle handle)
    {
        ThrowIfCompiled();
        ValidateSlot(slot, nameof(slot));
        ValidateBufferHandle(handle, nameof(handle));
        AddSymbolicSlot(slot, new SymbolicSlotBinding(handle.Id, IsTexture: false));
    }

    /// <summary>stable symbolic slot を texture handle に束縛する。</summary>
    public void DeclareTexture(RenderResourceSlotId slot, TextureHandle handle)
    {
        ThrowIfCompiled();
        ValidateSlot(slot, nameof(slot));
        ValidateTextureHandle(handle, nameof(handle));
        AddSymbolicSlot(slot, new SymbolicSlotBinding(handle.Id, IsTexture: true));
    }

    /// <summary>symbolic resource version を graph output として保持する。</summary>
    public void Export(RenderResourceVersionId version)
    {
        ThrowIfCompiled();
        ValidateSymbolicVersion(version, nameof(version));
        _exports.Add(version);
    }

    /// <summary>新しいパスを追加する。返り値の builder で Read/Write/Execute を宣言する。</summary>
    public PassBuilder AddPass(string name, PassQueue queue = PassQueue.Graphics)
        => AddPass(new RenderPassKey($"__graph_{_graphId}_pass_{_nextPassKey++}"), name, queue);

    /// <summary>stable key を持つ新しいパスを追加する。</summary>
    public PassBuilder AddPass(RenderPassKey key, string name, PassQueue queue = PassQueue.Graphics)
    {
        ThrowIfCompiled();
        ValidatePassKey(key, nameof(key));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("pass name は空にできません。", nameof(name));
        var rec = new PassRecord
        {
            Name = name,
            Queue = queue,
            Index = -1,
            Key = key,
        };
        return new PassBuilder(this, rec);
    }

    internal void AddPassInternal(PassRecord record)
    {
        record.Index = _passes.Count;
        _passes.Add(record);
    }

    // === Compile + Execute ===================================================

    /// <summary>
    /// グラフを compile し、即座に execute する。1 フレームで 1 度呼ぶ想定。
    /// 内部で寿命解析 → 物理リソース割当 → パス順に execute + 自動バリア挿入を行う。
    /// </summary>
    public void Execute(GpuCommandBuffer cmd)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(cmd);

        if (!_compiled)
        {
            _compiled_ = Compile();
            _compiled = true;
        }

        var compiled = _compiled_!;
        var ctx = new PassContext(this, cmd);

        // 物理リソース単位で最後の使用 (stage, isWrite) を追跡する。
        // Aliasing で複数の論理リソースが同じ物理 buffer/texture を共有するため、論理ハンドル単位で追跡すると
        // alias 境界でバリアが漏れる。Key は object reference (GpuBuffer or GpuTexture)。
        var physState = new Dictionary<object, (GpuStage Stage, bool IsWrite)>(ReferenceEqualityComparer.Instance);

        int executedCount = 0;
        for (int i = 0; i < compiled.Order.Count; i++)
        {
            var pass = compiled.Order[i];
            if (pass.Culled) continue;
            executedCount++;

            // === バリア計算 ===
            // 依存リソースの直前の使用を物理バッファ単位で参照し、ハザードを判定:
            //   prev=W → cur=R/W : RAW/WAW
            //   prev=R → cur=W   : WAR
            //   prev=R → cur=R   : ハザードなし
            GpuStage srcStage = GpuStage.None;
            GpuStage dstStage = GpuStage.None;
            foreach (var acc in EnumerateAllAccesses(pass))
            {
                var res = _resources[acc.ResourceId];
                object? phys = res.IsBuffer ? (object?)res.PhysicalBuffer : res.PhysicalTexture;
                if (phys == null) continue;
                if (!physState.TryGetValue(phys, out var prev)) continue;
                bool curIsWrite = acc.IsWrite();
                if (prev.IsWrite || curIsWrite)  // R-after-R 以外はバリア
                {
                    srcStage |= prev.Stage;
                    dstStage |= acc.Stage();
                }
            }
            if (srcStage != GpuStage.None && dstStage != GpuStage.None)
            {
                cmd.Barrier(srcStage, dstStage);
            }

            // === パス本体 ===
            pass.Body?.Invoke(ctx);

            // === 使用状況の更新 (物理単位) ===
            foreach (var acc in EnumerateAllAccesses(pass))
            {
                var res = _resources[acc.ResourceId];
                object? phys = res.IsBuffer ? (object?)res.PhysicalBuffer : res.PhysicalTexture;
                if (phys == null) continue;
                physState[phys] = (acc.Stage(), acc.IsWrite());
            }
        }

        // External リソースが最終的に書かれた場合、後段 (Copy/Present 等) からの可視化を保証するため
        // 終端バリアを一つ流す (粗いが安全な保守的挿入)。
        GpuStage finalSrc = GpuStage.None;
        foreach (var r in _resources)
        {
            if (r == null) continue;
            if (r.Kind != ResourceKind.ExternalBuffer && r.Kind != ResourceKind.ExternalTexture) continue;
            object? phys = r.IsBuffer ? (object?)r.PhysicalBuffer : r.PhysicalTexture;
            if (phys == null) continue;
            if (physState.TryGetValue(phys, out var st) && st.IsWrite)
                finalSrc |= st.Stage;
        }
        if (finalSrc != GpuStage.None)
        {
            cmd.Barrier(finalSrc, GpuStage.All);
        }

        LastExecutedPassCount = executedCount;

        // === 計装イベント発行 (Luxel.DevTools 等が購読) ===
        if (EngineDiagnostics.IsEnabled(EngineDiagnostics.RenderGraph))
        {
            EngineDiagnostics.Emit(EngineDiagnostics.RenderGraph, BuildDiagnostic());
        }
    }

    /// <summary>計装用に Compile 後の最終形をスナップショット。テストからも呼べる。</summary>
    public DiagRenderGraph BuildDiagnostic()
    {
        var passes = new DiagRenderGraphPass[_passes.Count];
        for (int i = 0; i < _passes.Count; i++)
        {
            var p = _passes[i];
            passes[i] = new DiagRenderGraphPass(
                Index: i,
                Name: p.Name,
                Queue: p.Queue.ToString(),
                Culled: p.Culled,
                Reads: p.Reads.Select(a => a.ResourceId).Distinct().ToArray(),
                Writes: p.Writes.Select(a => a.ResourceId).Distinct().ToArray());
        }
        var resources = new DiagRenderGraphResource[_resources.Count - 1];
        for (int i = 1; i < _resources.Count; i++)
        {
            var r = _resources[i];
            string kindStr = r.Kind switch
            {
                ResourceKind.ExternalBuffer => "External",
                ResourceKind.ExternalTexture => "ExternalTex",
                ResourceKind.TransientBuffer => "Transient",
                ResourceKind.TransientTexture => "TransientTex",
                _ => "?",
            };
            ulong sizeBytes = r.Kind switch
            {
                ResourceKind.TransientBuffer => r.TransientBufferDesc.SizeBytes,
                ResourceKind.TransientTexture => (ulong)(r.TransientTextureDesc.Width * r.TransientTextureDesc.Height * 4u),
                _ => 0UL,
            };
            resources[i - 1] = new DiagRenderGraphResource(
                Id: r.Id,
                Name: r.Name,
                Kind: kindStr,
                IsAliased: r.IsAliased,
                PhysicalSlot: r.PhysicalSlot,
                FirstWritePass: r.FirstWritePass,
                LastReadPass: r.LastReadPass,
                SizeBytes: sizeBytes);
        }
        return new DiagRenderGraph(passes, resources,
            _ownedTransientBuffers.Count + _ownedTransientTextures.Count, LastExecutedPassCount);
    }

    /// <summary>テスト/計測用: 直近 Execute で実際に駆動された (非 culled の) パス数。</summary>
    public int LastExecutedPassCount { get; private set; }

    /// <summary>テスト用に Compile のみ実行する (Execute なし)。</summary>
    internal CompiledGraph CompileForTest()
    {
        if (!_compiled)
        {
            _compiled_ = Compile();
            _compiled = true;
        }
        return _compiled_!;
    }

    /// <summary>
    /// Compile 相: symbolic dependency の解決、stable topological sort、culling、寿命解析、aliasing、物理割当。
    /// </summary>
    private CompiledGraph Compile()
    {
        var dependencies = BuildDependencyGraph();
        var order = StableTopologicalSort(dependencies);

        var required = FindRequiredPasses(dependencies);
        foreach (var pass in _passes) pass.Culled = !required.Contains(pass.Index);

        for (int i = 1; i < _resources.Count; i++)
        {
            var res = _resources[i];
            res.FirstWritePass = -1;
            res.LastReadPass = -1;
            res.PhysicalSlot = -1;
            res.IsAliased = false;
        }

        var aliveFirstWrite = new int[_resources.Count];
        var aliveLastUse = new int[_resources.Count];
        Array.Fill(aliveFirstWrite, -1);
        Array.Fill(aliveLastUse, -1);

        for (int p = 0; p < order.Count; p++)
        {
            var pass = order[p];
            foreach (var write in pass.Writes)
            {
                var resource = _resources[write.ResourceId];
                if (resource.FirstWritePass < 0) resource.FirstWritePass = p;
                if (!pass.Culled)
                {
                    if (aliveFirstWrite[write.ResourceId] < 0) aliveFirstWrite[write.ResourceId] = p;
                    aliveLastUse[write.ResourceId] = Math.Max(aliveLastUse[write.ResourceId], p);
                }
            }
            foreach (var read in pass.Reads)
            {
                var resource = _resources[read.ResourceId];
                resource.LastReadPass = Math.Max(resource.LastReadPass, p);
                if (!pass.Culled)
                    aliveLastUse[read.ResourceId] = Math.Max(aliveLastUse[read.ResourceId], p);
            }
        }

        // 4. Transient aliasing: (Size, Kind) ごとに interval scheduling で物理 slot 番号を決定。
        //    Slot 番号付けは GPU 非依存 (テストモードでも実行)。物理バッファの Malloc だけ device があるときに行う。
        // External は単純に固定参照。
        for (int i = 1; i < _resources.Count; i++)
        {
            var res = _resources[i];
            if (res.Kind == ResourceKind.ExternalBuffer) res.PhysicalBuffer = res.ExternalBuffer;
            else if (res.Kind == ResourceKind.ExternalTexture) res.PhysicalTexture = res.ExternalTexture;
        }

        // 4a. Buffer aliasing: (Size, MemoryKind) ごとに interval scheduling。
        var bufGroups = new Dictionary<(ulong, GpuMemoryKind), List<ResourceRecord>>();
        for (int i = 1; i < _resources.Count; i++)
        {
            var res = _resources[i];
            if (res.Kind != ResourceKind.TransientBuffer) continue;
            if (aliveFirstWrite[i] < 0) continue;
            var key = (res.TransientBufferDesc.SizeBytes, res.TransientBufferDesc.Kind);
            if (!bufGroups.TryGetValue(key, out var list)) bufGroups[key] = list = new();
            list.Add(res);
        }
        foreach (var kv in bufGroups)
        {
            var sorted = kv.Value.OrderBy(r => aliveFirstWrite[r.Id]).ToList();
            var slots = new List<(GpuBuffer? Buf, int FreeAfter)>();
            foreach (var res in sorted)
            {
                int start = aliveFirstWrite[res.Id];
                int end = aliveLastUse[res.Id];
                int reuseIdx = -1;
                for (int s = 0; s < slots.Count; s++)
                    if (slots[s].FreeAfter < start) { reuseIdx = s; break; }
                if (reuseIdx >= 0)
                {
                    res.PhysicalBuffer = slots[reuseIdx].Buf;
                    res.PhysicalSlot = reuseIdx;
                    slots[reuseIdx] = (slots[reuseIdx].Buf, end);
                }
                else
                {
                    GpuBuffer? fresh = null;
                    if (_device != null)
                    {
                        fresh = _device.Malloc(res.TransientBufferDesc.SizeBytes, res.TransientBufferDesc.Kind);
                        _ownedTransientBuffers.Add(fresh);
                    }
                    res.PhysicalBuffer = fresh;
                    res.PhysicalSlot = slots.Count;
                    slots.Add((fresh, end));
                }
            }
            foreach (var res in sorted)
            {
                int count = sorted.Count(o => o.PhysicalSlot == res.PhysicalSlot);
                if (count >= 2) res.IsAliased = true;
            }
        }

        // 4b. Texture aliasing: (Width, Height, Format, Kind) ごとに interval scheduling。
        var texGroups = new Dictionary<(uint, uint, GpuFormat, TextureKind), List<ResourceRecord>>();
        for (int i = 1; i < _resources.Count; i++)
        {
            var res = _resources[i];
            if (res.Kind != ResourceKind.TransientTexture) continue;
            if (aliveFirstWrite[i] < 0) continue;
            var d = res.TransientTextureDesc;
            var key = (d.Width, d.Height, d.Format, d.Kind);
            if (!texGroups.TryGetValue(key, out var list)) texGroups[key] = list = new();
            list.Add(res);
        }
        foreach (var kv in texGroups)
        {
            var sorted = kv.Value.OrderBy(r => aliveFirstWrite[r.Id]).ToList();
            var slots = new List<(GpuTexture? Tex, int FreeAfter)>();
            foreach (var res in sorted)
            {
                int start = aliveFirstWrite[res.Id];
                int end = aliveLastUse[res.Id];
                int reuseIdx = -1;
                for (int s = 0; s < slots.Count; s++)
                    if (slots[s].FreeAfter < start) { reuseIdx = s; break; }
                if (reuseIdx >= 0)
                {
                    res.PhysicalTexture = slots[reuseIdx].Tex;
                    res.PhysicalSlot = reuseIdx;
                    slots[reuseIdx] = (slots[reuseIdx].Tex, end);
                }
                else
                {
                    GpuTexture? fresh = null;
                    if (_device != null)
                    {
                        fresh = res.TransientTextureDesc.Kind == TextureKind.Depth
                            ? _device.CreateDepthTarget(res.TransientTextureDesc.Width, res.TransientTextureDesc.Height, res.TransientTextureDesc.Format)
                            : _device.CreateRenderTarget(res.TransientTextureDesc.Width, res.TransientTextureDesc.Height, res.TransientTextureDesc.Format);
                        _ownedTransientTextures.Add(fresh);
                    }
                    res.PhysicalTexture = fresh;
                    res.PhysicalSlot = slots.Count;
                    slots.Add((fresh, end));
                }
            }
            foreach (var res in sorted)
            {
                int count = sorted.Count(o => o.PhysicalSlot == res.PhysicalSlot);
                if (count >= 2) res.IsAliased = true;
            }
        }

        return new CompiledGraph(order);
    }

    private DependencyGraph BuildDependencyGraph()
    {
        var byKey = new Dictionary<RenderPassKey, PassRecord>();
        foreach (var pass in _passes)
        {
            if (!byKey.TryAdd(pass.Key, pass))
                throw new InvalidOperationException($"RenderGraph pass key '{pass.Key.Value}' has multiple declarations.");
        }

        var graph = new DependencyGraph(_passes.Count);
        AddLegacyResourceEdges(graph);
        var producers = new Dictionary<RenderResourceVersionId, PassRecord>();

        foreach (var pass in _passes)
        {
            foreach (var write in pass.SymbolicWrites)
            {
                var binding = GetSymbolicBinding(write.Version);
                ValidateSymbolicUsage(binding, write.BufferUsage, write.TextureUsage, write.Version);
                if (!producers.TryAdd(write.Version, pass))
                    throw new InvalidOperationException($"RenderGraph resource version '{Format(write.Version)}' has multiple producers.");
                pass.Writes.Add(ToResourceAccess(binding, write.BufferUsage, write.TextureUsage));
            }
        }

        foreach (var pass in _passes)
        {
            foreach (var read in pass.SymbolicReads)
            {
                var binding = GetSymbolicBinding(read.Version);
                ValidateSymbolicUsage(binding, read.BufferUsage, read.TextureUsage, read.Version);
                if (!producers.TryGetValue(read.Version, out var producer))
                    throw new InvalidOperationException($"RenderGraph resource version '{Format(read.Version)}' has no producer.");
                graph.AddEdge(producer.Index, pass.Index);
                pass.Reads.Add(ToResourceAccess(binding, read.BufferUsage, read.TextureUsage));
            }

            foreach (var write in pass.SymbolicWrites)
            {
                if (write.Predecessor is not { } predecessor) continue;
                if (predecessor.Slot != write.Version.Slot)
                    throw new InvalidOperationException($"RenderGraph predecessor '{Format(predecessor)}' belongs to a different slot than '{Format(write.Version)}'.");
                if (!producers.TryGetValue(predecessor, out var producer))
                    throw new InvalidOperationException($"RenderGraph predecessor '{Format(predecessor)}' is unknown.");
                graph.AddEdge(producer.Index, pass.Index);
            }

            foreach (var target in pass.ControlDependencies)
            {
                if (!byKey.TryGetValue(target, out var predecessor))
                    throw new InvalidOperationException($"RenderGraph control dependency target '{target.Value}' is unknown.");
                graph.AddEdge(predecessor.Index, pass.Index);
            }
        }

        foreach (var version in _exports)
        {
            _ = GetSymbolicBinding(version);
            if (!producers.TryGetValue(version, out var producer))
                throw new InvalidOperationException($"Exported RenderGraph resource version '{Format(version)}' has no producer.");
            graph.ExportProducers.Add(producer.Index);
        }

        return graph;
    }

    private void AddLegacyResourceEdges(DependencyGraph graph)
    {
        for (int resourceId = 1; resourceId < _resources.Count; resourceId++)
        {
            var writers = _passes.Where(p => p.Writes.Any(a => a.ResourceId == resourceId)).ToList();
            var readers = _passes.Where(p => p.Reads.Any(a => a.ResourceId == resourceId)).ToList();
            if (writers.Count == 1)
            {
                foreach (var reader in readers)
                    if (writers[0].Index != reader.Index) graph.AddEdge(writers[0].Index, reader.Index);
                continue;
            }

            if (writers.Count <= 1) continue;
            PassRecord? previous = null;
            foreach (var pass in _passes)
            {
                bool accesses = pass.Reads.Any(a => a.ResourceId == resourceId)
                    || pass.Writes.Any(a => a.ResourceId == resourceId);
                if (!accesses) continue;
                if (previous != null) graph.AddEdge(previous.Index, pass.Index);
                if (pass.Writes.Any(a => a.ResourceId == resourceId)) previous = pass;
            }
        }
    }

    private List<PassRecord> StableTopologicalSort(DependencyGraph graph)
    {
        var indegree = graph.Incoming.Select(edges => edges.Count).ToArray();
        var ready = new PriorityQueue<int, int>();
        for (int i = 0; i < indegree.Length; i++)
            if (indegree[i] == 0) ready.Enqueue(i, _passes[i].Index);

        var order = new List<PassRecord>(_passes.Count);
        while (ready.TryDequeue(out int index, out _))
        {
            order.Add(_passes[index]);
            foreach (int next in graph.Outgoing[index])
            {
                if (--indegree[next] == 0) ready.Enqueue(next, _passes[next].Index);
            }
        }

        if (order.Count != _passes.Count)
        {
            string passes = string.Join(", ", _passes.Where((_, i) => indegree[i] > 0).Select(p => p.Name));
            throw new InvalidOperationException($"RenderGraph dependency cycle detected among: {passes}.");
        }
        return order;
    }

    private HashSet<int> FindRequiredPasses(DependencyGraph graph)
    {
        var required = new HashSet<int>(graph.ExportProducers);
        foreach (var pass in _passes)
        {
            if (pass.HasSideEffect || pass.Writes.Any(write =>
                    _resources[write.ResourceId].Kind is ResourceKind.ExternalBuffer or ResourceKind.ExternalTexture))
                required.Add(pass.Index);
        }

        var pending = new Stack<int>(required);
        while (pending.TryPop(out int pass))
        {
            foreach (int dependency in graph.Incoming[pass])
                if (required.Add(dependency)) pending.Push(dependency);
        }
        return required;
    }

    private SymbolicSlotBinding GetSymbolicBinding(RenderResourceVersionId version)
    {
        if (!_symbolicSlots.TryGetValue(version.Slot, out var binding))
            throw new InvalidOperationException($"RenderGraph resource slot '{version.Slot.Value}' is not declared.");
        return binding;
    }

    private static ResourceAccess ToResourceAccess(
        SymbolicSlotBinding binding,
        ResourceUsage bufferUsage,
        TextureUsage textureUsage)
        => new(binding.ResourceId, bufferUsage, textureUsage, binding.IsTexture);

    private static void ValidateSymbolicUsage(
        SymbolicSlotBinding binding,
        ResourceUsage bufferUsage,
        TextureUsage textureUsage,
        RenderResourceVersionId version)
    {
        bool textureAccess = bufferUsage == ResourceUsage.None;
        if (binding.IsTexture != textureAccess)
            throw new InvalidOperationException($"RenderGraph resource version '{Format(version)}' access type does not match its slot.");
    }

    private void AddSymbolicSlot(RenderResourceSlotId slot, SymbolicSlotBinding binding)
    {
        if (!_symbolicSlots.TryAdd(slot, binding))
            throw new InvalidOperationException($"RenderGraph resource slot '{slot.Value}' is already declared.");
    }

    internal void ValidateBufferHandle(BufferHandle handle, string parameterName)
    {
        if (handle.Id <= 0 || handle.GraphId != _graphId || handle.Id >= _resources.Count || !_resources[handle.Id].IsBuffer)
            throw new ArgumentException($"BufferHandle {handle.Id} does not belong to this RenderGraph.", parameterName);
    }

    internal void ValidateTextureHandle(TextureHandle handle, string parameterName)
    {
        if (handle.Id <= 0 || handle.GraphId != _graphId || handle.Id >= _resources.Count || !_resources[handle.Id].IsTexture)
            throw new ArgumentException($"TextureHandle {handle.Id} does not belong to this RenderGraph.", parameterName);
    }

    internal void ValidateSymbolicVersion(RenderResourceVersionId version, string parameterName)
    {
        ValidateSlot(version.Slot, parameterName);
        if (string.IsNullOrWhiteSpace(version.Value))
            throw new ArgumentException("resource version value は空にできません。", parameterName);
    }

    internal void ValidatePassKey(RenderPassKey key, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(key.Value))
            throw new ArgumentException("pass key は空にできません。", parameterName);
    }

    private static void ValidateSlot(RenderResourceSlotId slot, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(slot.Value))
            throw new ArgumentException("resource slot value は空にできません。", parameterName);
    }

    private static string Format(RenderResourceVersionId version) => $"{version.Slot.Value}:{version.Value}";

    // === 内部 ===============================================================

    internal GpuBuffer ResolveBuffer(BufferHandle handle)
    {
        ValidateBufferHandle(handle, nameof(handle));
        var rec = _resources[handle.Id];
        if (!rec.IsBuffer) throw new InvalidOperationException($"'{rec.Name}' は Texture です。 Texture() を使ってください。");
        if (rec.PhysicalBuffer == null)
            throw new InvalidOperationException($"リソース '{rec.Name}' は未割当 (Compile 未実行)。");
        return rec.PhysicalBuffer;
    }

    internal GpuTexture ResolveTexture(TextureHandle handle)
    {
        ValidateTextureHandle(handle, nameof(handle));
        var rec = _resources[handle.Id];
        if (!rec.IsTexture) throw new InvalidOperationException($"'{rec.Name}' は Buffer です。 Buffer() を使ってください。");
        if (rec.PhysicalTexture == null)
            throw new InvalidOperationException($"リソース '{rec.Name}' は未割当 (Compile 未実行)。");
        return rec.PhysicalTexture;
    }

    private void ThrowIfCompiled()
    {
        if (_compiled) throw new InvalidOperationException("Compile/Execute 後にグラフを変更することはできません。");
    }

    private static IEnumerable<ResourceAccess> EnumerateAllAccesses(PassRecord pass)
    {
        foreach (var a in pass.Reads) yield return a;
        foreach (var a in pass.Writes) yield return a;
    }

    /// <summary>テスト用: 寿命解析結果を引く。</summary>
    internal (int first, int last) GetLifetime(BufferHandle handle)
    {
        ValidateBufferHandle(handle, nameof(handle));
        var r = _resources[handle.Id];
        return (r.FirstWritePass, r.LastReadPass);
    }

    /// <summary>テスト用: テクスチャの寿命解析結果を引く。</summary>
    internal (int first, int last) GetLifetime(TextureHandle handle)
    {
        ValidateTextureHandle(handle, nameof(handle));
        var r = _resources[handle.Id];
        return (r.FirstWritePass, r.LastReadPass);
    }

    /// <summary>テクスチャの同形グループ内 slot 番号 (aliasing 確認用、未割当なら -1)。</summary>
    public int GetPhysicalSlot(TextureHandle handle)
    {
        ValidateTextureHandle(handle, nameof(handle));
        return _resources[handle.Id].PhysicalSlot;
    }

    /// <summary>テスト用: テクスチャが aliasing で物理共有しているか。</summary>
    public bool IsAliased(TextureHandle handle)
    {
        ValidateTextureHandle(handle, nameof(handle));
        return _resources[handle.Id].IsAliased;
    }

    /// <summary>Compile/Execute 後に確保された物理 Transient テクスチャ数。</summary>
    public int PhysicalTransientTextureCount => _ownedTransientTextures.Count;

    /// <summary>登録済みパス数 (culled を含む)。</summary>
    public int PassCount => _passes.Count;

    /// <summary>Compile/Execute 後に確保された物理 Transient バッファ数。aliasing の効果を観測するのに使う。</summary>
    public int PhysicalTransientBufferCount => _ownedTransientBuffers.Count;

    /// <summary>リソースの同形グループ内 slot 番号 (aliasing 確認用、未割当なら -1)。</summary>
    public int GetPhysicalSlot(BufferHandle handle)
    {
        ValidateBufferHandle(handle, nameof(handle));
        return _resources[handle.Id].PhysicalSlot;
    }

    /// <summary>リソースが aliasing で物理バッファを他リソースと共有しているか。</summary>
    public bool IsAliased(BufferHandle handle)
    {
        ValidateBufferHandle(handle, nameof(handle));
        return _resources[handle.Id].IsAliased;
    }

    /// <summary>Compile 相のデッドパスカリングで除外されたか。</summary>
    public bool IsPassCulled(int passIndex) => _passes[passIndex].Culled;

    /// <summary>テスト用: Transient バッファのダミー登録 (物理割当なし)。</summary>
    internal BufferHandle CreateBufferForTest(BufferDesc desc, string name)
    {
        ThrowIfCompiled();
        if (desc.SizeBytes == 0) throw new ArgumentException("SizeBytes は 0 不可", nameof(desc));
        var rec = new ResourceRecord
        {
            Name = name,
            Kind = ResourceKind.TransientBuffer,
            Id = _resources.Count,
            TransientBufferDesc = desc,
        };
        _resources.Add(rec);
        return new BufferHandle(rec.Id, _graphId);
    }

    /// <summary>テスト用: Transient テクスチャのダミー登録 (物理割当なし)。</summary>
    internal TextureHandle CreateTextureForTest(TextureDesc desc, string name)
    {
        ThrowIfCompiled();
        var rec = new ResourceRecord
        {
            Name = name,
            Kind = ResourceKind.TransientTexture,
            Id = _resources.Count,
            TransientTextureDesc = desc,
        };
        _resources.Add(rec);
        return new TextureHandle(rec.Id, _graphId);
    }

    /// <summary>テスト用: External テクスチャの dummy 登録。</summary>
    internal TextureHandle ImportTextureForTest(string name)
    {
        ThrowIfCompiled();
        var rec = new ResourceRecord
        {
            Name = name,
            Kind = ResourceKind.ExternalTexture,
            Id = _resources.Count,
            ExternalTexture = null,
        };
        _resources.Add(rec);
        return new TextureHandle(rec.Id, _graphId);
    }

    /// <summary>テスト用: External の論理ハンドルを物理参照なしで登録 (バリア計算検証用)。</summary>
    internal BufferHandle ImportBufferForTest(string name)
    {
        ThrowIfCompiled();
        var rec = new ResourceRecord
        {
            Name = name,
            Kind = ResourceKind.ExternalBuffer,
            Id = _resources.Count,
            ExternalBuffer = null,
        };
        _resources.Add(rec);
        return new BufferHandle(rec.Id, _graphId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var b in _ownedTransientBuffers) b.Dispose();
        _ownedTransientBuffers.Clear();
        foreach (var t in _ownedTransientTextures) t.Dispose();
        _ownedTransientTextures.Clear();
    }
}

internal readonly record struct SymbolicSlotBinding(int ResourceId, bool IsTexture);

internal sealed class DependencyGraph
{
    public DependencyGraph(int passCount)
    {
        Incoming = Enumerable.Range(0, passCount).Select(_ => new HashSet<int>()).ToArray();
        Outgoing = Enumerable.Range(0, passCount).Select(_ => new HashSet<int>()).ToArray();
    }

    public HashSet<int>[] Incoming { get; }
    public HashSet<int>[] Outgoing { get; }
    public HashSet<int> ExportProducers { get; } = new();

    public void AddEdge(int predecessor, int successor)
    {
        if (Outgoing[predecessor].Add(successor)) Incoming[successor].Add(predecessor);
    }
}

/// <summary>Compile 後の確定情報。RG-M1 はパス順序だけ。</summary>
internal sealed class CompiledGraph
{
    public List<PassRecord> Order { get; }
    public CompiledGraph(List<PassRecord> order) { Order = order; }
}
