namespace Luxel.UI;

/// <summary>
/// UI ツリーのデバッグ snapshot ノード (DevTools のツリー可視化用)。
/// 矩形は <see cref="Widget.WorldPos"/>/<see cref="Widget.Size"/> 由来 (canvas 座標)。
/// <see cref="Props"/> は DevTools で編集可能な属性 (color / opacity / text 等) の spec。
/// </summary>
public sealed record DebugNode(
    string Type, float X, float Y, float W, float H, float Z, string? Detail, DebugNode[] Children,
    DebugProp[]? Props = null);

/// <summary>Widget の 1 プロパティ (DevTools UI で可視化 + 編集される)。</summary>
/// <param name="Name">プロパティ名 (path 用)。</param>
/// <param name="Type">"float" | "int" | "bool" | "string" | "color" (rgba hex) | "vec2" 等の型ヒント。フロントの入力 UI を切り替える。</param>
/// <param name="Value">現在値 (JSON 文字列 or scalar 生値、フロントで型ヒントに従って解釈)。</param>
public readonly record struct DebugProp(string Name, string Type, string Value);

/// <summary>複数の UI (HUD / cockpit / world-space monitor 等) を並べて emit するためのラッパ。</summary>
public sealed record DebugTreeSet(DebugTreeEntry[] Trees);

/// <summary>1 UI (UiHost 単位) の tree エントリ。<paramref name="Name"/> は表示用、<paramref name="Placement"/> は "Screen" / "WorldSpace" 等。</summary>
public sealed record DebugTreeEntry(string Name, string Placement, DebugNode? Root, int Width, int Height);
