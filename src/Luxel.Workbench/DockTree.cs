using System.Text.Json.Nodes;

namespace Luxel.Workbench;

/// <summary>ドックの分割方向 (グループの側)。</summary>
public enum DockSide { Left, Right, Top, Bottom }

/// <summary>DockTree のノード。Id はツリー内で一意 (欠番可) — UI のドロップ先指定と
/// 直列化/復元の同定に使う安定 id。</summary>
public abstract record DockNode(int Id);

/// <summary>タブグループ (葉)。Tabs はドキュメント id (シェルが id ↔ IEditorDocument を写像する
/// — ツリー自身はドキュメントを知らず純粋に直列化できる)。Active はタブ index (空なら -1)。</summary>
public sealed record DockGroup(int Id, IReadOnlyList<string> Tabs, int Active) : DockNode(Id);

/// <summary>分割領域 (内部ノード)。Horizontal = 子が左→右に並ぶ。Sizes は子の割合 (合計 1)。</summary>
public sealed record DockSplit(int Id, bool Horizontal, IReadOnlyList<DockNode> Children, IReadOnlyList<float> Sizes) : DockNode(Id);

/// <summary>窓内フローティングパネル 1 枚 (ドック木の外に浮くタブグループ + 矩形、ホスト相対 px)。</summary>
public sealed record DockFloat(DockGroup Group, float X, float Y, float W, float H);

/// <summary>
/// 領域 + タブグループの再帰木 + 窓内フローティング (ADR-0010)。**不変** — 各操作は新しいツリーを
/// 返す (テキスト/ノードスタックと同じ不変状態の流儀)。描画は ADR-0014 の DockHost。
/// 正規化規則: 空グループは消える (ルートは空グループ 1 つで残る)・子 1 つの分割は繰上げ・
/// 同方向の入れ子分割は親へ畳む (サイズは按分)・空になったフロートは消える。
/// フロートのグループも <see cref="Groups"/>/<see cref="Group"/> に含まれ、MoveTab/AddTab の
/// 対象にできる (Dock でフロートグループを指すと分割せず末尾追加)。
/// </summary>
public sealed class DockTree
{
    private DockTree(DockNode root, IReadOnlyList<DockFloat> floats, int nextId)
    { Root = root; Floats = floats; NextId = nextId; }

    public DockNode Root { get; }

    /// <summary>フローティングパネル (前面順 = 末尾が最前)。</summary>
    public IReadOnlyList<DockFloat> Floats { get; }

    /// <summary>次に割り当てる id (直列化に含め、復元後も衝突しない)。</summary>
    public int NextId { get; }

    /// <summary>グループ 1 つの初期ツリー。</summary>
    public static DockTree Single(params string[] tabs)
        => new(new DockGroup(1, tabs, tabs.Length > 0 ? 0 : -1), [], 2);

    // ---- 参照 ----

    /// <summary>全グループ (ドック木 DFS → フロート順)。</summary>
    public IEnumerable<DockGroup> Groups
        => Walk(Root).OfType<DockGroup>().Concat(Floats.Select(f => f.Group));

    /// <summary>タブが属するグループ。無ければ null。</summary>
    public DockGroup? GroupOf(string tab) => Groups.FirstOrDefault(g => g.Tabs.Contains(tab));

    /// <summary>id のグループ (フロート含む)。無ければ null。</summary>
    public DockGroup? Group(int id)
        => Walk(Root).FirstOrDefault(n => n.Id == id) as DockGroup
           ?? Floats.FirstOrDefault(f => f.Group.Id == id)?.Group;

    /// <summary>id の分割。無ければ null。</summary>
    public DockSplit? Split(int id) => Walk(Root).FirstOrDefault(n => n.Id == id) as DockSplit;

    /// <summary>id のグループを持つフロート。ドック内 (または無い) なら null。</summary>
    public DockFloat? FloatOf(int groupId) => Floats.FirstOrDefault(f => f.Group.Id == groupId);

    private static IEnumerable<DockNode> Walk(DockNode n)
    {
        yield return n;
        if (n is DockSplit s)
            foreach (DockNode c in s.Children)
                foreach (DockNode d in Walk(c))
                    yield return d;
    }

    // ---- タブ操作 ----

    /// <summary>タブをグループへ追加してアクティブにする (index &lt; 0 = 末尾)。
    /// 既に他所に居るタブは移動 (MoveTab)。グループが無ければ no-op。</summary>
    public DockTree AddTab(int groupId, string tab, int index = -1)
    {
        if (GroupOf(tab) is not null) return MoveTab(tab, groupId, index);
        if (Group(groupId) is null) return this;
        (DockNode root, IReadOnlyList<DockFloat> floats) =
            MapAll(n => n is DockGroup g && g.Id == groupId ? InsertTab(g, tab, index) : n);
        return new DockTree(root, floats, NextId);
    }

