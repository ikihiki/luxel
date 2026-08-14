namespace Luxel.Ecs;

/// <summary>
/// 描画補間: <see cref="InterpolatedTransform"/> を持つ entity の前/現ステップ TRS を
/// 補間係数 <c>alpha</c> [0,1) で lerp/slerp し、<see cref="LocalTransform"/> に書き込む。
///
/// <para>PreRender で 1 回呼ぶ (物理などの <see cref="Luxel.Ecs.Phase.PreRender"/> 相当)。
/// FixedUpdate 側は <see cref="InterpolatedTransform.Push"/> で毎ステップの位置を積むだけでよい。
/// alpha はapplicationが所有する<see cref="Luxel.Framework.Game.FixedTimestep"/>の<see cref="Luxel.Framework.Game.FixedTimestep.Alpha"/>を渡す。</para>
///
/// <para>GPU 非依存 — 単体テスト可能。<see cref="LocalTransform"/> を持たない entity には
/// component を足して書き込む。</para>
/// </summary>
public static class TransformInterpolationSystem
{
    /// <summary>登録 entity すべてを alpha で補間して <see cref="LocalTransform"/> に反映する。</summary>
    public static void Run(World world, float alpha)
    {
        // クエリ列挙中の構造変更を避けるため、収集 → 適用の 2 段。
        var updates = new List<(Friflo.Engine.ECS.Entity E, System.Numerics.Matrix4x4 M)>();
        world.Query<InterpolatedTransform>().ForEachEntity((ref InterpolatedTransform it, Friflo.Engine.ECS.Entity e) =>
            updates.Add((e, it.Sample(alpha))));
        foreach ((Friflo.Engine.ECS.Entity e, System.Numerics.Matrix4x4 m) in updates)
        {
            if (e.HasComponent<LocalTransform>()) { ref var lt = ref e.GetComponent<LocalTransform>(); lt.Matrix = m; }
            else e.AddComponent(new LocalTransform(m));
        }
    }
}
