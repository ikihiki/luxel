using System.Numerics;

namespace Luxel.SceneEdit;

/// <summary>
/// フィールドの**型付きの意味** — インスペクタのエディタ選択とランタイム構築 (SceneCompiler) の
/// 共通語彙。値の保存形は <see cref="SceneValueKind"/> (形ベース) で、この型はその解釈
/// (<see cref="SceneFieldTypes.KindOf"/> が対応を定める)。2D/3D 両対応の設計原則 2:
/// Vec3/Quat/Color 含め**初日から全部**切る (後から足すとスキーマ + JSON + PropertyGrid の
/// 3 箇所に同時に手が入るため)。
/// </summary>
public enum SceneFieldType
{
    Bool,
    Int,
    Float,
    String,
    /// <summary>選択肢つき文字列 (<see cref="SceneFieldDef.EnumValues"/> が選択肢)。</summary>
    Enum,
    Vec2,
    Vec3,
    /// <summary>クォータニオン (保存形 Vec4)。エディタ表示はオイラー角に変換するが保存は Quat のまま (往復劣化を避ける)。</summary>
    Quat,
    /// <summary>RGBA (保存形 Vec4、各成分 0..1)。</summary>
    Color,
    /// <summary>res:// のアセット参照 (保存形 Text)。<see cref="SceneFieldDef.AssetKind"/> で種類を絞れる。</summary>
    AssetRef,
}

/// <summary>スキーマが対応する空間 (原則 2: 各スキーマは対応 space を宣言し、
/// インスペクタの「コンポーネント追加」やパレットが出し分ける)。</summary>
[Flags]
public enum SceneSpaces
{
    TwoD = 1,
    ThreeD = 2,
    Both = TwoD | ThreeD,
}

public static class SceneSpacesExtensions
{
    public static bool Supports(this SceneSpaces spaces, SceneSpace space)
        => (spaces & (space == SceneSpace.TwoD ? SceneSpaces.TwoD : SceneSpaces.ThreeD)) != 0;
}

/// <summary>フィールド定義 1 個 — 名前・型・既定値。Enum は選択肢、AssetRef は種類フィルタを持てる。</summary>
public sealed record SceneFieldDef(
    string Name,
    SceneFieldType Type,
    SceneValue Default,
    IReadOnlyList<string>? EnumValues = null,
    string? AssetKind = null);

/// <summary>
/// コンポーネント種別のスキーマ — エディタ (インスペクタ/パレット) とランタイム構築の
/// **単一の真実** (ADR-0015)。SceneDoc 自体はスキーマを参照しない (未知型も保持する) —
/// スキーマは既知型の解釈にだけ使う。
/// </summary>
public interface IComponentSchema
{
    /// <summary><see cref="SceneComponent.Type"/> と一致する登録キー (例 "transform2d")。</summary>
    string Type { get; }

    /// <summary>インスペクタ等に出す表示名。</summary>
    string DisplayName { get; }

    /// <summary>対応する空間。</summary>
    SceneSpaces Spaces { get; }

    IReadOnlyList<SceneFieldDef> Fields { get; }
}

/// <inheritdoc cref="IComponentSchema"/>
public sealed class ComponentSchema : IComponentSchema
{
    public string Type { get; }

    public string DisplayName { get; }

    public SceneSpaces Spaces { get; }

    public IReadOnlyList<SceneFieldDef> Fields { get; }

    public ComponentSchema(string type, string displayName, SceneSpaces spaces, IReadOnlyList<SceneFieldDef> fields)
    {
        if (string.IsNullOrEmpty(type)) throw new ArgumentException("スキーマ型が空");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (SceneFieldDef f in fields)
        {
            if (f.Name == "type") throw new ArgumentException("フィールド名 \"type\" は予約");
            if (!seen.Add(f.Name)) throw new ArgumentException($"フィールド名の重複: {f.Name}");
            if (f.Default.Kind != SceneFieldTypes.KindOf(f.Type))
                throw new ArgumentException($"{type}.{f.Name}: 既定値の形 {f.Default.Kind} が型 {f.Type} (形 {SceneFieldTypes.KindOf(f.Type)}) に合わない");
            if (f.Type == SceneFieldType.Enum && (f.EnumValues is null || f.EnumValues.Count == 0))
                throw new ArgumentException($"{type}.{f.Name}: Enum に選択肢が無い");
        }
        Type = type;
        DisplayName = displayName;
        Spaces = spaces;
        Fields = fields;
    }
}

