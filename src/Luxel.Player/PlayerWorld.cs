using System.Numerics;
using Luxel;
using Luxel.SceneEdit;
using Luxel.TwoD;
using Luxel.Typography;

namespace Luxel.Player;

public interface IPlayerWorld
{
    IReadOnlyList<PlayerEntity> Entities { get; }
    float Time { get; }
    HashSet<string> KeysDown { get; }
    PlayerBehaviours? Behaviours { get; set; }
    string? SceneRequest { get; }
    PlayerEntity Entity(int id);
    PlayerEntity? Find(string name);
    void RequestScene(string resPath);
    void Update(float dt);
    void Render(Scene2D s, float viewW, float viewH, VectorFont? font = null, float fontSize = 13);
}

/// <summary>
/// ランタイムのエンティティ 1 個 — SceneCompiler が <see cref="SceneEntity"/> から展開する
/// **可変**状態。transform2d は第一級フィールド (<see cref="Pos"/>/<see cref="Rotation"/>/<see cref="Scale"/>)
/// に展開され、コンポーネントデータ袋には残らない (二重の真実を避ける — スクリプトは e.Pos を使う)。
/// その他のコンポーネントは形ベースの <see cref="SceneValue"/> のまま持ち、csx ビヘイビア (S2) が
/// <see cref="Field"/>/<see cref="SetField"/> で読み書きする。ランタイム変更は保存されない
/// (プレイ状態は使い捨て — ADR-0017 の契約)。
/// </summary>
public sealed class PlayerEntity
{
    public int Id { get; }

    public string Name { get; }

    /// <summary>transform2d があったか (無いエンティティは論理のみで描画されない)。</summary>
    public bool HasTransform { get; }

    public Vector2 Pos;
    public float Rotation;
    public Vector2 Scale = Vector2.One;

    /// <summary>transform3d があったか。</summary>
    public bool HasTransform3D { get; }

    public Vector3 Pos3D;
    public Quaternion Rotation3D = Quaternion.Identity;
    public Vector3 Scale3D = Vector3.One;

    public string? MeshAsset { get; }

    public bool HasMesh3D => !string.IsNullOrWhiteSpace(MeshAsset);

    private readonly Dictionary<string, SceneComponent> _components;

    internal PlayerEntity(SceneEntity source)
    {
        Id = source.Id;
        Name = source.Name;
        _components = new Dictionary<string, SceneComponent>(StringComparer.Ordinal);
        foreach (SceneComponent c in source.Components)
        {
            if (c.Type == "transform2d")
            {
                HasTransform = true;
                Pos = c.Get("pos")?.AsVec2() ?? Vector2.Zero;
                Rotation = c.Get("rotation")?.AsFloat() ?? 0f;
                Scale = c.Get("scale")?.AsVec2() ?? Vector2.One;
            }
            else if (c.Type == "transform3d")
            {
                HasTransform3D = true;
                Pos3D = c.Get("pos")?.AsVec3() ?? Vector3.Zero;
                Rotation3D = c.Get("rotation")?.AsQuat() ?? Quaternion.Identity;
                Scale3D = c.Get("scale")?.AsVec3() ?? Vector3.One;
            }
            else if (c.Type == "mesh3d")
            {
                MeshAsset = c.Get("asset")?.AsText();
                _components[c.Type] = c;
            }
            else _components[c.Type] = c;
        }
    }

    /// <summary>コンポーネントを持っているか (transform2d は対象外 — 第一級フィールドを使う)。</summary>
    public bool Has(string component) => _components.ContainsKey(component);

    /// <summary>コンポーネントフィールドを読む (無ければ null)。</summary>
    public SceneValue? Field(string component, string field)
        => _components.TryGetValue(component, out SceneComponent? c) ? c.Get(field) : null;

    /// <summary>コンポーネントフィールドを書く (ランタイム状態のみ、保存されない)。
    /// コンポーネントが無ければ例外 (スクリプトの打ち間違いを隠さない)。</summary>
    public void SetField(string component, string field, SceneValue value)
    {
        if (!_components.TryGetValue(component, out SceneComponent? c))
            throw new KeyNotFoundException($"コンポーネントが無い: entity {Id} の {component}");
        _components[component] = c.With(field, value);
    }
}

