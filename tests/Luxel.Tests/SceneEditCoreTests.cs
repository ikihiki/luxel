using System.Numerics;
using Luxel.SceneEdit;

namespace Luxel.Tests;

/// <summary>ゲームエディタ GE-0 コア (ToDo 27 / ADR-0015) の単体テスト — SceneValue の形と往復、
/// SceneDoc/SceneEntity/SceneComponent の検証、決定的 JSON 往復 (未知コンポーネント保全含む)、
/// IComponentSchema (Transform2D/3D)、GameProject/ResPath。canvas 不要 (純データ)。</summary>
public class SceneEditCoreTests
{
    // ---- 組み立てヘルパ ----

    private static SceneComponent T2(float x = 0, float y = 0)
        => SceneComponent.Of("transform2d",
            ("pos", SceneValue.Of(new Vector2(x, y))),
            ("rotation", SceneValue.Of(0f)),
            ("scale", SceneValue.Of(Vector2.One)));

    private static SceneDoc RoundTrip(SceneDoc doc) => SceneJson.Deserialize(SceneJson.Serialize(doc));

    // ---- SceneValue ----

    [Fact]
    public void Value_KindsAndAccessors()
    {
        Assert.True(SceneValue.Of(true).AsBool());
        Assert.Equal(42, SceneValue.Of(42).AsInt());
        Assert.Equal(0.5f, SceneValue.Of(0.5f).AsFloat());
        Assert.Equal("res://a.png", SceneValue.Of("res://a.png").AsText());
        Assert.Equal(new Vector2(1, 2), SceneValue.Of(new Vector2(1, 2)).AsVec2());
        Assert.Equal(new Vector3(1, 2, 3), SceneValue.Of(new Vector3(1, 2, 3)).AsVec3());
        Assert.Equal(Quaternion.Identity, SceneValue.Of(Quaternion.Identity).AsQuat());
        // 形違いの読み出しは throw
        Assert.Throws<InvalidOperationException>(() => SceneValue.Of(1).AsText());
        Assert.Throws<InvalidOperationException>(() => SceneValue.Of("x").AsVec2());
    }

    [Fact]
    public void Value_FloatWritesShortestForm()
    {
        // (double)0.1f のノイズ (0.100000001…) が JSON に出ない
        var c = SceneComponent.Of("c", ("f", SceneValue.Of(0.1f)));
        var doc = SceneDoc.Of(SceneSpace.TwoD, [SceneEntity.Of(1, "e", c)]);
        Assert.Contains("\"f\": 0.1", SceneJson.Serialize(doc));
        Assert.Equal(0.1f, RoundTrip(doc).Entity(1).Component("c")!.Get("f")!.Value.AsFloat());
    }

    // ---- SceneComponent / SceneEntity / SceneDoc の検証 ----

    [Fact]
    public void Component_SortsFieldsAndRejectsReservedAndDuplicate()
    {
        var c = SceneComponent.Of("c", ("zzz", SceneValue.Of(1)), ("aaa", SceneValue.Of(2)));
        Assert.Equal(["aaa", "zzz"], c.Fields.Select(f => f.Name));   // 構築順に依らず名前順
        Assert.Throws<ArgumentException>(() => SceneComponent.Of("c", ("type", SceneValue.Of(1))));
        Assert.Throws<ArgumentException>(() => SceneComponent.Of("c", ("a", SceneValue.Of(1)), ("a", SceneValue.Of(2))));
    }

    [Fact]
    public void Component_WithReplacesOrAdds()
    {
        SceneComponent c = T2().With("pos", SceneValue.Of(new Vector2(5, 6))).With("extra", SceneValue.Of(7));
        Assert.Equal(new Vector2(5, 6), c.Get("pos")!.Value.AsVec2());
        Assert.Equal(7, c.Get("extra")!.Value.AsInt());
        Assert.Null(c.Get("nope"));
    }

    [Fact]
    public void Entity_RejectsDuplicateComponentType_AndLooksUpByType()
    {
        Assert.Throws<ArgumentException>(() => SceneEntity.Of(1, "e", T2(), T2()));
        var e = SceneEntity.Of(1, "e", T2(3, 4));
        Assert.Equal(new Vector2(3, 4), e.Component("transform2d")!.Get("pos")!.Value.AsVec2());
        Assert.Null(e.Component("nope"));
        Assert.Empty(e.WithoutComponent("transform2d").Components);
    }

    [Fact]
    public void Doc_RejectsDuplicateIds()
    {
        Assert.Throws<ArgumentException>(() => SceneDoc.Of(SceneSpace.TwoD, [SceneEntity.Of(1, "a"), SceneEntity.Of(1, "b")]));
        var l = TileLayer.Of(1, "g", "res://t.json", 16, 2, 2);
        Assert.Throws<ArgumentException>(() => SceneDoc.Of(SceneSpace.TwoD, [], [l, l]));
    }