    /// <summary>タブを外す。空になったグループ/フロートは畳む (ルートは空グループで残る)。無ければ no-op。</summary>
    public DockTree RemoveTab(string tab)
    {
        if (GroupOf(tab) is null) return this;
        (DockNode root, IReadOnlyList<DockFloat> floats) =
            MapAll(n => n is DockGroup g && g.Tabs.Contains(tab) ? RemoveFromGroup(g, tab) : n);
        return Normalized(root, floats, NextId);
    }

    /// <summary>タブを属するグループ内でアクティブにする。無ければ no-op。</summary>
    public DockTree ActivateTab(string tab)
    {
        if (GroupOf(tab) is not { } g) return this;
        int i = IndexOf(g.Tabs, tab);
        if (g.Active == i) return this;
        (DockNode root, IReadOnlyList<DockFloat> floats) =
            MapAll(n => n is DockGroup gg && gg.Id == g.Id ? gg with { Active = i } : n);
        return new DockTree(root, floats, NextId);
    }

    /// <summary>タブを別グループ (または同グループ内の別位置) へ移す。移動後そのタブがアクティブ。
    /// 空になった元グループ/フロートは畳む。ドック⇄フロート間の移動も可。
    /// タブ/グループが無ければ no-op (D&amp;D の競合に安全)。</summary>
    public DockTree MoveTab(string tab, int targetGroupId, int index = -1)
    {
        if (GroupOf(tab) is not { } src || Group(targetGroupId) is null) return this;
        if (src.Id == targetGroupId)
        {
            // グループ内並べ替え
            var tabs = src.Tabs.Where(t => t != tab).ToList();
            int at = index < 0 || index > tabs.Count ? tabs.Count : index;
            tabs.Insert(at, tab);
            (DockNode r, IReadOnlyList<DockFloat> f) =
                MapAll(n => n is DockGroup g && g.Id == src.Id ? g with { Tabs = tabs, Active = at } : n);
            return new DockTree(r, f, NextId);
        }
        (DockNode root, IReadOnlyList<DockFloat> floats) = MapAll(n => n switch
        {
            DockGroup g when g.Id == src.Id => RemoveFromGroup(g, tab),
            DockGroup g when g.Id == targetGroupId => InsertTab(g, tab, index),
            _ => n,
        });
        return Normalized(root, floats, NextId);
    }

    /// <summary>タブをグループの side に**新グループで dock** する (本格ドッキングの分割操作)。
    /// タブは現在位置から外れる。親分割が同方向なら入れ子にせず隣へ挿入 (対象の取り分を半分こ)、
    /// 違う方向なら対象を新しい分割で包む。**フロートグループが対象のときは分割せず末尾追加**
    /// (フロートは 1 グループ)。唯一タブの自己分割は no-op。</summary>
    public DockTree Dock(string tab, int groupId, DockSide side)
    {
        if (Group(groupId) is not { } target) return this;
        if (FloatOf(groupId) is not null) return MoveTab(tab, groupId);   // フロートは分割しない
        if (GroupOf(tab) is { } src && src.Id == groupId && target.Tabs.Count == 1) return this;

        DockTree t = GroupOf(tab) is null ? this : RemoveTab(tab);
        if (t.Group(groupId) is null) return this;   // 対象が畳まれた (= tab が唯一タブだった) → no-op

        var add = new DockGroup(t.NextId, [tab], 0);
        int splitId = t.NextId + 1;                  // 使わなくても消費 (id は一意なら欠番可)
        bool horizontal = side is DockSide.Left or DockSide.Right;
        bool before = side is DockSide.Left or DockSide.Top;
        DockNode root = InsertBeside(t.Root, groupId, add, horizontal, before, splitId);
        return new DockTree(root, t.Floats, t.NextId + 2);
    }

    // ---- フローティング ----

    /// <summary>タブを現在位置から外して窓内フロートにする (新グループ、指定矩形)。
    /// タブが無ければ no-op。</summary>
    public DockTree Float(string tab, float x, float y, float w, float h)
    {
        if (GroupOf(tab) is null) return this;
        DockTree t = RemoveTab(tab);
        var fl = new DockFloat(new DockGroup(t.NextId, [tab], 0), x, y, MathF.Max(120, w), MathF.Max(80, h));
        return new DockTree(t.Root, [.. t.Floats, fl], t.NextId + 1);
    }