/// <summary>
/// 2D シーンのランタイム world (SceneCompiler の 2D バックエンドの出力)。固定 dt の
/// <see cref="Update"/> で駆動し (wall-clock 禁止 — golden/リプレイの決定性)、
/// <see cref="Render"/> が Scene2D へ全体を描く。見た目はエディタと同じプレースホルダ
/// (タイル = <see cref="TilePalette"/>、エンティティ = 箱 + 名前。tint コンポーネントが
/// あれば箱色に反映) — 実アトラス描画はアセット配線後に差し替える。
/// </summary>
public sealed class Player2DWorld : IPlayerWorld
{
    /// <summary>エンティティ表示ボックス (world px、エディタの既定と同じ)。</summary>
    public static readonly Vector2 BoxSize = new(96, 40);

    private readonly List<PlayerEntity> _entities;
    private readonly Dictionary<int, PlayerEntity> _byId;
    private readonly List<TileLayer> _layers;

    internal Player2DWorld(IEnumerable<PlayerEntity> entities, IEnumerable<TileLayer> layers)
    {
        _entities = entities.ToList();
        _byId = _entities.ToDictionary(e => e.Id);
        _layers = layers.ToList();
    }

    public IReadOnlyList<PlayerEntity> Entities => _entities;

    public IReadOnlyList<TileLayer> Layers => _layers;

    /// <summary>累積時間 (固定 dt の合計)。</summary>
    public float Time { get; private set; }

    /// <summary>現在押されているキー名 (exe が毎ステップ供給。"A"/"Left"/"Space" 等) —
    /// csx が <c>world.KeysDown.Contains("Right")</c> で読む。InputAction 宣言 (project.luxel) は
    /// dogfood (GE-7) で必要になったら載せる。</summary>
    public HashSet<string> KeysDown { get; } = new(StringComparer.OrdinalIgnoreCase);

    public PlayerEntity Entity(int id) => _byId.TryGetValue(id, out PlayerEntity? e) ? e : throw new KeyNotFoundException($"エンティティが無い: {id}");

    /// <summary>名前で引く (最初の一致。無ければ null)。</summary>
    public PlayerEntity? Find(string name) => _entities.FirstOrDefault(e => e.Name == name);

    public int TileAt(int layerId, int x, int y)
        => _layers.FirstOrDefault(l => l.Id == layerId)?.Cell(x, y) ?? 0;

    /// <summary>csx ビヘイビアのホスト (ADR-0018、ローダが配線。null = スクリプトなし)。</summary>
    public PlayerBehaviours? Behaviours { get; set; }

    public string? SceneRequest { get; private set; }

    public void RequestScene(string resPath) => SceneRequest = resPath;

    /// <summary>固定 dt で 1 ステップ進める (時間 → csx ビヘイビア)。</summary>
    public void Update(float dt)
    {
        Time += dt;
        Behaviours?.Update(this, dt);
    }

    /// <summary>world 全体を Scene2D へ描く (背景 → タイル → エンティティ)。</summary>
    public void Render(Scene2D s, float viewW, float viewH, VectorFont? font = null, float fontSize = 13)
    {
        s.FillRect(TilePalette.Pack(22, 26, 34), 0, 0, viewW, viewH);
        foreach (TileLayer layer in _layers)
        {
            float cs = layer.CellSize;
            for (int y = 0; y < layer.Height; y++)
                for (int x = 0; x < layer.Width; x++)
                {
                    int t = layer.Cell(x, y);
                    if (t != 0) s.FillRect(TilePalette.ColorOf(t), x * cs, y * cs, cs, cs);
                }
        }
        foreach (PlayerEntity e in _entities)
        {
            if (!e.HasTransform) continue;
            float x = e.Pos.X - BoxSize.X / 2, y = e.Pos.Y - BoxSize.Y / 2;
            uint fill = TilePalette.Pack(238, 240, 244);
            if (e.Field("tint", "color") is { Kind: SceneValueKind.Vec4 } tint)
            {
                Vector4 c = tint.AsVec4();
                fill = Color2D.FromFloat(c.X, c.Y, c.Z, c.W);
            }
            s.FillRoundedRect(fill, x, y, BoxSize.X, BoxSize.Y, 5);
            s.StrokeRoundedRect(TilePalette.Pack(40, 44, 54), 1.4f, x, y, BoxSize.X, BoxSize.Y, 5);
            if (font is not null && e.Name.Length > 0)
            {
                (float tw, float th) = font.Measure(e.Name, fontSize);
                font.AppendText(s, e.Name, x + (BoxSize.X - tw) / 2, y + (BoxSize.Y - th) / 2 + font.Ascent(fontSize),
                    fontSize, TilePalette.Pack(30, 34, 44));
            }
        }
    }
}

