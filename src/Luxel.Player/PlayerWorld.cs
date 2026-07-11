using System.Numerics;
using Luxel.SceneEdit;
using Luxel.TwoD;
using Luxel.Typography;

namespace Luxel.Player;

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
public sealed class Player2DWorld
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

    public PlayerEntity Entity(int id) => _byId.TryGetValue(id, out PlayerEntity? e) ? e : throw new KeyNotFoundException($"エンティティが無い: {id}");

    /// <summary>名前で引く (最初の一致。無ければ null)。</summary>
    public PlayerEntity? Find(string name) => _entities.FirstOrDefault(e => e.Name == name);

    public int TileAt(int layerId, int x, int y)
        => _layers.FirstOrDefault(l => l.Id == layerId)?.Cell(x, y) ?? 0;

    /// <summary>csx ビヘイビアのホスト (ADR-0018、ローダが配線。null = スクリプトなし)。</summary>
    public PlayerBehaviours? Behaviours { get; set; }

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
