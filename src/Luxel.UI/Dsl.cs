namespace Luxel.UI;

/// <summary>添付プロパティ DSL のルート受け手。<c>using static Luxel.UI.Decl;</c> で <c>P</c> を導入する。</summary>
public readonly struct PRoot { }

public static class Decl
{
    /// <summary>添付プロパティのエントリ (例: <c>P.Grid.Column(1)</c>)。構築は P 無しのベアファクトリ。</summary>
    public static PRoot P => default;
}

// 各コントロールの添付プロパティ (P.Grid.* 等) は、コントロールを定義するアセンブリが
// C# 14 拡張メンバーで PRoot に生やす (Luxel.Controls の GridDsl.cs 参照)。