    /// <summary>フロートを移動する (グループ id 指定)。前面 (末尾) へも上げる。無ければ no-op。</summary>
    public DockTree MoveFloat(int groupId, float x, float y)
    {
        if (FloatOf(groupId) is not { } fl) return this;
        var rest = Floats.Where(f => f.Group.Id != groupId).ToList();
        rest.Add(fl with { X = x, Y = y });
        return new DockTree(Root, rest, NextId);
    }

    /// <summary>フロートをリサイズする (最小 120×80)。無ければ no-op。</summary>
    public DockTree ResizeFloat(int groupId, float w, float h)
    {
        if (FloatOf(groupId) is not { } fl) return this;
        return new DockTree(Root,
            Floats.Select(f => f.Group.Id == groupId
                ? f with { W = MathF.Max(120, w), H = MathF.Max(80, h) } : f).ToArray(),
            NextId);
    }

    /// <summary>分割の子サイズを差し替える (スプリッタドラッグ)。合計 1 に正規化。
    /// 個数不一致は ArgumentException。</summary>
    public DockTree WithSizes(int splitId, IReadOnlyList<float> sizes)
    {
        if (Split(splitId) is not { } s) return this;
        if (sizes.Count != s.Children.Count)
            throw new ArgumentException($"サイズ数 {sizes.Count} が子数 {s.Children.Count} と一致しない", nameof(sizes));
        float sum = sizes.Sum();
        if (sum <= 0) throw new ArgumentException("サイズ合計が非正", nameof(sizes));
        var norm = sizes.Select(x => x / sum).ToArray();
        return new DockTree(Map(Root, n => n is DockSplit ss && ss.Id == splitId ? ss with { Sizes = norm } : n), Floats, NextId);
    }

    // ---- 直列化 ----

    /// <summary>JSON へ直列化 (レイアウトの保存、フロート込み)。</summary>
    public string Serialize()
    {
        var o = new JsonObject { ["nextId"] = NextId, ["root"] = Write(Root) };
        if (Floats.Count > 0)
            o["floats"] = new JsonArray(Floats.Select(f => (JsonNode)new JsonObject
            {
                ["group"] = Write(f.Group),
                ["x"] = f.X, ["y"] = f.Y, ["w"] = f.W, ["h"] = f.H,
            }).ToArray());
        return o.ToJsonString();
    }

    /// <summary>JSON から復元。</summary>
    public static DockTree Deserialize(string json)
    {
        var o = (JsonObject)JsonNode.Parse(json)!;
        var floats = new List<DockFloat>();
        if (o["floats"] is JsonArray fa)
            foreach (JsonNode? fn in fa)
            {
                var fo = (JsonObject)fn!;
                floats.Add(new DockFloat((DockGroup)Read((JsonObject)fo["group"]!),
                    (float)fo["x"]!, (float)fo["y"]!, (float)fo["w"]!, (float)fo["h"]!));
            }
        return new DockTree(Read((JsonObject)o["root"]!), floats, (int)o["nextId"]!);
    }

    private static JsonNode Write(DockNode n) => n switch
    {
        DockGroup g => new JsonObject
        {
            ["id"] = g.Id,
            ["tabs"] = new JsonArray(g.Tabs.Select(t => (JsonNode)t).ToArray()),
            ["active"] = g.Active,
        },
        DockSplit s => new JsonObject
        {
            ["id"] = s.Id,
            ["h"] = s.Horizontal,
            ["sizes"] = new JsonArray(s.Sizes.Select(x => (JsonNode)x).ToArray()),
            ["children"] = new JsonArray(s.Children.Select(Write).ToArray()),
        },
        _ => throw new InvalidOperationException(),
    };

    private static DockNode Read(JsonObject o) => o.ContainsKey("tabs")
        ? new DockGroup((int)o["id"]!, ((JsonArray)o["tabs"]!).Select(t => (string)t!).ToArray(), (int)o["active"]!)
        : new DockSplit((int)o["id"]!, (bool)o["h"]!,
            ((JsonArray)o["children"]!).Select(c => Read((JsonObject)c!)).ToArray(),
            ((JsonArray)o["sizes"]!).Select(x => (float)x!).ToArray());

    // ---- 内部 ----

    private static int IndexOf(IReadOnlyList<string> tabs, string tab)
    {
        for (int i = 0; i < tabs.Count; i++) if (tabs[i] == tab) return i;
        return -1;
    }