public readonly record struct Player3DHit(PlayerEntity Entity, float T, Vector3 Normal);

/// <summary>
/// 3D シーンのランタイム world。v1 は GPU 非依存の deterministic backend として、
/// transform3d / mesh3d / camera3d を展開し、Scene2D へ投影ワイヤを描く。
/// </summary>
public sealed class Player3DWorld : IPlayerWorld
{
    public static readonly Vector3 BoxSize = Vector3.One;

    private readonly List<PlayerEntity> _entities;
    private readonly Dictionary<int, PlayerEntity> _byId;
    private readonly List<string> _meshAssets;
    private readonly HashSet<string> _missingAssets = new(StringComparer.Ordinal);

    public Player3DWorld(IEnumerable<PlayerEntity> entities, OrbitCamera camera)
    {
        _entities = entities.ToList();
        _byId = _entities.ToDictionary(e => e.Id);
        _meshAssets = _entities.Where(e => e.HasMesh3D).Select(e => e.MeshAsset!).Distinct(StringComparer.Ordinal).ToList();
        Camera = camera;
    }

    public IReadOnlyList<PlayerEntity> Entities => _entities;

    public IReadOnlyList<string> MeshAssets => _meshAssets;

    public IReadOnlySet<string> MissingAssets => _missingAssets;

    public OrbitCamera Camera;

    public float Time { get; private set; }

    public HashSet<string> KeysDown { get; } = new(StringComparer.OrdinalIgnoreCase);

    public PlayerBehaviours? Behaviours { get; set; }

    public string? SceneRequest { get; private set; }

    public PlayerEntity Entity(int id) => _byId.TryGetValue(id, out PlayerEntity? e) ? e : throw new KeyNotFoundException($"エンティティが無い: {id}");

    public PlayerEntity? Find(string name) => _entities.FirstOrDefault(e => e.Name == name);

    public void MarkMissingAsset(string asset) => _missingAssets.Add(asset);

    public void RequestScene(string resPath) => SceneRequest = resPath;

    public void Update(float dt)
    {
        Time += dt;
        Behaviours?.Update(this, dt);
    }

    public IEnumerable<PlayerEntity> QueryAabb(Vector3 min, Vector3 max)
    {
        foreach (PlayerEntity e in _entities)
            if (Bounds(e) is { } b && Overlaps(b.Min, b.Max, min, max))
                yield return e;
    }

    public bool RayCast(Vector3 origin, Vector3 direction, out Player3DHit hit, float maxT = 100f)
    {
        Vector3 dir = Vector3.Normalize(direction);
        float best = maxT;
        Player3DHit bestHit = default;
        bool found = false;
        foreach (PlayerEntity e in _entities)
        {
            if (Bounds(e) is not { } b || !RayAabb(origin, dir, b.Min, b.Max, out float t, out Vector3 n) || t > best) continue;
            best = t;
            bestHit = new Player3DHit(e, t, n);
            found = true;
        }
        hit = bestHit;
        return found;
    }

    public void Render(Scene2D s, float viewW, float viewH, VectorFont? font = null, float fontSize = 13)
    {
        Camera.Aspect = viewW / MathF.Max(1f, viewH);
        s.FillRect(TilePalette.Pack(18, 23, 31), 0, 0, viewW, viewH);
        DrawGrid(s, viewW, viewH);
        foreach (PlayerEntity e in _entities)
        {
            if (!e.HasTransform3D) continue;
            (Vector3 Min, Vector3 Max) b = Bounds(e)!.Value;
            DrawBox(s, viewW, viewH, b.Min, b.Max, e.HasMesh3D ? Color2D.Rgba(109, 196, 167, 210) : Color2D.Rgba(218, 226, 235, 190), e.HasMesh3D ? 1.8f : 1.2f);
            if (font is not null && Project(new Vector3((b.Min.X + b.Max.X) * 0.5f, b.Max.Y, (b.Min.Z + b.Max.Z) * 0.5f), viewW, viewH) is { } p)
            {
                string label = e.HasMesh3D ? $"{e.Name}  glb" : e.Name;
                font.AppendText(s, label, p.X + 6, p.Y - 4, fontSize, Color2D.Rgba(235, 240, 245));
            }
        }
    }

