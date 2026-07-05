namespace Luxel.UI;

/// <summary>
/// ソースジェネレーター (Luxel.UI.Generators) にファクトリ関数を生成させる widget。
/// クラスは <c>partial</c> であること (DevTools 用コードも同じクラスへ焼き込まれる)。
/// パラメータ無しコンストラクタが必要。
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UiComponentAttribute : Attribute
{
    /// <summary>ファクトリを生成する static partial class 名 (同一 namespace)。
    /// 省略時はアセンブリの <see cref="UiFactoryDefaultsAttribute"/>、それも無ければ "Factories"。</summary>
    public string? Factory { get; set; }

    /// <summary>生成する関数名。省略時はクラス名。</summary>
    public string? Name { get; set; }
}

/// <summary>
/// ファクトリ引数 + DevTools 編集対象にする <see cref="Bindable{T}"/>/<see cref="BindableString"/>。
/// 標準形は **private フィールド** <c>[UiParam] private readonly Bindable&lt;T&gt; _x = ...;</c> —
/// ソースジェネレーターが公開面のプロパティ <c>public Bindable&lt;T&gt; X { get => _x; internal init => _x = value; }</c>
/// を partial に生成する (外部アセンブリからは get only、構築 = 同一アセンブリのファクトリ/テストは internal init)。
/// 生成コード (SetProp/DebugProps/When/ファクトリ) はプロパティ経由で SetBase/SetState を呼ぶ。
/// 継承先の生成コードにも含まれる (Widget 基底の Width/Margin 等が「共通引数」になる仕組み)。
/// (旧形式の public readonly フィールド / 手書きプロパティも引き続き収集される。)
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class UiParamAttribute : Attribute
{
    /// <summary>状態レイヤ (生成される <c>When(state, ...)</c> と <c>{Class}Props</c> 定数) に出すか。
    /// **effect 内で毎回解決される表示系プロパティ (色/opacity/scale 等) のみ true にする** —
    /// レイアウトは単一パスで 1 回しか読まないため、レイアウト系を状態で変えても反映されない。</summary>
    public bool Stateable { get; set; }
}

/// <summary>アセンブリ既定のファクトリ生成先クラス名 (<c>[assembly: UiFactoryDefaults("Kit")]</c>)。</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class UiFactoryDefaultsAttribute(string factoryClass) : Attribute
{
    public string FactoryClass { get; } = factoryClass;
}

/// <summary>ファクトリ生成に使うコンストラクタを明示する (複数 ctor がある場合)。
/// 無指定なら最も引数の多い public ctor が選ばれる。</summary>
[AttributeUsage(AttributeTargets.Constructor)]
public sealed class UiCtorAttribute : Attribute { }
