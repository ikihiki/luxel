using Luxel.Diagnostics;

namespace Luxel.Graphics.TwoD;

/// <summary>
/// バックエンド非依存の保持型 (retained) 2D シーン。UIツリー、CPU display-list、dirty stateを
/// フレーム間で保持する。移動はtransform、色変更はstyleだけを更新し、構造変更時のみ再構築する。
/// 描画リソースは<see cref="IRasterizer2D.CreateScene(RetainedCanvas)"/>が返すbackend sessionが所有する。
/// </summary>
public sealed class RetainedCanvas : IDisposable
{
    private const uint NoClip = 0xFFFFFFFF;

    private readonly List<GpuTransform> _transforms = new();
    private readonly List<GpuStyle> _styles = new();
    private readonly List<GpuClip> _clips = new();
    private readonly List<GpuSegment> _segments = new();
    private readonly List<GpuPath> _paths = new();
    private readonly List<uint> _order = new();
    private readonly List<IRetainedCanvasSink> _sinks = new();

    private readonly HashSet<UiNode> _dirtyTransform = new();
    private readonly HashSet<UiNode> _dirtyStyle = new();
    private readonly HashSet<UiNode> _dirtyClip = new();
    private readonly HashSet<UiNode> _dirtyContent = new();
    private bool _dirtyStructure = true;
    private bool _dirtyOrder;
    private bool _disposed;
    private ulong _changeGeneration = 1;

    /// <summary>直近 Flush の部分更新量 (検証用)。</summary>
    public int LastTransformWrites { get; private set; }
    public int LastStyleWrites { get; private set; }
    /// <summary>直近 Flush で Content を in-place 部分更新したノード数 (フル再構築なし)。</summary>
    public int LastContentWrites { get; private set; }
    public long LastSegmentBytesWritten { get; private set; }
    public bool LastWasFullRebuild { get; private set; }
    /// <summary>直近 Flush で order バッファを再構成したときのエントリ数 (Visible 切替。0 = 再構成なし)。</summary>
    public int LastOrderWrites { get; private set; }

    // ---- 累積統計 (性能計測/ベンチ用。ResetStats で 0 に戻す) ----
    /// <summary>Flush の累積回数 (= 実際に描画されたフレーム数の近似)。</summary>
    public long TotalFlushes { get; private set; }
    /// <summary>フル再構築の累積回数。増分更新が効いていれば定常フレームで増えない。</summary>
    public long TotalRebuilds { get; private set; }
    /// <summary>フル再構築 (CPU display-list再エンコード) に費やした累積時間 (µs)。</summary>
    public long TotalRebuildMicros { get; private set; }
    /// <summary>直近のフル再構築時間 (µs)。</summary>
    public long LastRebuildMicros { get; private set; }
    /// <summary>display-list更新量の累積バイト数。互換名であり、CPU backendでも同じ推定値を返す。</summary>
    public long TotalUploadBytes { get; private set; }
    /// <summary>現在のシーン規模 (直近 Rebuild 時点の線分/パス数)。</summary>
    public int SegmentCount => _segments.Count;
    public int PathCount => _paths.Count;

    /// <summary>累積統計を 0 に戻す (ベンチの区間計測用)。</summary>
    public void ResetStats()
    {
        TotalFlushes = 0; TotalRebuilds = 0; TotalRebuildMicros = 0;
        LastRebuildMicros = 0; TotalUploadBytes = 0;
    }

    /// <summary>バックエンド非依存の保持型キャンバス。</summary>
    public RetainedCanvas() => Root = new UiNode(this);

    public UiNode Root { get; }

    /// <summary>保持内容の変更ごとに単調増加する世代。Flush は世代を戻さない。</summary>
    public ulong ChangeGeneration => _changeGeneration;

    /// <summary>親の下に子ノードを作る。</summary>
    public UiNode AddChild(UiNode parent)
    {
        var n = new UiNode(this) { Parent = parent };
        parent.Children.Add(n);
        MarkStructureDirty();
        return n;
    }

    /// <summary>ノードを削除する。</summary>
    public void Remove(UiNode node)
    {
        if (node.Parent?.Children.Remove(node) == true) MarkStructureDirty();
    }