    [Fact]
    public void TileLayer_ValidatesCellCountAndIndexing()
    {
        Assert.Throws<ArgumentException>(() => TileLayer.Of(1, "g", "res://t.json", 16, 2, 2, [1, 2, 3]));
        var l = TileLayer.Of(1, "g", "res://t.json", 16, 3, 2, [0, 1, 2, 3, 4, 5]);
        Assert.Equal(5, l.Cell(2, 1));   // 行優先
        Assert.Throws<ArgumentOutOfRangeException>(() => l.Cell(3, 0));
    }

    // ---- JSON 往復 (決定性 + 忠実性) ----

    [Fact]
    public void Json_RoundTripsBothSpaces_AndIsDeterministic()
    {
        var t3 = SceneComponent.Of("transform3d",
            ("pos", SceneValue.Of(new Vector3(1, 2, 3))),
            ("rotation", SceneValue.Of(Quaternion.CreateFromYawPitchRoll(0.3f, 0.2f, 0.1f))),
            ("scale", SceneValue.Of(new Vector3(1, 1, 1))));
        var doc2 = SceneDoc.Of(SceneSpace.TwoD, [SceneEntity.Of(1, "プレイヤー", T2(10, 20))],
            [TileLayer.Of(1, "ground", "res://atlas/tiles.json", 16, 3, 2, [0, 1, 1, 2, 0, 0])]);
        var doc3 = SceneDoc.Of(SceneSpace.ThreeD, [SceneEntity.Of(1, "fox", t3)]);

        foreach (SceneDoc doc in new[] { doc2, doc3 })
        {
            string json = SceneJson.Serialize(doc);
            // serialize→deserialize→serialize が文字列一致 (決定性 + 忠実性を同時に担保)
            Assert.Equal(json, SceneJson.Serialize(SceneJson.Deserialize(json)));
        }
        SceneDoc back = RoundTrip(doc3);
        Assert.Equal(SceneSpace.ThreeD, back.Space);
        // Quat が float ビット一致で戻る
        Assert.Equal(Quaternion.CreateFromYawPitchRoll(0.3f, 0.2f, 0.1f),
            back.Entity(1).Component("transform3d")!.Get("rotation")!.Value.AsQuat());
        // 日本語がエスケープされず読める形で出る
        Assert.Contains("プレイヤー", SceneJson.Serialize(doc2));
        // タイルは行 CSV
        Assert.Contains("\"0,1,1\"", SceneJson.Serialize(doc2));
        Assert.Equal(2, RoundTrip(doc2).Layer(1).Cell(0, 1));
    }

    [Fact]
    public void Json_PreservesUnknownComponentsAndShapes()
    {
        // スキーマに無い型 + どの形にも合わないネスト値 (Raw) が素通しで往復する
        var unknown = SceneComponent.Of("my_game_boss",
            ("phase", SceneValue.Of("enraged")),
            ("hp", SceneValue.Of(500)),
            ("drops", SceneValue.Raw("""{"coins":10,"items":["key","potion"]}""")));
        var doc = SceneDoc.Of(SceneSpace.TwoD, [SceneEntity.Of(7, "boss", unknown)]);
        string json = SceneJson.Serialize(doc);
        Assert.Equal(json, SceneJson.Serialize(SceneJson.Deserialize(json)));
        SceneComponent back = RoundTrip(doc).Entity(7).Component("my_game_boss")!;
        Assert.Equal(500, back.Get("hp")!.Value.AsInt());
        Assert.Equal(SceneValueKind.Raw, back.Get("drops")!.Value.Kind);
        Assert.Contains("\"potion\"", back.Get("drops")!.Value.AsRaw());
    }

    [Fact]
    public void Json_RejectsBadInput()
    {
        Assert.Throws<FormatException>(() => SceneJson.Deserialize("""{"entities":[]}"""));           // space 無し
        Assert.Throws<FormatException>(() => SceneJson.Deserialize("""{"space":"5d","entities":[]}"""));
    }

    // ---- スキーマ ----

    [Fact]
    public void Schema_BuiltInTransforms_AndNewComponentFillsDefaults()
    {
        SchemaRegistry reg = SceneSchemas.BuiltIns();
        Assert.Same(SceneSchemas.Transform2D, reg.TryGet("transform2d"));
        // space で出し分け: 2D シーンには transform3d が出ない
        Assert.Contains(SceneSchemas.Transform2D, reg.For(SceneSpace.TwoD));
        Assert.DoesNotContain(SceneSchemas.Transform3D, reg.For(SceneSpace.TwoD));
        Assert.Contains(SceneSchemas.Transform3D, reg.For(SceneSpace.ThreeD));

        SceneComponent c = SceneSchemas.NewComponent(SceneSchemas.Transform3D);
        Assert.Equal(Vector3.Zero, c.Get("pos")!.Value.AsVec3());
        Assert.Equal(Quaternion.Identity, c.Get("rotation")!.Value.AsQuat());
        Assert.Equal(Vector3.One, c.Get("scale")!.Value.AsVec3());
    }

