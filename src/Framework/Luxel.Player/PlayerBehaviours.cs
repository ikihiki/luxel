using System.Numerics;
using System.Reflection;
using System.Text;
using Luxel.Resources;
using Luxel.SceneEdit;
using Luxel.Scripting;

namespace Luxel.Player;

/// <summary>csx ビヘイビアの globals (ADR-0018) — スクリプトは <see cref="Update"/> に
/// 毎ステップ処理を代入する: <c>Update = (self, world, dt) =&gt; { self.Pos.X += 60f * dt; };</c>。
/// **スクリプト自身は状態を持たない** (同じスクリプトを複数エンティティが共有する) —
/// エンティティ状態はコンポーネント (<see cref="PlayerEntity.Field"/>/<see cref="PlayerEntity.SetField"/>) に置く。
/// 空間非依存の共通部で、2D/3D の world へ同じ delegate で接続する。</summary>
public sealed class BehaviourGlobals
{
    /// <summary>毎ステップ呼ばれる更新 (self = このスクリプトを持つエンティティ, world, 固定 dt)。</summary>
    public Action<PlayerEntity, IPlayerWorld, float>? Update;
}

/// <summary>
/// behaviour コンポーネント (script = res:// の .csx) のホスト (ADR-0018)。1 スクリプト 1 コンパイルで
/// 複数エンティティが共有する。**失敗契約は ScriptSystem と同じ**: コンパイル失敗 = 旧 Update を維持して
/// 診断公開、実行時例外 = そのスクリプトを無効化して診断公開 (毎フレームのスパムを避ける)、
/// <see cref="Reload"/> で復帰。スクリプトは固定 dt の Update のみ (wall-clock 禁止 — 決定性)。
/// </summary>
public sealed class PlayerBehaviours
{
    private sealed class Slot
    {
        public Action<PlayerEntity, IPlayerWorld, float>? Update;
        public List<string> Diagnostics = [];
    }

    private readonly IVirtualFileSystem _fs;
    private readonly ScriptHost _host;
    private readonly Dictionary<string, Slot> _scripts = new(StringComparer.Ordinal);   // res:// path → slot

    public PlayerBehaviours(IVirtualFileSystem fs)
    {
        _fs = fs;
        _host = new ScriptHost(
            references:
            [
                typeof(PlayerEntity).Assembly,           // Luxel.Player
                typeof(SceneValue).Assembly,             // Luxel.SceneEdit
                typeof(Vector2).Assembly,                // System.Numerics
            ],
            usings: ["System", "System.Numerics", "Luxel.Player", "Luxel.SceneEdit"],
            globalsType: typeof(BehaviourGlobals));
    }

    /// <summary>全スクリプトの診断 (パス付き)。空 = 健全。</summary>
    public IReadOnlyList<string> Diagnostics
        => _scripts.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                   .SelectMany(kv => kv.Value.Diagnostics.Select(d => $"{kv.Key}: {d}")).ToList();

    /// <summary>world の behaviour コンポーネントが参照する全スクリプトを読み込む。</summary>
    public void LoadAll(IPlayerWorld world)
    {
        foreach (PlayerEntity e in world.Entities)
            if (ScriptPathOf(e) is { Length: > 0 } path && !_scripts.ContainsKey(path))
                Reload(path);
    }

    /// <summary>スクリプトを (再) コンパイルする。失敗 = 旧 Update 維持 + 診断。</summary>
    public void Reload(string resPath)
    {
        if (!_scripts.TryGetValue(resPath, out Slot? slot)) _scripts[resPath] = slot = new Slot();
        slot.Diagnostics = [];
        string file = ResPath.Resolve(resPath);
        if (!_fs.Exists(file))
        {
            slot.Diagnostics.Add($"スクリプトが無い: {file}");
            return;
        }
        string code = Encoding.UTF8.GetString(_fs.ReadAsync(file, CancellationToken.None).GetAwaiter().GetResult());
        var globals = new BehaviourGlobals();
        ScriptResult result = _host.Run(code, globals);
        if (!result.Success || globals.Update is null)
        {
            foreach (ScriptDiagnostic d in result.Diagnostics.Where(d => d.IsError))
                slot.Diagnostics.Add($"({d.Line},{d.Column}) {d.Message}");
            if (result.Exception is not null) slot.Diagnostics.Add(result.Exception.Message);
            if (result.Success && globals.Update is null) slot.Diagnostics.Add("スクリプトが Update を設定していない");
            return;   // 旧 Update を維持 (あれば動き続ける)
        }
        slot.Update = globals.Update;
    }

    /// <summary>behaviour を持つ全エンティティの Update を呼ぶ (固定 dt)。実行時例外は
    /// そのスクリプトを無効化して診断に積む (リロードで復帰)。</summary>
    public void Update(IPlayerWorld world, float dt)
    {
        foreach (PlayerEntity e in world.Entities)
        {
            if (ScriptPathOf(e) is not { Length: > 0 } path || !_scripts.TryGetValue(path, out Slot? slot)) continue;
            if (slot.Update is not { } update) continue;
            try
            {
                update(e, world, dt);
            }
            catch (Exception ex)
            {
                slot.Update = null;
                slot.Diagnostics.Add($"実行時例外 (entity {e.Id} で無効化、リロードで復帰): {(ex is TargetInvocationException t ? t.InnerException?.Message ?? t.Message : ex.Message)}");
            }
        }
    }

    private static string? ScriptPathOf(PlayerEntity e) =>
        e.Has("behaviour") && e.Field("behaviour", "script") is { Kind: SceneValueKind.Text } v ? v.AsText() : null;
}