public static class SceneFieldTypes
{
    /// <summary>型付きの意味 → 保存形の対応 (単一の真実)。</summary>
    public static SceneValueKind KindOf(SceneFieldType type) => type switch
    {
        SceneFieldType.Bool => SceneValueKind.Bool,
        SceneFieldType.Int or SceneFieldType.Float => SceneValueKind.Number,
        SceneFieldType.String or SceneFieldType.Enum or SceneFieldType.AssetRef => SceneValueKind.Text,
        SceneFieldType.Vec2 => SceneValueKind.Vec2,
        SceneFieldType.Vec3 => SceneValueKind.Vec3,
        SceneFieldType.Quat or SceneFieldType.Color => SceneValueKind.Vec4,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}

/// <summary>スキーマ登録簿 — 型キー→スキーマ。ドメイン (組み込み + ゲーム固有) が登録する。</summary>
public sealed class SchemaRegistry
{
    private readonly Dictionary<string, IComponentSchema> _byType = new(StringComparer.Ordinal);
    private readonly List<IComponentSchema> _all = [];

    public SchemaRegistry Add(IComponentSchema schema)
    {
        if (!_byType.TryAdd(schema.Type, schema))
            throw new ArgumentException($"スキーマ型の重複: {schema.Type}");
        _all.Add(schema);
        return this;
    }

    public IComponentSchema? TryGet(string type) => _byType.GetValueOrDefault(type);

    public IReadOnlyList<IComponentSchema> All => _all;

    /// <summary>指定空間のシーンに追加できるスキーマ (インスペクタ/パレットの出し分け)。</summary>
    public IEnumerable<IComponentSchema> For(SceneSpace space) => _all.Where(s => s.Spaces.Supports(space));
}

/// <summary>組み込みスキーマ (原則 1: Transform2D と Transform3D は**別スキーマ**として
/// 初日から両方定義 — 混在や自動変換はしない)。</summary>
public static class SceneSchemas
{
    public static readonly IComponentSchema Transform2D = new ComponentSchema(
        "transform2d", "Transform 2D", SceneSpaces.TwoD,
        [
            new SceneFieldDef("pos", SceneFieldType.Vec2, SceneValue.Of(Vector2.Zero)),
            new SceneFieldDef("rotation", SceneFieldType.Float, SceneValue.Of(0f)),
            new SceneFieldDef("scale", SceneFieldType.Vec2, SceneValue.Of(Vector2.One)),
        ]);

    public static readonly IComponentSchema Transform3D = new ComponentSchema(
        "transform3d", "Transform 3D", SceneSpaces.ThreeD,
        [
            new SceneFieldDef("pos", SceneFieldType.Vec3, SceneValue.Of(Vector3.Zero)),
            new SceneFieldDef("rotation", SceneFieldType.Quat, SceneValue.Of(Quaternion.Identity)),
            new SceneFieldDef("scale", SceneFieldType.Vec3, SceneValue.Of(Vector3.One)),
        ]);

    /// <summary>csx ビヘイビア (ADR-0018)。script はプロジェクト内 .csx への res:// 参照 —
    /// ランタイム (Luxel.Player) が ScriptHost でコンパイルし毎ステップ呼ぶ。</summary>
    public static readonly IComponentSchema Behaviour = new ComponentSchema(
        "behaviour", "Behaviour (csx)", SceneSpaces.Both,
        [new SceneFieldDef("script", SceneFieldType.AssetRef, SceneValue.Of(""), AssetKind: "csx")]);

    /// <summary>3D メッシュ参照。asset は project 内の glTF/glb への res:// 参照。</summary>
    public static readonly IComponentSchema Mesh3D = new ComponentSchema(
        "mesh3d", "Mesh 3D", SceneSpaces.ThreeD,
        [new SceneFieldDef("asset", SceneFieldType.AssetRef, SceneValue.Of(""), AssetKind: "glb")]);

    /// <summary>3D カメラ。SceneCompiler が最初の camera3d を Player3DWorld の OrbitCamera 初期値に使う。</summary>
    public static readonly IComponentSchema Camera3D = new ComponentSchema(
        "camera3d", "Camera 3D", SceneSpaces.ThreeD,
        [
            new SceneFieldDef("target", SceneFieldType.Vec3, SceneValue.Of(Vector3.Zero)),
            new SceneFieldDef("distance", SceneFieldType.Float, SceneValue.Of(8f)),
            new SceneFieldDef("yaw", SceneFieldType.Float, SceneValue.Of(0.72f)),
            new SceneFieldDef("pitch", SceneFieldType.Float, SceneValue.Of(0.42f)),
        ]);

    /// <summary>組み込み分を登録した新しい登録簿。</summary>
    public static SchemaRegistry BuiltIns() => new SchemaRegistry().Add(Transform2D).Add(Transform3D).Add(Behaviour).Add(Mesh3D).Add(Camera3D);

    /// <summary>スキーマの既定値で埋めた新しいコンポーネント (インスペクタの「追加」の実体)。</summary>
    public static SceneComponent NewComponent(IComponentSchema schema)
        => SceneComponent.Of(schema.Type, schema.Fields.Select(f => new SceneField(f.Name, f.Default)));
}
