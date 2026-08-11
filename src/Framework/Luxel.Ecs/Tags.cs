using Friflo.Engine.ECS;

namespace Luxel.Ecs;

/// <summary>
/// 共通の Tag 定義 (Friflo の ITag インターフェース)。
/// Tag は値を持たない marker (storage オーバーヘッドが component より小さい)、
/// query フィルタで <c>.AllTags(Tags.Get&lt;Enabled&gt;())</c> のように利用。
/// </summary>
public struct Enabled : ITag { }
/// <summary>選択中を示す Tag。</summary>
public struct Selected : ITag { }
/// <summary>再計算が必要なことを示す Tag。</summary>
public struct Dirty : ITag { }
