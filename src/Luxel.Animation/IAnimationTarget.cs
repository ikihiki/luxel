namespace Luxel.Animation;

/// <summary>
/// アニメーション値の書き込み先抽象。Track / Clip 側はこのインタフェース越しに値を渡し、
/// 具体的なターゲット (Signal / RetainedCanvas / ECS / GPU buffer) はドメイン別アダプタが実装する。
/// 設計プラン §7 (#3) 「IR は共通、ターゲットは 3 アダプタ + 上から拡張」に基づく。
/// </summary>
public interface IAnimationTarget
{
    /// <summary>指定 path に値を書き込む。アダプタは path をパースして該当 entity / signal / node を更新。</summary>
    void Apply(string path, object value);
}
