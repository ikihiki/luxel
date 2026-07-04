using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;

namespace Luxel.Ecs;

/// <summary>
/// Luxel 流の ECS World wrapper。Friflo の <see cref="EntityStore"/> をベースにする純粋 ECS ラッパ。
/// Signal 連携などの外部依存は <c>Luxel.Ecs.Signal</c> プロジェクトの拡張メソッドで提供する。
///
/// <para><b>使い方</b>:</para>
/// <code>
/// using var world = new World();
/// var entity = world.CreateEntity(new Position(1, 2, 3), new Velocity(0, 0, 1));
/// foreach (var e in world.Query&lt;Position&gt;().Entities)
///     Console.WriteLine(e.GetComponent&lt;Position&gt;());
/// </code>
/// </summary>
public sealed class World : IDisposable
{
    /// <summary>Friflo の生 <see cref="EntityStore"/>。高度な操作はこれ経由で。</summary>
    public EntityStore Store { get; }

    /// <summary>空の <see cref="EntityStore"/> を持つ World を生成。</summary>
    public World() { Store = new EntityStore(); }

    /// <summary>新規 Entity を作成 (component なし)。</summary>
    public Entity CreateEntity() => Store.CreateEntity();

    /// <summary>component 付きで Entity 作成。</summary>
    public Entity CreateEntity<T1>(T1 c1) where T1 : struct, IComponent
        => Store.CreateEntity(c1);

    /// <summary>component 2 つ付きで Entity 作成。</summary>
    public Entity CreateEntity<T1, T2>(T1 c1, T2 c2)
        where T1 : struct, IComponent where T2 : struct, IComponent
        => Store.CreateEntity(c1, c2);

    /// <summary>component 3 つ付きで Entity 作成。</summary>
    public Entity CreateEntity<T1, T2, T3>(T1 c1, T2 c2, T3 c3)
        where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent
        => Store.CreateEntity(c1, c2, c3);

    /// <summary>Friflo の Query API を直接公開。</summary>
    public ArchetypeQuery<T1> Query<T1>() where T1 : struct, IComponent => Store.Query<T1>();
    /// <summary>2 component 版の Query。</summary>
    public ArchetypeQuery<T1, T2> Query<T1, T2>() where T1 : struct, IComponent where T2 : struct, IComponent => Store.Query<T1, T2>();
    /// <summary>3 component 版の Query。</summary>
    public ArchetypeQuery<T1, T2, T3> Query<T1, T2, T3>() where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent => Store.Query<T1, T2, T3>();

    // ==================== System / Phase =====================

    private SystemRoot? _root;
    private readonly Dictionary<string, SystemGroup> _phaseGroups = new();

    private SystemGroup EnsureGroup(string phase)
    {
        _root ??= new SystemRoot(Store);
        if (!_phaseGroups.TryGetValue(phase, out var g))
        {
            g = new SystemGroup(phase);
            if (_perfMonitoring) g.SetMonitorPerf(true);
            _root.Add(g);
            _phaseGroups[phase] = g;
        }
        return g;
    }

    private bool _perfMonitoring;

    /// <summary>全 phase group で <see cref="SystemGroup.MonitorPerf"/> を有効化する。以後 <see cref="CollectSystemTimings"/> で
    /// child system 単位の <c>Perf.LastMs</c> が取れる。呼び出しは冪等。</summary>
    public void EnableSystemPerfMonitor()
    {
        if (_perfMonitoring) return;
        _perfMonitoring = true;
        foreach (var g in _phaseGroups.Values) g.SetMonitorPerf(true);
    }

    /// <summary>指定 phase の各子 System の (Name, LastMs) を返す。<see cref="EnableSystemPerfMonitor"/> が有効なときのみ意味がある。</summary>
    public IReadOnlyList<(string Name, double LastMs)> CollectSystemTimings(string phase)
    {
        if (!_phaseGroups.TryGetValue(phase, out var g)) return Array.Empty<(string, double)>();
        var list = new List<(string, double)>(g.ChildSystems.Count);
        foreach (var s in g.ChildSystems) list.Add((s.Name, s.Perf.LastMs));
        return list;
    }

