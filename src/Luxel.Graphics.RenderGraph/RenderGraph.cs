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
    private readonly GpuDevice? _device;
    private readonly List<GpuBuffer> _ownedTransientBuffers = new();
    private readonly List<GpuTexture> _ownedTransientTextures = new();
    private readonly List<ResourceRecord> _resources = new() { null! };   // index 0 = invalid sentinel
    private readonly List<PassRecord> _passes = new();
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
        return new BufferHandle(rec.Id);
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
        return new BufferHandle(rec.Id);
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
        return new TextureHandle(rec.Id);
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
        return new TextureHandle(rec.Id);
    }

    /// <summary>新しいパスを追加する。返り値の builder で Read/Write/Execute を宣言する。</summary>
    public PassBuilder AddPass(string name, PassQueue queue = PassQueue.Graphics)
    {
        ThrowIfCompiled();
        var rec = new PassRecord
        {
            Name = name,
            Queue = queue,
            Index = _passes.Count,
        };
        return new PassBuilder(this, rec);
    }

    internal void AddPassInternal(PassRecord record) => _passes.Add(record);

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
    /// Compile 相: (1) 寿命解析、(2) デッドパスカリング (backward reachability)、
    /// (3) Transient aliasing (同形リソース×寿命非重複の物理バッファ/テクスチャ共有, interval scheduling)、
    /// (4) 物理リソース割当 (テストモードではスキップ)、(5) 実行順は登録順そのまま。
    /// </summary>
    private CompiledGraph Compile()
    {
        // 1. 寿命解析: 各リソースの first-write-pass / last-read-pass を計算 (全パスを対象)。
        for (int p = 0; p < _passes.Count; p++)
        {
            var pass = _passes[p];
            foreach (var w in pass.Writes)
            {
                var res = _resources[w.ResourceId];
                if (res.FirstWritePass < 0) res.FirstWritePass = p;
            }
            foreach (var r in pass.Reads)
            {
                var res = _resources[r.ResourceId];
                if (res.LastReadPass < p) res.LastReadPass = p;
            }
        }

        // 2. デッドパスカリング: External リソースを「必要」とし、それを書き込むパスを Required にし、
        //    そのパスの Reads を「必要」に伝播。pass を逆順に走査して 1 パスで決定。
        var needed = new HashSet<int>();
        foreach (var r in _resources)
        {
            if (r == null) continue;
            if (r.Kind == ResourceKind.ExternalBuffer || r.Kind == ResourceKind.ExternalTexture) needed.Add(r.Id);
        }
        for (int p = _passes.Count - 1; p >= 0; p--)
        {
            var pass = _passes[p];
            bool required = false;
            foreach (var w in pass.Writes)
            {
                if (needed.Contains(w.ResourceId)) { required = true; break; }
            }
            pass.Culled = !required;
            if (required)
            {
                foreach (var rd in pass.Reads) needed.Add(rd.ResourceId);
            }
        }

        // 3. 非 culled パスから見た有効寿命を再計算 (aliasing に使う)。
        for (int i = 1; i < _resources.Count; i++)
        {
            var res = _resources[i];
            res.PhysicalSlot = -1;
            res.IsAliased = false;
        }
        var aliveFirstWrite = new int[_resources.Count];
        var aliveLastUse = new int[_resources.Count];
        for (int i = 0; i < aliveFirstWrite.Length; i++) { aliveFirstWrite[i] = -1; aliveLastUse[i] = -1; }
        for (int p = 0; p < _passes.Count; p++)
        {
            if (_passes[p].Culled) continue;
            foreach (var w in _passes[p].Writes)
            {
                int id = w.ResourceId;
                if (aliveFirstWrite[id] < 0) aliveFirstWrite[id] = p;
                if (aliveLastUse[id] < p) aliveLastUse[id] = p;
            }
            foreach (var rd in _passes[p].Reads)
            {
                int id = rd.ResourceId;
                if (aliveLastUse[id] < p) aliveLastUse[id] = p;
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

        // 5. 実行順: 登録順そのまま (culled は Execute 側でスキップ)。
        var order = new List<PassRecord>(_passes);
        return new CompiledGraph(order);
    }

    // === 内部 ===============================================================

    internal GpuBuffer ResolveBuffer(BufferHandle handle)
    {
        if (!handle.IsValid || handle.Id >= _resources.Count)
            throw new ArgumentException($"無効なハンドル: {handle.Id}", nameof(handle));
        var rec = _resources[handle.Id];
        if (!rec.IsBuffer) throw new InvalidOperationException($"'{rec.Name}' は Texture です。 Texture() を使ってください。");
        if (rec.PhysicalBuffer == null)
            throw new InvalidOperationException($"リソース '{rec.Name}' は未割当 (Compile 未実行)。");
        return rec.PhysicalBuffer;
    }

    internal GpuTexture ResolveTexture(TextureHandle handle)
    {
        if (!handle.IsValid || handle.Id >= _resources.Count)
            throw new ArgumentException($"無効なハンドル: {handle.Id}", nameof(handle));
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
        var r = _resources[handle.Id];
        return (r.FirstWritePass, r.LastReadPass);
    }

    /// <summary>テスト用: テクスチャの寿命解析結果を引く。</summary>
    internal (int first, int last) GetLifetime(TextureHandle handle)
    {
        var r = _resources[handle.Id];
        return (r.FirstWritePass, r.LastReadPass);
    }

    /// <summary>テスト用: テクスチャの aliasing slot 番号。</summary>
    /// <summary>テクスチャの同形グループ内 slot 番号 (aliasing 確認用、未割当なら -1)。</summary>
    public int GetPhysicalSlot(TextureHandle handle) => _resources[handle.Id].PhysicalSlot;

    /// <summary>テスト用: テクスチャが aliasing で物理共有しているか。</summary>
    public bool IsAliased(TextureHandle handle) => _resources[handle.Id].IsAliased;

    /// <summary>Compile/Execute 後に確保された物理 Transient テクスチャ数。</summary>
    public int PhysicalTransientTextureCount => _ownedTransientTextures.Count;

    /// <summary>登録済みパス数 (culled を含む)。</summary>
    public int PassCount => _passes.Count;

    /// <summary>Compile/Execute 後に確保された物理 Transient バッファ数。aliasing の効果を観測するのに使う。</summary>
    public int PhysicalTransientBufferCount => _ownedTransientBuffers.Count;

    /// <summary>リソースの同形グループ内 slot 番号 (aliasing 確認用、未割当なら -1)。</summary>
    public int GetPhysicalSlot(BufferHandle handle) => _resources[handle.Id].PhysicalSlot;

    /// <summary>リソースが aliasing で物理バッファを他リソースと共有しているか。</summary>
    public bool IsAliased(BufferHandle handle) => _resources[handle.Id].IsAliased;

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
        return new BufferHandle(rec.Id);
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
        return new TextureHandle(rec.Id);
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
        return new TextureHandle(rec.Id);
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
        return new BufferHandle(rec.Id);
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

/// <summary>Compile 後の確定情報。RG-M1 はパス順序だけ。</summary>
internal sealed class CompiledGraph
{
    public List<PassRecord> Order { get; }
    public CompiledGraph(List<PassRecord> order) { Order = order; }
}
