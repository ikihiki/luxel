namespace Luxel.SceneEdit;

/// <summary>シーンの空間種別。viewport/コンパイラのパイプライン選択にだけ使い、
/// データモデル自体は空間非依存 (座標はコンポーネント側の関心事 — ADR-0015)。</summary>
public enum SceneSpace
{
    TwoD,
    ThreeD,
}

/// <summary>コンポーネントのフィールド 1 個 (名前 + 値)。不変。</summary>
public sealed record SceneField(string Name, SceneValue Value);

/// <summary>
/// エンティティに載るコンポーネント 1 個 — **不変**。<see cref="Type"/> はスキーマ登録キー
/// (例 "transform2d")。フィールドは**名前順ソートで正規化**して保持する (決定的 JSON の根拠 —
/// 構築順に依らず同じ並びで直列化される)。スキーマに無い型/フィールドもそのまま保持する
/// (未知保全)。予約語: フィールド名 "type" は JSON のコンポーネント種別キーと衝突するため禁止。
/// </summary>
public sealed class SceneComponent
{
    public string Type { get; }

    /// <summary>フィールド列 (Name の序数順でソート済み)。</summary>
    public IReadOnlyList<SceneField> Fields { get; }

    private SceneComponent(string type, IReadOnlyList<SceneField> sorted)
    {
        Type = type;
        Fields = sorted;
    }

    public static SceneComponent Of(string type, params (string Name, SceneValue Value)[] fields)
        => Of(type, fields.Select(f => new SceneField(f.Name, f.Value)));

    public static SceneComponent Of(string type, IEnumerable<SceneField> fields)
    {
        if (string.IsNullOrEmpty(type)) throw new ArgumentException("コンポーネント型が空");
        var list = fields.OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (SceneField f in list)
        {
            if (f.Name == "type") throw new ArgumentException("フィールド名 \"type\" は予約 (コンポーネント種別キー)");
            if (string.IsNullOrEmpty(f.Name)) throw new ArgumentException("フィールド名が空");
            if (!seen.Add(f.Name)) throw new ArgumentException($"フィールド名の重複: {f.Name}");
        }
        return new SceneComponent(type, list);
    }

    /// <summary>フィールドを名前で引く (無ければ null)。</summary>
    public SceneValue? Get(string name)
    {
        foreach (SceneField f in Fields) if (f.Name == name) return f.Value;
        return null;
    }

    /// <summary>フィールドを置換 (無ければ追加) した新しいコンポーネント。</summary>
    public SceneComponent With(string name, SceneValue value)
        => Of(Type, Fields.Where(f => f.Name != name).Append(new SceneField(name, value)));
}

/// <summary>
/// シーンのエンティティ 1 個 — **不変**。安定 <see cref="Id"/> を持ち (座標写像不要の根拠、
/// NodeGraph と同じ)、コンポーネントは**型ごとに 1 個** (型で引ける)。編集は With 系で
/// 新インスタンスを作る。
/// </summary>
public sealed class SceneEntity
{
    public int Id { get; }

    public string Name { get; }

    public IReadOnlyList<SceneComponent> Components { get; }

    private SceneEntity(int id, string name, IReadOnlyList<SceneComponent> components)
    {
        Id = id;
        Name = name;
        Components = components;
    }

    public static SceneEntity Of(int id, string name, params SceneComponent[] components)
        => Of(id, name, (IEnumerable<SceneComponent>)components);

    public static SceneEntity Of(int id, string name, IEnumerable<SceneComponent> components)
    {
        var list = components.ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (SceneComponent c in list)
            if (!seen.Add(c.Type)) throw new ArgumentException($"コンポーネント型の重複: {c.Type} (entity {id})");
        return new SceneEntity(id, name, list);
    }

    /// <summary>コンポーネントを型で引く (無ければ null)。</summary>
    public SceneComponent? Component(string type)
    {
        foreach (SceneComponent c in Components) if (c.Type == type) return c;
        return null;
    }

    /// <summary>コンポーネントを置換 (同型が無ければ追加) した新しいエンティティ。</summary>
    public SceneEntity WithComponent(SceneComponent component)
        => new(Id, Name, Components.Where(c => c.Type != component.Type).Append(component).ToList());

    /// <summary>コンポーネントを外した新しいエンティティ (無ければそのまま)。</summary>
    public SceneEntity WithoutComponent(string type)
        => new(Id, Name, Components.Where(c => c.Type != type).ToList());
}

/// <summary>
/// タイルレイヤ 1 枚 — **不変**。<see cref="TileSet"/> は res:// のアセット参照。
/// <see cref="Cells"/> は行優先 Width×Height (0 = 空タイル、1 始まりのタイル番号)。
/// 描き込みの変更モデル (PaintTiles) は GE-1 S2 で足す。
/// </summary>
public sealed class TileLayer
{
    public int Id { get; }

    public string Name { get; }

    /// <summary>タイルセット定義への res:// 参照。</summary>
    public string TileSet { get; }

    /// <summary>セル 1 個の world 辺長。</summary>
    public float CellSize { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>行優先 Width×Height。0 = 空。</summary>
    public IReadOnlyList<int> Cells { get; }

    private TileLayer(int id, string name, string tileSet, float cellSize, int width, int height, IReadOnlyList<int> cells)
    {
        Id = id;
        Name = name;
        TileSet = tileSet;
        CellSize = cellSize;
        Width = width;
        Height = height;
        Cells = cells;
    }

    public static TileLayer Of(int id, string name, string tileSet, float cellSize, int width, int height, IReadOnlyList<int>? cells = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException($"タイルレイヤ寸法が不正: {width}x{height}");
        cells ??= new int[width * height];
        if (cells.Count != width * height)
            throw new ArgumentException($"cells 数 {cells.Count} が {width}x{height} に一致しない");
        return new TileLayer(id, name, tileSet, cellSize, width, height, cells);
    }

    public int Cell(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) throw new ArgumentOutOfRangeException($"({x},{y})");
        return Cells[y * Width + x];
    }
}