    /// <summary>Friflo <see cref="BaseSystem"/> を phase に登録。</summary>
    public void AddSystem(string phase, BaseSystem system) => EnsureGroup(phase).Add(system);

    /// <summary>プレーンな <see cref="Action"/> を phase に登録 (method group で System 化)。</summary>
    public void AddSystem(string phase, Action action) => EnsureGroup(phase).Add(new DelegateSystem(action));

    /// <summary>指定 phase の SystemGroup を Update する。登録が無い phase は no-op。</summary>
    public void RunPhase(string phase, UpdateTick tick)
    {
        if (_phaseGroups.TryGetValue(phase, out var g)) g.Update(tick);
    }

    /// <summary>破棄 (現状 no-op。EntityStore は IDisposable ではない)。</summary>
    public void Dispose() { /* EntityStore は IDisposable ではない */ }

    // === 旧 Luxel.ThreeD.World 互換 API (移行期間用) ===

    /// <summary>旧 API 互換: 新規 Entity 作成 (<c>CreateEntity()</c> と同じ)。</summary>
    public Entity Create() => Store.CreateEntity();
    /// <summary>旧 API 互換: component を追加または上書き。</summary>
    public void Set<T>(Entity e, T c) where T : struct, IComponent
    {
        if (e.HasComponent<T>()) { ref var existing = ref e.GetComponent<T>(); existing = c; }
        else e.AddComponent(c);
    }
    /// <summary>旧 API 互換: component を持つか。</summary>
    public bool Has<T>(Entity e) where T : struct, IComponent => e.HasComponent<T>();
    /// <summary>旧 API 互換: component を取得。</summary>
    public T Get<T>(Entity e) where T : struct, IComponent => e.GetComponent<T>();
    /// <summary>旧 API 互換: component があれば取得して true。</summary>
    public bool TryGet<T>(Entity e, out T c) where T : struct, IComponent
    {
        if (e.HasComponent<T>()) { c = e.GetComponent<T>(); return true; }
        c = default;
        return false;
    }
    /// <summary>Store 内の Entity 総数。</summary>
    public int Count => Store.Count;

    /// <summary>旧 API 互換: <c>(Entity, T)</c> tuple で列挙 (LINQ で扱いやすい)。</summary>
    public IEnumerable<(Entity Entity, T C1)> QueryEnumerable<T>() where T : struct, IComponent
    {
        var results = new List<(Entity, T)>();
        Store.Query<T>().ForEachEntity((ref T c, Entity e) => results.Add((e, c)));
        return results;
    }
    /// <summary>旧 API 互換: 2 component 版の tuple 列挙。</summary>
    public IEnumerable<(Entity Entity, T1 C1, T2 C2)> QueryEnumerable<T1, T2>()
        where T1 : struct, IComponent where T2 : struct, IComponent
    {
        var results = new List<(Entity, T1, T2)>();
        Store.Query<T1, T2>().ForEachEntity((ref T1 c1, ref T2 c2, Entity e) => results.Add((e, c1, c2)));
        return results;
    }
    /// <summary>旧 API 互換: 3 component 版の tuple 列挙。</summary>
    public IEnumerable<(Entity Entity, T1 C1, T2 C2, T3 C3)> QueryEnumerable<T1, T2, T3>()
        where T1 : struct, IComponent where T2 : struct, IComponent where T3 : struct, IComponent
    {
        var results = new List<(Entity, T1, T2, T3)>();
        Store.Query<T1, T2, T3>().ForEachEntity((ref T1 c1, ref T2 c2, ref T3 c3, Entity e) => results.Add((e, c1, c2, c3)));
        return results;
    }
}

/// <summary>旧 Luxel.ThreeD.Entity 互換 (Friflo Entity を re-export)。</summary>
public static class EntityExtensions
{
    /// <summary>Entity が有効 (null でない) かどうか。</summary>
    public static bool IsValid(this Entity e) => !e.IsNull;
}
