using System.Numerics;
using Friflo.Engine.ECS;
using Luxel.UI;

namespace Luxel.Scene.UI;

/// <summary>
/// 形式非依存の Bone TRS 編集 state + Apply ヘルパ。
/// Signal で TRS を保持し、<see cref="Apply"/> で ECS の <see cref="Luxel.Ecs.LocalTransform"/> に反映。
/// Widget 構築は Sample 側で行う (Kit.Select / Kit.Slider / Button をシグナルに bind)。
/// </summary>
public sealed class BoneEditor
{
    public Luxel.UI.Signal<int> SelectedIndex { get; } = new(0);
    public Luxel.UI.Signal<float> TX { get; } = new(0f);
    public Luxel.UI.Signal<float> TY { get; } = new(0f);
    public Luxel.UI.Signal<float> TZ { get; } = new(0f);
    public Luxel.UI.Signal<float> RX { get; } = new(0f);
    public Luxel.UI.Signal<float> RY { get; } = new(0f);
    public Luxel.UI.Signal<float> RZ { get; } = new(0f);
    public Luxel.UI.Signal<float> SX { get; } = new(1f);
    public Luxel.UI.Signal<float> SY { get; } = new(1f);
    public Luxel.UI.Signal<float> SZ { get; } = new(1f);

    private readonly Luxel.Ecs.World _world;
    private readonly IReadOnlyList<Entity> _bones;

    public BoneEditor(Luxel.Ecs.World world, IReadOnlyList<Entity> bones)
    {
        _world = world;
        _bones = bones;
        // 選択切替で bone の現在値を Signal に流す
        Reactive.Effect(() => LoadFromBone(SelectedIndex.Value));
    }

    /// <summary>編集中の TRS を ECS の LocalTransform に反映 (Button から呼ぶ)。</summary>
    public void Apply()
    {
        int idx = SelectedIndex.Value;
        if (idx < 0 || idx >= _bones.Count) return;
        var rot = Quaternion.CreateFromYawPitchRoll(
            RY.Value * MathF.PI / 180f,
            RX.Value * MathF.PI / 180f,
            RZ.Value * MathF.PI / 180f);
        var mat = Matrix4x4.CreateScale(SX.Value, SY.Value, SZ.Value)
                * Matrix4x4.CreateFromQuaternion(rot)
                * Matrix4x4.CreateTranslation(TX.Value, TY.Value, TZ.Value);
        var bone = _bones[idx];
        if (bone.HasComponent<Luxel.Ecs.LocalTransform>())
            bone.RemoveComponent<Luxel.Ecs.LocalTransform>();
        bone.AddComponent(new Luxel.Ecs.LocalTransform(mat));
    }

    private void LoadFromBone(int idx)
    {
        if (idx < 0 || idx >= _bones.Count) return;
        var bone = _bones[idx];
        if (!bone.HasComponent<Luxel.Ecs.LocalTransform>()) return;
        var lt = bone.GetComponent<Luxel.Ecs.LocalTransform>();
        if (!Matrix4x4.Decompose(lt.Matrix, out var s, out var r, out var t)) return;
        TX.Value = t.X; TY.Value = t.Y; TZ.Value = t.Z;
        SX.Value = s.X; SY.Value = s.Y; SZ.Value = s.Z;
        var euler = QuatToEuler(r);
        RX.Value = euler.X; RY.Value = euler.Y; RZ.Value = euler.Z;
    }

    private static Vector3 QuatToEuler(Quaternion q)
    {
        float sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
        float cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
        float roll = MathF.Atan2(sinr_cosp, cosr_cosp);
        float sinp = 2 * (q.W * q.Y - q.Z * q.X);
        float pitch = MathF.Abs(sinp) >= 1 ? MathF.CopySign(MathF.PI / 2, sinp) : MathF.Asin(sinp);
        float siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
        float cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
        float yaw = MathF.Atan2(siny_cosp, cosy_cosp);
        return new Vector3(roll, pitch, yaw) * (180f / MathF.PI);
    }
}