    private void DrawGrid(Scene2D s, float viewW, float viewH)
    {
        const int n = 8;
        uint major = Color2D.Rgba(108, 128, 150, 90);
        uint minor = Color2D.Rgba(108, 128, 150, 50);
        uint xColor = Color2D.Rgba(229, 83, 75, 150);
        uint zColor = Color2D.Rgba(71, 132, 238, 150);
        for (int i = -n; i <= n; i++)
        {
            float v = i;
            DrawLine(s, viewW, viewH, new Vector3(-n, 0, v), new Vector3(n, 0, v), i == 0 ? xColor : (i % 4 == 0 ? major : minor), 1);
            DrawLine(s, viewW, viewH, new Vector3(v, 0, -n), new Vector3(v, 0, n), i == 0 ? zColor : (i % 4 == 0 ? major : minor), 1);
        }
    }

    private (Vector3 Min, Vector3 Max)? Bounds(PlayerEntity e)
    {
        if (!e.HasTransform3D) return null;
        Vector3 s = Vector3.Max(Vector3.Abs(e.Scale3D), new Vector3(0.15f));
        Vector3 half = BoxSize * s * 0.5f;
        return (e.Pos3D - half, e.Pos3D + half);
    }

    private void DrawBox(Scene2D s, float viewW, float viewH, Vector3 min, Vector3 max, uint color, float width)
    {
        Span<Vector3> c = stackalloc Vector3[8];
        Corners(min, max, c);
        for (int i = 0; i < 8; i++)
        {
            if ((i & 1) == 0) DrawLine(s, viewW, viewH, c[i], c[i | 1], color, width);
            if ((i & 2) == 0) DrawLine(s, viewW, viewH, c[i], c[i | 2], color, width);
            if ((i & 4) == 0) DrawLine(s, viewW, viewH, c[i], c[i | 4], color, width);
        }
    }

    private void DrawLine(Scene2D s, float viewW, float viewH, Vector3 a, Vector3 b, uint color, float width)
    {
        if (Project(a, viewW, viewH) is not { } p0 || Project(b, viewW, viewH) is not { } p1) return;
        s.StrokeLine(color, width, p0.X, p0.Y, p1.X, p1.Y);
    }

    private Vector2? Project(Vector3 world, float viewW, float viewH)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), Camera.ViewProjection);
        if (clip.W <= 1e-5f) return null;
        float ndcX = clip.X / clip.W, ndcY = clip.Y / clip.W;
        if (float.IsNaN(ndcX) || float.IsNaN(ndcY)) return null;
        return new Vector2((ndcX + 1f) * 0.5f * viewW, (1f - ndcY) * 0.5f * viewH);
    }

    private static void Corners(Vector3 min, Vector3 max, Span<Vector3> c)
    {
        for (int i = 0; i < 8; i++)
            c[i] = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
    }

    private static bool Overlaps(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax)
        => aMin.X <= bMax.X && aMax.X >= bMin.X &&
           aMin.Y <= bMax.Y && aMax.Y >= bMin.Y &&
           aMin.Z <= bMax.Z && aMax.Z >= bMin.Z;

    private static bool RayAabb(Vector3 origin, Vector3 dir, Vector3 min, Vector3 max, out float t, out Vector3 normal)
    {
        float tMin = 0f, tMax = float.PositiveInfinity;
        normal = Vector3.Zero;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            float d = axis == 0 ? dir.X : axis == 1 ? dir.Y : dir.Z;
            float lo = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            float hi = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;
            if (MathF.Abs(d) < 1e-6f)
            {
                if (o < lo || o > hi) { t = 0; return false; }
                continue;
            }
            float inv = 1f / d;
            float t1 = (lo - o) * inv;
            float t2 = (hi - o) * inv;
            Vector3 n = axis switch
            {
                0 => new Vector3(MathF.Sign(-d), 0, 0),
                1 => new Vector3(0, MathF.Sign(-d), 0),
                _ => new Vector3(0, 0, MathF.Sign(-d)),
            };
            if (t1 > t2) { (t1, t2) = (t2, t1); n = -n; }
            if (t1 > tMin) { tMin = t1; normal = n; }
            tMax = MathF.Min(tMax, t2);
            if (tMin > tMax) { t = 0; return false; }
        }
        t = tMin;
        return t >= 0f;
    }
}