    /// <summary>明示的な全再構築要求。Content 差し替え/ノード増減は setter/AddChild が自動で
    /// dirty をマークするため通常は不要 — 呼ぶとフル再構築を強制する (増分更新が効かなくなる) ので、
    /// slot 管理の外で何かを変えた場合の脱出口としてのみ使うこと。</summary>
    public void Invalidate() => MarkStructureDirty();

    /// <summary>未反映の変更があるか。false なら再描画しても前回と同じ絵になる
    /// (呼び出し側は Render 自体をスキップできる)。</summary>
    public bool HasPendingChanges
        => _dirtyStructure || _dirtyOrder || _dirtyContent.Count > 0
        || _dirtyTransform.Count > 0 || _dirtyStyle.Count > 0 || _dirtyClip.Count > 0;

    internal void MarkTransformDirty(UiNode n) { _dirtyTransform.Add(n); Changed(); }
    internal void MarkStyleDirty(UiNode n) { _dirtyStyle.Add(n); Changed(); }
    internal void MarkClipDirty(UiNode n) { _dirtyClip.Add(n); _dirtyTransform.Add(n); Changed(); }
    internal void MarkContentDirty(UiNode n) { _dirtyContent.Add(n); Changed(); }
    internal void MarkStructureDirty() { _dirtyStructure = true; Changed(); }
    internal void MarkOrderDirty() { _dirtyOrder = true; Changed(); }

    private void Changed() => _changeGeneration = checked(_changeGeneration + 1);

    /// <summary>最終 2D プリミティブ (SoA) のスナップショットを DevTools へ配信する。</summary>
    private void EmitPrimitives()
    {
        var list = new DiagPath[_paths.Count];
        for (int i = 0; i < _paths.Count; i++)
        {
            GpuPath p = _paths[i];
            uint color = p.StyleSlot < _styles.Count ? _styles[(int)p.StyleSlot].ColorRgba : 0u;
            float op = p.StyleSlot < _styles.Count ? _styles[(int)p.StyleSlot].Opacity : 1f;
            list[i] = new DiagPath(i, p.SegStart, p.SegCount, p.TransformSlot, p.StyleSlot, p.ClipSlot, p.Kind, color, op);
        }
        EngineDiagnostics.Emit(EngineDiagnostics.Primitives, new DiagPrimitives(
            _segments.Count, _paths.Count, _transforms.Count, _styles.Count, _clips.Count, _order.Count,
            _segments.Count * 32L, list));
    }

    /// <summary>dirtyをCPU display-listへ反映する。backend sessionが描画前に呼ぶ。</summary>
    public void Flush(uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        LastTransformWrites = 0; LastStyleWrites = 0; LastSegmentBytesWritten = 0;
        LastWasFullRebuild = false; LastOrderWrites = 0; LastContentWrites = 0;
        TotalFlushes++;

        // Content 差し替え: 新しい線分/パス数が既存レンジに収まるノードは in-place 書き込み。
        // 収まらないノードが 1 つでもあればフル再構築へフォールバック (伸長対応は IC-M2)。
        if (!_dirtyStructure && _dirtyContent.Count > 0)
        {
            foreach (UiNode n in _dirtyContent)
                if (!TryUpdateContentInPlace(n)) { _dirtyStructure = true; break; }
            _dirtyContent.Clear();   // in-place 分は反映済み、フォールバック時は Rebuild が全量やり直す
        }

        if (_dirtyStructure)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Rebuild(width, height);
            LastRebuildMicros = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1_000_000
                / System.Diagnostics.Stopwatch.Frequency;
            TotalRebuilds++;
            TotalRebuildMicros += LastRebuildMicros;
            TotalUploadBytes += _segments.Count * 32L + _paths.Count * (long)GpuPath.SizeBytes + _transforms.Count * 32L
                + _styles.Count * 16L + _clips.Count * (long)GpuClip.SizeBytes + _order.Count * 4L;
            EmitFlush(width, height);
            return;
        }

        if (_dirtyTransform.Count > 0 || _dirtyClip.Count > 0)
        {
            var affected = new HashSet<UiNode>();
            foreach (UiNode n in _dirtyTransform) AddSubtree(n, affected);
            Span<GpuTransform> tfSpan = default;
            Span<GpuClip> clipSpan = default;
            UpdateTransformsDfs(Root, affected, tfSpan, clipSpan, width, height);
            _dirtyTransform.Clear();
            _dirtyClip.Clear();
        }