    [Fact]
    public void Schema_ValidatesDefaultShapeAndEnum()
    {
        // 既定値の形が型に合わない (Float に Text)
        Assert.Throws<ArgumentException>(() => new ComponentSchema("bad", "Bad", SceneSpaces.Both,
            [new SceneFieldDef("speed", SceneFieldType.Float, SceneValue.Of("fast"))]));
        // Enum に選択肢が無い
        Assert.Throws<ArgumentException>(() => new ComponentSchema("bad", "Bad", SceneSpaces.Both,
            [new SceneFieldDef("mode", SceneFieldType.Enum, SceneValue.Of("a"))]));
        // 型→形の対応
        Assert.Equal(SceneValueKind.Vec4, SceneFieldTypes.KindOf(SceneFieldType.Quat));
        Assert.Equal(SceneValueKind.Vec4, SceneFieldTypes.KindOf(SceneFieldType.Color));
        Assert.Equal(SceneValueKind.Text, SceneFieldTypes.KindOf(SceneFieldType.AssetRef));
    }

    // ---- SceneRotation (Quat ↔ オイラー、インスペクタ表示用) ----

    [Fact]
    public void Rotation_EulerQuatRoundTrip()
    {
        // 一般姿勢: オイラー → Quat → オイラーで角度が戻る
        var e = new Vector3(20, 30, 10);
        Vector3 back = SceneRotation.ToEulerDegrees(SceneRotation.FromEulerDegrees(e));
        Assert.True((back - e).Length() < 0.01f, $"往復誤差: {back}");
        // Quat → オイラー → Quat は「同じ回転」(q と -q は同一視)
        var q = Quaternion.CreateFromYawPitchRoll(1.2f, 0.4f, -0.7f);
        Quaternion q2 = SceneRotation.FromEulerDegrees(SceneRotation.ToEulerDegrees(q));
        float dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(q), Quaternion.Normalize(q2)));
        Assert.True(dot > 0.9999f, $"回転が変わった: dot={dot}");
        Assert.Equal(Vector3.Zero, SceneRotation.ToEulerDegrees(Quaternion.Identity));
    }

    // ---- AtlasDef ----

    [Fact]
    public void Atlas_RoundTripsAndValidates()
    {
        var a = new AtlasDef { Image = "res://assets/tiles.png", TileWidth = 16, TileHeight = 24 };
        string json = AtlasDefJson.Serialize(a);
        Assert.Equal(json, AtlasDefJson.Serialize(AtlasDefJson.Deserialize(json)));   // 決定的往復
        AtlasDef back = AtlasDefJson.Deserialize(json);
        Assert.Equal(("res://assets/tiles.png", 16, 24), (back.Image, back.TileWidth, back.TileHeight));
        // 未設定 (空 Image) は許す、res:// でないパスと非正サイズは拒否
        Assert.Equal("", AtlasDefJson.Deserialize("""{"image":""}""").Image);
        Assert.Throws<ArgumentException>(() => AtlasDefJson.Deserialize("""{"image":"C:/x.png"}"""));
        Assert.Throws<FormatException>(() => AtlasDefJson.Deserialize("""{"image":"","tileWidth":0}"""));
    }

    // ---- GameProject / ResPath ----

    [Fact]
    public void Project_RoundTripsAndValidatesStartScene()
    {
        var p = new GameProject("マイゲーム", "res://scenes/main.scene.json", 960, 540);
        string json = GameProjectJson.Serialize(p);
        Assert.Equal(json, GameProjectJson.Serialize(GameProjectJson.Deserialize(json)));
        Assert.Equal(p, GameProjectJson.Deserialize(json));
        // window 省略で既定値
        Assert.Equal(1280, GameProjectJson.Deserialize("""{"name":"g","startScene":"res://s.json"}""").WindowWidth);
        // startScene が res:// でないと拒否
        Assert.Throws<ArgumentException>(() => GameProjectJson.Deserialize("""{"name":"g","startScene":"C:/evil"}"""));
    }

    [Fact]
    public void ResPath_ResolvesAndRejectsEscape()
    {
        Assert.Equal("assets/hero.png", ResPath.Resolve("res://assets/hero.png"));
        Assert.True(ResPath.Is("res://a"));
        Assert.False(ResPath.Is("assets/a.png"));
        Assert.Throws<ArgumentException>(() => ResPath.Resolve("assets/a.png"));       // scheme 無し
        Assert.Throws<ArgumentException>(() => ResPath.Resolve("res://../secret"));    // 脱出
        Assert.Throws<ArgumentException>(() => ResPath.Resolve("res://a//b"));         // 空セグメント
        Assert.Throws<ArgumentException>(() => ResPath.Resolve(@"res://a\b"));         // バックスラッシュ
    }
}
