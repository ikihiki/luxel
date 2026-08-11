using Friflo.Engine.ECS;

namespace Luxel.Ecs;

/// <summary>
/// Luxel の Schedule Phase (フレーム位相の規約名)。SystemGroup の名前に使う。
/// <para><b>Luxel の規約</b>: PreUpdate / Update / PostUpdate / PreRender / Render</para>
/// <para>RenderGraph の pass 順との接続点 (将来):
/// PreRender = ExtractAll, Render = RenderGraph.Execute。</para>
/// </summary>
public static class Phase
{
    /// <summary>Update 前の準備 phase (入力処理など)。</summary>
    public const string PreUpdate = "PreUpdate";
    /// <summary>ゲームロジック本体の phase。</summary>
    public const string Update = "Update";
    /// <summary>Update 後の後処理 phase (Transform 伝搬など)。</summary>
    public const string PostUpdate = "PostUpdate";
    /// <summary>描画前の抽出 phase (ECS → RenderGraph の Extract)。</summary>
    public const string PreRender = "PreRender";
    /// <summary>描画 phase (RenderGraph 実行)。</summary>
    public const string Render = "Render";
}

/// <summary>
/// Luxel 標準の <see cref="Friflo.Engine.ECS.Systems.SystemRoot"/> プリセット (5 phase の SystemGroup を内蔵)。
/// <code>
/// var root = ScheduleRoot.Create(world);
/// root.GetGroup(Phase.Update).Add(new MoveSystem());
/// root.Update(new UpdateTick { deltaTime = 1f / 60 });
/// </code>
/// </summary>
public static class ScheduleRoot
{
    /// <summary>5 phase の SystemGroup を登録済みの SystemRoot を作成。</summary>
    public static Friflo.Engine.ECS.Systems.SystemRoot Create(World world)
    {
        var root = new Friflo.Engine.ECS.Systems.SystemRoot(world.Store);
        root.Add(new Friflo.Engine.ECS.Systems.SystemGroup(Phase.PreUpdate));
        root.Add(new Friflo.Engine.ECS.Systems.SystemGroup(Phase.Update));
        root.Add(new Friflo.Engine.ECS.Systems.SystemGroup(Phase.PostUpdate));
        root.Add(new Friflo.Engine.ECS.Systems.SystemGroup(Phase.PreRender));
        root.Add(new Friflo.Engine.ECS.Systems.SystemGroup(Phase.Render));
        return root;
    }
}
