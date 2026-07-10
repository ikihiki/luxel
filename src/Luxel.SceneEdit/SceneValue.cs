using System.Globalization;
using System.Numerics;

namespace Luxel.SceneEdit;

/// <summary>
/// <see cref="SceneValue"/> の**形** (JSON に現れる形そのもの)。型付きの意味
/// (Int/Float/Enum/AssetRef/Quat/Color…) はスキーマ (<see cref="SceneFieldType"/>) 側の解釈で、
/// 値自身は形しか知らない — これにより**スキーマに無いコンポーネント/フィールドも
/// 素通しで往復できる** (未知保全、ADR-0015)。
/// </summary>
public enum SceneValueKind
{
    /// <summary>true / false。</summary>
    Bool,
    /// <summary>数値 (double で保持 — 整数は 2^53 まで無劣化)。Int/Float の別はスキーマの解釈。</summary>
    Number,
    /// <summary>文字列。String/Enum/AssetRef の別はスキーマの解釈。</summary>
    Text,
    /// <summary>数値 2 要素の配列。</summary>
    Vec2,
    /// <summary>数値 3 要素の配列。</summary>
    Vec3,
    /// <summary>数値 4 要素の配列。Quat/Color の別はスキーマの解釈。</summary>
    Vec4,
    /// <summary>上記のどの形でもない JSON (ネストしたオブジェクト等)。原文のまま保全する。</summary>
    Raw,
}

/// <summary>
/// コンポーネントフィールドの値 — 不変・値等価。JSON の形 (<see cref="SceneValueKind"/>) を
/// そのまま持つ小さな変種型。float からの生成は最短表現 (R) を経由して double に上げるので、
/// 保存される JSON は "0.1" のような読める形になり、かつ往復は正確。
/// </summary>
public readonly record struct SceneValue
{
    public SceneValueKind Kind { get; }
    private readonly double _x, _y, _z, _w;
    private readonly string? _s;

    private SceneValue(SceneValueKind kind, double x = 0, double y = 0, double z = 0, double w = 0, string? s = null)
    {
        Kind = kind;
        _x = x; _y = y; _z = z; _w = w; _s = s;
    }

    // ---- 生成 ----

    public static SceneValue Of(bool v) => new(SceneValueKind.Bool, v ? 1 : 0);

    public static SceneValue Of(double v) => new(SceneValueKind.Number, v);

    public static SceneValue Of(int v) => new(SceneValueKind.Number, v);

    public static SceneValue Of(float v) => new(SceneValueKind.Number, D(v));

    public static SceneValue Of(string v) => new(SceneValueKind.Text, s: v);

    public static SceneValue Of(Vector2 v) => new(SceneValueKind.Vec2, D(v.X), D(v.Y));

    public static SceneValue Of(Vector3 v) => new(SceneValueKind.Vec3, D(v.X), D(v.Y), D(v.Z));

    public static SceneValue Of(Vector4 v) => new(SceneValueKind.Vec4, D(v.X), D(v.Y), D(v.Z), D(v.W));

    public static SceneValue Of(Quaternion v) => new(SceneValueKind.Vec4, D(v.X), D(v.Y), D(v.Z), D(v.W));

    /// <summary>生 JSON (正規形 = パース時の並びのままコンパクト直列化した文字列) を包む。</summary>
    public static SceneValue Raw(string json) => new(SceneValueKind.Raw, s: json);

    internal static SceneValue Vec(double x, double y) => new(SceneValueKind.Vec2, x, y);

    internal static SceneValue Vec(double x, double y, double z) => new(SceneValueKind.Vec3, x, y, z);

    internal static SceneValue Vec(double x, double y, double z, double w) => new(SceneValueKind.Vec4, x, y, z, w);

    // float の最短表現を double に上げる — (double)0.1f = 0.100000001490116… の
    // ノイズを JSON に出さないための正規化
    private static double D(float f) => double.Parse(f.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    // ---- 読み出し (形が合わなければ InvalidOperationException) ----

    public bool AsBool() => Kind == SceneValueKind.Bool ? _x != 0 : throw Bad(SceneValueKind.Bool);

    public double AsDouble() => Kind == SceneValueKind.Number ? _x : throw Bad(SceneValueKind.Number);

    public float AsFloat() => (float)AsDouble();

    public int AsInt() => (int)AsDouble();

    public string AsText() => Kind == SceneValueKind.Text ? _s! : throw Bad(SceneValueKind.Text);

    public Vector2 AsVec2() => Kind == SceneValueKind.Vec2 ? new Vector2((float)_x, (float)_y) : throw Bad(SceneValueKind.Vec2);

    public Vector3 AsVec3() => Kind == SceneValueKind.Vec3 ? new Vector3((float)_x, (float)_y, (float)_z) : throw Bad(SceneValueKind.Vec3);

    public Vector4 AsVec4() => Kind == SceneValueKind.Vec4 ? new Vector4((float)_x, (float)_y, (float)_z, (float)_w) : throw Bad(SceneValueKind.Vec4);

    public Quaternion AsQuat() => Kind == SceneValueKind.Vec4 ? new Quaternion((float)_x, (float)_y, (float)_z, (float)_w) : throw Bad(SceneValueKind.Vec4);

    public string AsRaw() => Kind == SceneValueKind.Raw ? _s! : throw Bad(SceneValueKind.Raw);

    /// <summary>直列化用の数値成分 (Vec 系と Number)。</summary>
    internal (double X, double Y, double Z, double W) Components => (_x, _y, _z, _w);

    private InvalidOperationException Bad(SceneValueKind want)
        => new($"SceneValue は {Kind} — {want} として読めない");

    public override string ToString() => Kind switch
    {
        SceneValueKind.Bool => _x != 0 ? "true" : "false",
        SceneValueKind.Number => _x.ToString("R", CultureInfo.InvariantCulture),
        SceneValueKind.Text => _s!,
        SceneValueKind.Vec2 => $"({_x}, {_y})",
        SceneValueKind.Vec3 => $"({_x}, {_y}, {_z})",
        SceneValueKind.Vec4 => $"({_x}, {_y}, {_z}, {_w})",
        _ => _s ?? "",
    };
}
