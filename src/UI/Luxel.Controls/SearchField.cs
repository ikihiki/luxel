using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// 検索フィールド — **CompositeControl の見本**。
/// TextField + クリアボタン + 絞り込み候補リストを Build() で宣言的に組み合わせる。
/// 状態 3 層の実演:
/// - 値状態: query signal (呼び出し側と共有、TextField と双方向) — 細粒度反映
/// - **構造状態: 絞り込んだ候補リスト** — Build 本体で query を読むだけで、
///   **TrackedBuild (既定) が変化を捕捉して自動 Rebuild** する (明示購読は不要)
/// - 外部 props: <see cref="MaxSuggestions"/> ([UiParam] — knobs/DevTools から編集可)
/// 候補行クリックで確定 (signal へ書く → TextField が追従し、候補は閉じる)。
/// </summary>
[UiComponent]
public sealed partial class SearchField : CompositeControl
{
    /// <summary>検索文字列の signal (TextField と双方向)。</summary>
    [UiParam] private readonly Bindable<Signal<string>> _value = new();
    /// <summary>絞り込み候補の全リスト。</summary>
    [UiParam] private readonly Bindable<IReadOnlyList<string>> _candidates = new([]);
    /// <summary>候補リストの最大表示行数 (既定 5)。</summary>
    [UiParam] private readonly Bindable<int> _maxSuggestions = 5;

    private TextField? _field;            // 状態を保つ子はフィールド保持 (Rebuild を跨いで生存) — 初回 Build で構築
    private Button? _clear;
    private string[] _matches = [];       // 構造状態 — 変わったら Rebuild

    public override string? DebugDetail => $"\"{Value.Get().Value}\" ({_matches.Length} 候補)";

    private string[] Filter(string q)
        => q.Length == 0
            ? []
            : Candidates.Get().Where(c => c.Contains(q, StringComparison.OrdinalIgnoreCase)
                                          && !string.Equals(c, q, StringComparison.OrdinalIgnoreCase))
                .Take(Math.Max(1, MaxSuggestions.Get())).ToArray();

    protected override Widget Build()
    {
        Signal<string> query = Value.Get();
        // 状態を保つ子は初回 Build で一度だけ構築 (旧 ctor から遅延 — Rebuild を跨いで生存)
        _field ??= Kit.TextField(query, placeholder: "Search...", width: 220);
        _clear ??= Kit.Button(_ => query.Value = "", "×", variant: Variant.Ghost);

        // Build 本体での signal 読み取り = TrackedBuild の依存 — query が変わると自動 Rebuild される
        // (明示的な購読/Rebuild 呼び出しは不要)。MaxSuggestions が knobs で束縛されていれば同様に追跡。
        _matches = Filter(query.Value);
        var rows = new Widget[1 + _matches.Length];
        rows[0] = Kit.HStack(spacing: 4)[_field, _clear];
        for (int i = 0; i < _matches.Length; i++)
        {
            string m = _matches[i];
            rows[1 + i] = Kit.MenuRow(m, _ => query.Value = m);   // 確定 = signal へ (TextField が追従)
        }
        return Kit.VStack(spacing: 2)[rows];
    }
}
