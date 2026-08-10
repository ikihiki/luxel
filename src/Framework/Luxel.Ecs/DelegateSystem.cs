using Friflo.Engine.ECS.Systems;

namespace Luxel.Ecs;

/// <summary>プレーンな <see cref="Action"/> を Friflo <see cref="BaseSystem"/> にラップする内部ヘルパ。
/// <see cref="World.AddSystem(string, Action)"/> から使う。</summary>
internal sealed class DelegateSystem(Action action) : BaseSystem
{
    private readonly Action _action = action;
    protected override void OnUpdateGroup() => _action();
}
