using Friflo.Engine.ECS;

namespace Luxel.Ecs;

/// <summary>
/// 共通の Tag 定義 (Friflo の ITag インターフェース)。
/// Tag は値を持たない marker (storage オーバーヘッドが component より小さい)、
/// query フィルタで <c>.AllTags(Tags.Get&lt;Enabled&gt;())</c> のように利用。
/// </summary>
public struct Enabled : ITag { }
public struct Selected : ITag { }
public struct Dirty : ITag { }
