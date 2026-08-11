using System.Numerics;
using Luxel.Controls;
using Luxel.SceneEdit;

namespace Luxel.Tests;

/// <summary>3D 空間アダプタ (ToDo 27 GE-8) の canvas 非依存部テスト。</summary>
public class SceneSpace3DAdapterTests
{
    private static SceneEntity E(int id, float x, float y, float z)
        => SceneEntity.Of(id, $"e{id}", SceneSchemas.NewComponent(SceneSchemas.Transform3D)
            .With("pos", SceneValue.Of(new Vector3(x, y, z))));

    private static Vector3 PosOf(SceneDoc doc, int id) => doc.Entity(id).Component("transform3d")!.Get("pos")!.Value.AsVec3();

    [Fact]
    public void HitEntity_ProjectedBoxCenterHits()
    {
        var ad = new SceneSpace3DAdapter();
        SceneDoc doc = SceneDoc.Of(SceneSpace.ThreeD, [E(1, 0, 0, 0), E(2, 2, 0, 0)]);

        Vector2 center = ad.EntityLocalCenter(doc, 1);

        Assert.Equal(1, ad.HitEntity(doc, center));
        Assert.Equal(-1, ad.HitEntity(doc, new Vector2(-1000, -1000)));
    }

    [Fact]
    public void BuildMove_AxisXAndAxisZ_UseProjectedAxis()
    {
        var ad = new SceneSpace3DAdapter();
        SceneDoc doc = SceneDoc.Of(SceneSpace.ThreeD, [E(1, 0, 0, 0)]);
        Vector2 c = ad.EntityLocalCenter(doc, 1);

        Vector2 xEnd = ad.LocalOfPlane(new Vector2(1, 0));
        SceneDoc moved = new SceneChangeSet(ad.BuildMove(doc, [1], xEnd - c, SceneHandleKind.AxisX, snap: false)).Apply(doc);
        Assert.True(PosOf(moved, 1).X > 0.9f);
        Assert.Equal(0f, PosOf(moved, 1).Y, 3);

        Vector2 zEnd = ad.LocalOfPlane(new Vector2(0, 1));
        moved = new SceneChangeSet(ad.BuildMove(doc, [1], zEnd - c, SceneHandleKind.AxisZ, snap: false)).Apply(doc);
        Assert.True(PosOf(moved, 1).Z > 0.9f);
        Assert.Equal(0f, PosOf(moved, 1).X, 3);
    }

    [Fact]
    public void OffsetDuplicate_ShiftsOnGroundPlane()
    {
        var ad = new SceneSpace3DAdapter();
        SceneEntity moved = ad.OffsetDuplicate(E(1, 1, 2, 3));

        Assert.Equal(new Vector3(1.75f, 2, 3.75f), moved.Component("transform3d")!.Get("pos")!.Value.AsVec3());
        SceneEntity ghost = SceneEntity.Of(9, "ghost");
        Assert.Same(ghost, ad.OffsetDuplicate(ghost));
    }

    [Fact]
    public void PanAndZoom_OrbitAndDollyCamera()
    {
        var ad = new SceneSpace3DAdapter();
        float yaw = ad.Camera.Yaw;
        float distance = ad.Camera.Distance;

        ad.Pan(new Vector2(20, 10));
        ad.ZoomAt(2, Vector2.Zero);

        Assert.NotEqual(yaw, ad.Camera.Yaw);
        Assert.True(ad.Camera.Distance < distance);
    }
}