    /// <summary>木全体へ変換を適用する (f はノード置換、子は置換後に再帰)。</summary>
    private static DockNode Map(DockNode n, Func<DockNode, DockNode> f)
    {
        n = f(n);
        if (n is DockSplit s)
            return s with { Children = s.Children.Select(c => Map(c, f)).ToArray() };
        return n;
    }

    /// <summary>ドック木 + 全フロートグループへ変換を適用する。</summary>
    private (DockNode Root, IReadOnlyList<DockFloat> Floats) MapAll(Func<DockNode, DockNode> f)
        => (Map(Root, f), Floats.Select(fl => fl with { Group = (DockGroup)f(fl.Group) }).ToArray());

    private static DockGroup RemoveFromGroup(DockGroup g, string tab)
    {
        int i = IndexOf(g.Tabs, tab);
        var tabs = g.Tabs.Where(t => t != tab).ToArray();
        int active = tabs.Length == 0 ? -1
            : i < g.Active ? g.Active - 1
            : Math.Min(g.Active, tabs.Length - 1);
        return g with { Tabs = tabs, Active = active };
    }

    private static DockGroup InsertTab(DockGroup g, string tab, int index)
    {
        var tabs = g.Tabs.ToList();
        int at = index < 0 || index > tabs.Count ? tabs.Count : index;
        tabs.Insert(at, tab);
        return g with { Tabs = tabs, Active = at };
    }

    /// <summary>正規化: 空グループ削除・子 1 つの分割繰上げ・同方向入れ子の畳み込み・空フロート削除。
    /// ドック木が全部消えたらルートを空グループで残す。</summary>
    private static DockTree Normalized(DockNode root, IReadOnlyList<DockFloat> floats, int nextId)
    {
        var live = floats.Where(f => f.Group.Tabs.Count > 0).ToArray();
        DockNode? n = Norm(root);
        if (n is null) return new DockTree(new DockGroup(nextId, [], -1), live, nextId + 1);
        return new DockTree(n, live, nextId);
    }

    private static DockNode? Norm(DockNode n)
    {
        if (n is DockGroup g) return g.Tabs.Count == 0 ? null : g;
        var s = (DockSplit)n;
        var kids = new List<DockNode>();
        var sizes = new List<float>();
        for (int i = 0; i < s.Children.Count; i++)
        {
            DockNode? c = Norm(s.Children[i]);
            if (c is null) continue;
            if (c is DockSplit cs && cs.Horizontal == s.Horizontal)
            {
                // 同方向の入れ子は親へ畳む (取り分を子の比率で按分)
                float share = SizeAt(s, i);
                for (int j = 0; j < cs.Children.Count; j++)
                {
                    kids.Add(cs.Children[j]);
                    sizes.Add(share * SizeAt(cs, j));
                }
            }
            else { kids.Add(c); sizes.Add(SizeAt(s, i)); }
        }
        if (kids.Count == 0) return null;
        if (kids.Count == 1) return kids[0];
        float sum = sizes.Sum();
        return s with { Children = kids, Sizes = sizes.Select(x => x / sum).ToArray() };
    }

    private static float SizeAt(DockSplit s, int i)
        => i < s.Sizes.Count && s.Sizes.Count == s.Children.Count ? s.Sizes[i] : 1f / s.Children.Count;

    /// <summary>target の side へ add を挿し込む。親分割が同方向なら隣へ (取り分半分こ)、
    /// 違えば target を新しい分割 (id = splitId) で包む。</summary>
    private static DockNode InsertBeside(DockNode n, int targetId, DockGroup add, bool horizontal, bool before, int splitId)
    {
        if (n.Id == targetId)   // ルートが対象 (または異方向の親から降りてきた)
        {
            DockNode[] children = before ? [add, n] : [n, add];
            return new DockSplit(splitId, horizontal, children, [0.5f, 0.5f]);
        }
        if (n is not DockSplit s) return n;
        if (s.Horizontal == horizontal)
        {
            for (int i = 0; i < s.Children.Count; i++)
            {
                if (s.Children[i].Id != targetId) continue;
                var kids = s.Children.ToList();
                var sizes = Enumerable.Range(0, s.Children.Count).Select(j => SizeAt(s, j)).ToList();
                float half = sizes[i] / 2;
                sizes[i] = half;
                int at = before ? i : i + 1;
                kids.Insert(at, add);
                sizes.Insert(at, half);
                return s with { Children = kids, Sizes = sizes };
            }
        }
        return s with { Children = s.Children.Select(c => InsertBeside(c, targetId, add, horizontal, before, splitId)).ToArray() };
    }
}