        if (_dirtyStyle.Count > 0)
        {
            // Opacity は 親 × 自分 の実効値で子へ継承されるため、transform と同様にサブツリーへ伝播する。
            var affected = new HashSet<UiNode>();
            foreach (UiNode n in _dirtyStyle) AddSubtree(n, affected);
            Span<GpuStyle> stySpan = default;
            UpdateStylesDfs(Root, affected, stySpan);
            _dirtyStyle.Clear();
        }

        if (_dirtyOrder)
            RebuildOrder();

        TotalUploadBytes += LastTransformWrites * 32L + LastStyleWrites * 16L + LastOrderWrites * 4L;
        EmitFlush(width, height);
    }

    /// <summary>ノードの Content を既存レンジへ in-place 反映する。新しい線分/パス数が予約容量
    /// (SegCapacity/PathCapacity) 以内なら成功 — 書き込みはこのノードの線分 + パスエントリだけで、
    /// 他バッファ/bindless index は不変。パス数が変わったときのみ order を再構成する (軽量)。</summary>
    private bool TryUpdateContentInPlace(UiNode n)
    {
        if (n.TransformSlot < 0) return false;   // 未割当 (初回 Rebuild 前)

        GpuSegment[] segs; GpuPath[] paths; GpuStyle[] styles;
        if (n.Content is null) { segs = []; paths = []; styles = []; }
        else (segs, paths, styles) = PathEncoder.Encode(n.Content);
        if (paths.Length > n.PathCapacity || segs.Length > n.SegCapacity) return false;   // 予約超え → 全再構築
        bool mixed = HasOwnColors(n);
        if (mixed && n.ContentStyleStart < 0) return false;   // スタイルレンジ未割当 → 全再構築で確保

        // ヘッドレスは GPU バッファがない — CPU 側 SoA と統計だけ更新する (span は空 = 書き込みスキップ)
        Span<GpuSegment> segSpan = default;
        Span<GpuPath> pathSpan = default;
        Span<GpuStyle> stySpan = default;
        for (int i = 0; i < segs.Length; i++)
        {
            _segments[n.SegStart + i] = segs[i];
            NotifySegment(n.SegStart + i, segs[i]);
            if (!segSpan.IsEmpty) segSpan[n.SegStart + i] = segs[i];
        }
        for (int i = 0; i < paths.Length; i++)
        {
            GpuPath p = paths[i];
            p.SegStart += (uint)n.SegStart;
            p.TransformSlot = (uint)n.TransformSlot;
            bool abs = n.ContentColors || n.Content!.Shapes[i].AbsoluteColor;
            p.StyleSlot = abs ? (uint)(n.ContentStyleStart + i) : (uint)n.StyleSlot;
            p.ClipSlot = n.ClipSlot < 0 ? NoClip : (uint)n.ClipSlot;
            _paths[n.PathStart + i] = p;
            NotifyPath(n.PathStart + i, p);
            if (!pathSpan.IsEmpty) pathSpan[n.PathStart + i] = p;
            if (mixed)
            {
                GpuStyle g = abs
                    ? new GpuStyle { ColorRgba = styles[i].ColorRgba, Opacity = styles[i].Opacity * n.EffectiveOpacity }
                    : default;
                _styles[n.ContentStyleStart + i] = g;
                NotifyStyle(n.ContentStyleStart + i, g);
                if (!stySpan.IsEmpty) stySpan[n.ContentStyleStart + i] = g;
                LastStyleWrites++;
            }
        }
        for (int i = paths.Length; i < n.PathCount; i++)
        {
            _paths[n.PathStart + i] = default;   // 縮んだ分は中立化 (order からも外れる)
            NotifyPath(n.PathStart + i, default);
            if (!pathSpan.IsEmpty) pathSpan[n.PathStart + i] = default;
        }
        if (paths.Length != n.PathCount)
        {
            n.PathCount = paths.Length;
            _dirtyOrder = true;   // order は実数レンジを参照 — 数が変わったら再構成 (Flush 末尾で処理)
        }
        LastContentWrites++;
        LastSegmentBytesWritten += segs.Length * 32L;
        TotalUploadBytes += segs.Length * 32L + paths.Length * (long)GpuPath.SizeBytes;
        return true;
    }

    private void UpdateStylesDfs(UiNode node, HashSet<UiNode> affected, Span<GpuStyle> span)
    {
        if (affected.Contains(node))
        {
            node.EffectiveOpacity = (node.Parent?.EffectiveOpacity ?? 1f) * node.Opacity;
            var g = new GpuStyle { ColorRgba = node.Color, Opacity = node.EffectiveOpacity };
            _styles[node.StyleSlot] = g;
            NotifyStyle(node.StyleSlot, g);
            if (!span.IsEmpty) span[node.StyleSlot] = g;
            LastStyleWrites++;
        }
        foreach (UiNode child in node.Children) UpdateStylesDfs(child, affected, span);
    }

    /// <summary>Visible 切替/パス数変化の部分更新: order のみ再構成 (パス/スタイル等は不変)。
    /// 容量以内なら既存バッファへ in-place 書き込み (バッファ再確保も bindless 変更もなし)。</summary>
    private void RebuildOrder()
    {
        _order.Clear();
        BuildOrder(Root);
        foreach (IRetainedCanvasSink sink in _sinks) sink.WriteOrder(_order.ToArray());
        LastOrderWrites = _order.Count;
        _dirtyOrder = false;
    }

    private void EmitFlush(uint width, uint height)
    {
        if (EngineDiagnostics.IsEnabled(EngineDiagnostics.RenderFlush))
            EngineDiagnostics.Emit(EngineDiagnostics.RenderFlush,
                new DiagFlush(LastTransformWrites, LastStyleWrites, LastSegmentBytesWritten, LastWasFullRebuild, width, height));
    }

    // ---- フル再構築 (初回 + 構造変更) ----
    private void Rebuild(uint width, uint height)
    {
        LastWasFullRebuild = true;
        _transforms.Clear(); _styles.Clear(); _clips.Clear();
        _segments.Clear(); _paths.Clear(); _order.Clear();

        AssignAndEncode(Root, NoClip);
        BuildOrder(Root);
        foreach (IRetainedCanvasSink sink in _sinks) sink.FullSync(this);

        LastSegmentBytesWritten = _segments.Count * 32L;
        _dirtyStructure = false; _dirtyOrder = false;
        _dirtyTransform.Clear(); _dirtyStyle.Clear(); _dirtyClip.Clear(); _dirtyContent.Clear();
    }

    /// <summary>ノードの Content が形状固有の色を持つか (ContentColors 全体指定 or AbsoluteColor シェイプ)。</summary>
    private static bool HasOwnColors(UiNode node)
    {
        if (node.ContentColors) return node.Content is not null;
        if (node.Content is null) return false;
        foreach (Scene2D.Shape s in node.Content.Shapes)
            if (s.AbsoluteColor) return true;
        return false;
    }

    private void AssignAndEncode(UiNode node, uint inheritedClipSlot)
    {
        node.World = node.Parent == null ? node.Transform : Affine2D.Mul(node.Parent.World, node.Transform);
        node.EffectiveOpacity = (node.Parent?.EffectiveOpacity ?? 1f) * node.Opacity;   // 親 × 自分
        node.TransformSlot = _transforms.Count; _transforms.Add(node.World.ToGpu());
        node.StyleSlot = _styles.Count; _styles.Add(new GpuStyle { ColorRgba = node.Color, Opacity = node.EffectiveOpacity });
        node.OwnClipSlot = ResolveClip(node, inheritedClipSlot);
        node.ClipSlot = node.OwnClipSlot >= 0 ? node.OwnClipSlot
            : inheritedClipSlot == NoClip ? -1 : (int)inheritedClipSlot;

        node.PathStart = _paths.Count;
        node.SegStart = _segments.Count;
        node.ContentStyleStart = -1;
        if (node.Content != null)
        {
            (GpuSegment[] segs, GpuPath[] paths, GpuStyle[] styles) = PathEncoder.Encode(node.Content);
            // 混色ノード: ContentColors (全形状) か AbsoluteColor シェイプ (カラー絵文字レイヤ等) を
            // 含むノードは、形状別スタイルレンジを持つ (非対象パスはノード色のまま)
            bool mixed = HasOwnColors(node);
            if (mixed) node.ContentStyleStart = _styles.Count;
            uint segBase = (uint)_segments.Count;
            _segments.AddRange(segs);
            for (int i = 0; i < paths.Length; i++)
            {
                GpuPath p = paths[i];
                p.SegStart += segBase;
                p.TransformSlot = (uint)node.TransformSlot;
                bool abs = node.ContentColors || node.Content.Shapes[i].AbsoluteColor;
                p.StyleSlot = abs ? (uint)(node.ContentStyleStart + i) : (uint)node.StyleSlot;
                p.ClipSlot = node.ClipSlot < 0 ? NoClip : (uint)node.ClipSlot;
                _paths.Add(p);
                if (mixed)
                    _styles.Add(abs
                        ? new GpuStyle { ColorRgba = styles[i].ColorRgba, Opacity = styles[i].Opacity * node.EffectiveOpacity }
                        : default);   // 非対象パスはノードスロット参照 — 位置合わせのダミー
            }
        }
        node.PathCount = _paths.Count - node.PathStart;

        // 容量スラック (IC-M2): 少し伸びる Content 差し替え (タイプ 1 打鍵でグリフ +1 等) を
        // in-place + order 再構成だけで受けるための予約。order は実数 (PathCount) しか参照しない
        // ので、予約分は GPU メモリを +25% 程度使うだけで per-pixel コストは増えない。
        int segUsed = _segments.Count - node.SegStart;
        int segSlack = Math.Max(Math.Max(16, segUsed / 4), node.ReserveSegs - segUsed);   // ReserveContent の最低予約を尊重
        for (int i = 0; i < segSlack; i++) _segments.Add(default);
        node.SegCapacity = _segments.Count - node.SegStart;
        int pathSlack = Math.Max(Math.Max(4, node.PathCount / 4), node.ReservePaths - node.PathCount);   // 最低 4 = 選択矩形等の 0→数個をカバー
        for (int i = 0; i < pathSlack; i++)
        {
            _paths.Add(default);   // SegCount=0 = 何も描かない
            if (node.ContentStyleStart >= 0) _styles.Add(default);   // 伸長分のスタイルスロットも対で予約 (in-place 用)
        }
        node.PathCapacity = _paths.Count - node.PathStart;

        uint childClipSlot = node.ClipSlot < 0 ? NoClip : (uint)node.ClipSlot;
        foreach (UiNode child in SortedChildren(node)) AssignAndEncode(child, childClipSlot);
    }

    private int ResolveClip(UiNode node, uint parentSlot)
    {
        if (node.Clip is not RectClip clip) return -1;
        int slot = _clips.Count;
        _clips.Add(ToGpuClip(clip, node.World, parentSlot));
        return slot;
    }

    /// <summary>クリップスロットの現在値 (テスト用 — 部分更新の追従検証)。</summary>
    internal GpuClip DebugClipAt(int slot) => _clips[slot];

    /// <summary>ローカルの角丸クリップをワールド空間へ変換する。2D UI と同じ軸並行変換を前提とし、
    /// 回転・shear は従来どおり AABB へ近似する。</summary>
    private static GpuClip ToGpuClip(RectClip clip, Affine2D world, uint parentSlot)
    {
        var (lo, hi) = ToScreenAabb(clip, world);
        float sx = MathF.Sqrt(world.A * world.A + world.B * world.B);
        float sy = MathF.Sqrt(world.C * world.C + world.D * world.D);
        float radius = MathF.Min(MathF.Max(0, clip.Radius), MathF.Min(clip.W, clip.H) * 0.5f);
        return new GpuClip
        {
            MinX = lo.x, MinY = lo.y, MaxX = hi.x, MaxY = hi.y,
            RadiusX = MathF.Min(radius * sx, (hi.x - lo.x) * 0.5f),
            RadiusY = MathF.Min(radius * sy, (hi.y - lo.y) * 0.5f),
            Corners = (uint)clip.Corners,
            ParentSlot = parentSlot,
        };
    }

    private static ((float x, float y) lo, (float x, float y) hi) ToScreenAabb(RectClip rc, Affine2D world)
    {
        Span<System.Numerics.Vector2> c =
        [
            world.Apply(new(rc.X, rc.Y)), world.Apply(new(rc.X + rc.W, rc.Y)),
            world.Apply(new(rc.X, rc.Y + rc.H)), world.Apply(new(rc.X + rc.W, rc.Y + rc.H)),
        ];
        float minx = c[0].X, miny = c[0].Y, maxx = c[0].X, maxy = c[0].Y;
        for (int i = 1; i < 4; i++)
        {
            minx = MathF.Min(minx, c[i].X); miny = MathF.Min(miny, c[i].Y);
            maxx = MathF.Max(maxx, c[i].X); maxy = MathF.Max(maxy, c[i].Y);
        }
        return ((minx, miny), (maxx, maxy));
    }

    private void BuildOrder(UiNode node)
    {
        if (!node.Visible) return;   // サブツリーごと描画順から除外 (子も継承)
        for (int i = 0; i < node.PathCount; i++) _order.Add((uint)(node.PathStart + i));
        foreach (UiNode child in SortedChildren(node)) BuildOrder(child);
    }

    private void UpdateTransformsDfs(UiNode node, HashSet<UiNode> affected,
        Span<GpuTransform> tfSpan, Span<GpuClip> clipSpan, uint width, uint height)
    {
        if (affected.Contains(node))
        {
            node.World = node.Parent == null ? node.Transform : Affine2D.Mul(node.Parent.World, node.Transform);
            GpuTransform g = node.World.ToGpu();
            _transforms[node.TransformSlot] = g;
            NotifyTransform(node.TransformSlot, g);
            if (!tfSpan.IsEmpty) tfSpan[node.TransformSlot] = g;
            LastTransformWrites++;

            // 祖先クリップは ParentSlot の連鎖で参照するため、自分が所有する形だけを更新する。
            if (node.OwnClipSlot >= 0 && node.Clip is RectClip clip)
            {
                uint parentSlot = node.Parent is { ClipSlot: >= 0 } parent ? (uint)parent.ClipSlot : NoClip;
                GpuClip gc = ToGpuClip(clip, node.World, parentSlot);
                _clips[node.OwnClipSlot] = gc;
                NotifyClip(node.OwnClipSlot, gc);
                if (clipSpan.Length > node.OwnClipSlot) clipSpan[node.OwnClipSlot] = gc;
            }
        }
        foreach (UiNode child in SortedChildren(node))
            UpdateTransformsDfs(child, affected, tfSpan, clipSpan, width, height);
    }

    private static void AddSubtree(UiNode n, HashSet<UiNode> set)
    {
        if (!set.Add(n)) return;
        foreach (UiNode c in n.Children) AddSubtree(c, set);
    }

    private static IEnumerable<UiNode> SortedChildren(UiNode n)
        => n.Children.Count <= 1 ? n.Children : n.Children.OrderBy(c => c.Z);

    internal void RegisterSink(IRetainedCanvasSink sink)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_sinks.Contains(sink)) _sinks.Add(sink);
    }

    internal void UnregisterSink(IRetainedCanvasSink sink) => _sinks.Remove(sink);

    private void NotifyTransform(int index, GpuTransform value)
    { foreach (IRetainedCanvasSink sink in _sinks) sink.WriteTransform(index, value); }
    private void NotifyStyle(int index, GpuStyle value)
    { foreach (IRetainedCanvasSink sink in _sinks) sink.WriteStyle(index, value); }
    private void NotifyClip(int index, GpuClip value)
    { foreach (IRetainedCanvasSink sink in _sinks) sink.WriteClip(index, value); }
    private void NotifySegment(int index, GpuSegment value)
    { foreach (IRetainedCanvasSink sink in _sinks) sink.WriteSegment(index, value); }
    private void NotifyPath(int index, GpuPath value)
    { foreach (IRetainedCanvasSink sink in _sinks) sink.WritePath(index, value); }

    internal GpuSegment[] SegmentSnapshot() => _segments.Count > 0 ? _segments.ToArray() : new GpuSegment[1];
    internal GpuPath[] PathSnapshot() => _paths.Count > 0 ? _paths.ToArray() : new GpuPath[1];
    internal GpuTransform[] TransformSnapshot() => _transforms.Count > 0 ? _transforms.ToArray() : [GpuTransform.Identity];
    internal GpuStyle[] StyleSnapshot() => _styles.Count > 0 ? _styles.ToArray() : new GpuStyle[1];
    internal GpuClip[] ClipSnapshot() => _clips.Count > 0 ? _clips.ToArray() : new GpuClip[1];
    internal uint[] OrderSnapshot() => _order.Count > 0 ? _order.ToArray() : new uint[1];
    internal uint OrderCount => (uint)_order.Count;

    internal void EmitRenderDiagnostics()
    {
        if (EngineDiagnostics.IsEnabled(EngineDiagnostics.Primitives)) EmitPrimitives();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sinks.Clear();
    }
}
