using System.Numerics;
using Friflo.Engine.ECS;
using Luxel.Animation;
using Luxel.Ecs;

namespace Luxel.Animation.ThreeD;

/// <summary>
/// ECS World への <see cref="IAnimationTarget"/> アダプタ。
/// Path 形式: "{entityName}/translation" | "{entityName}/rotation" | "{entityName}/scale"。
/// glTF 2.0 の channel.target.path と同じ語彙を使う。<see cref="LocalTransform"/> を TRS に分解 → 該当成分を
/// 上書き → 再合成して書き戻す。
///
/// 拡張は <see cref="RegisterPropertyHandler"/> で任意の property name に対応可。
/// </summary>
public sealed class EcsAnimationTarget : IAnimationTarget
{
    private readonly Luxel.Ecs.World _world;
    private readonly Dictionary<string, Entity> _entities = new();
    private readonly Dictionary<string, Action<Entity, object>> _customHandlers = new();

    public EcsAnimationTarget(Luxel.Ecs.World world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>path 文字列 → entity のマッピングを登録。</summary>
    public EcsAnimationTarget Bind(string name, Entity entity)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required", nameof(name));
        _entities[name] = entity;
        return this;
    }

    /// <summary>"translation"/"rotation"/"scale" 以外の property を扱うカスタムハンドラ。</summary>
    public EcsAnimationTarget RegisterPropertyHandler(string propertyName, Action<Entity, object> handler)
    {
        _customHandlers[propertyName] = handler;
        return this;
    }

    public void Apply(string path, object value)
    {
        // path = "{entityName}/{property}"
        int slash = path.IndexOf('/');
        if (slash <= 0 || slash == path.Length - 1) return;
        string entityName = path[..slash];
        string property = path[(slash + 1)..];

        if (!_entities.TryGetValue(entityName, out var entity)) return;

        switch (property)
        {
            case "translation":
                ApplyTranslation(entity, (Vector3)value);
                break;
            case "rotation":
                ApplyRotation(entity, (Quaternion)value);
                break;
            case "scale":
                ApplyScale(entity, (Vector3)value);
                break;
            case "color":
                _world.Set(entity, new Color3D((Vector4)value));
                break;
            default:
                if (_customHandlers.TryGetValue(property, out var h)) h(entity, value);
                break;
        }
    }

    private void ApplyTranslation(Entity e, Vector3 v)
    {
        Decompose(e, out Vector3 s, out Quaternion r, out Vector3 _);
        Compose(e, s, r, v);
    }

    private void ApplyRotation(Entity e, Quaternion q)
    {
        Decompose(e, out Vector3 s, out Quaternion _, out Vector3 t);
        Compose(e, s, q, t);
    }

    private void ApplyScale(Entity e, Vector3 v)
    {
        Decompose(e, out Vector3 _, out Quaternion r, out Vector3 t);
        Compose(e, v, r, t);
    }

    private void Decompose(Entity e, out Vector3 scale, out Quaternion rot, out Vector3 trans)
    {
        if (_world.TryGet<LocalTransform>(e, out var lt))
        {
            if (!Matrix4x4.Decompose(lt.Matrix, out scale, out rot, out trans))
            {
                scale = Vector3.One; rot = Quaternion.Identity; trans = Vector3.Zero;
            }
        }
        else { scale = Vector3.One; rot = Quaternion.Identity; trans = Vector3.Zero; }
    }

    private void Compose(Entity e, Vector3 scale, Quaternion rot, Vector3 trans)
    {
        var m = Matrix4x4.CreateScale(scale)
              * Matrix4x4.CreateFromQuaternion(rot)
              * Matrix4x4.CreateTranslation(trans);
        _world.Set(e, new LocalTransform(m));
    }
}
