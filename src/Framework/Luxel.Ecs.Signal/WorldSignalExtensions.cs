using Friflo.Engine.ECS;
using Luxel.Ecs;

namespace Luxel.Ecs.Signal;

/// <summary>
/// <see cref="World"/> と <see cref="Luxel.UI.Signal{T}"/> のブリッジ。
/// Entity の component 変化を Signal に伝播させる薄い extension 層。
///
/// Luxel.Ecs 本体は Friflo 以外に依存しない純粋 ECS。UI の Signal 連携はこの
/// プロジェクトに切り離してある (依存: Luxel.Ecs + Luxel.UI)。
///
/// <para><b>使い方</b>:</para>
/// <code>
/// using Luxel.Ecs.Signal;
/// var sig = world.Signal&lt;Position&gt;(entity); // 同じ (entity, T) は同一インスタンスを返す
/// sig.Value = new Position(...); // Friflo の変更通知で他の Signal 監視者にも伝わる
/// </code>
/// </summary>
public static class WorldSignalExtensions
{
    // (World, entityId, compType) → Signal<T> のキャッシュ。World ごとに独立。
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<World, Dictionary<(int, Type), object>> _cache
        = new();

    /// <summary>
    /// Entity の特定 component に対する <see cref="Luxel.UI.Signal{T}"/> を取得。
    /// 同じ (entity, component) 組合せに対しては同じ Signal を返す (キャッシュ)。
    /// Friflo の <c>OnComponentChanged</c> イベントで自動 notify される。
    /// </summary>
    public static Luxel.UI.Signal<T> Signal<T>(this World world, Entity entity) where T : struct, IComponent
    {
        var perWorld = _cache.GetValue(world, _ => new Dictionary<(int, Type), object>());
        var key = (entity.Id, typeof(T));
        if (perWorld.TryGetValue(key, out var existing))
            return (Luxel.UI.Signal<T>)existing;

        T initial = entity.HasComponent<T>() ? entity.GetComponent<T>() : default;
        var sig = new Luxel.UI.Signal<T>(initial);
        perWorld[key] = sig;

        entity.OnComponentChanged += ev =>
        {
            if (ev.ComponentType.Type == typeof(T))
            {
                if (entity.HasComponent<T>())
                    sig.Value = entity.GetComponent<T>();
            }
        };
        return sig;
    }
}
