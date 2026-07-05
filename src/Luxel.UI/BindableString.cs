using System.Runtime.CompilerServices;
using System.Text;

namespace Luxel.UI;

/// <summary>
/// 文字列専用の <see cref="Bindable{T}"/>。値直接 / <see cref="Signal{T}"/> / <c>Func&lt;string&gt;</c> / <c>$"..."</c> 補完文字列を
/// 1 引数で受ける統合型。<c>[InterpolatedStringHandler]</c> 属性付きで C# コンパイラが <c>$"..."</c> を直接 handler として渡せる。
///
/// <code>
/// Text("Hello")                       // 値直接
/// Text(mySignal)                      // Signal&lt;string&gt;
/// Text(Bind.From(() => calc()))       // Func&lt;string&gt; wrap
/// Text($"Count: {count}")             // 補完文字列 (hole の Signal は reactive)
/// </code>
///
/// <para><see cref="Bindable{T}"/> と同じく <b>[UiParam] フィールドとして使える</b>:
/// <c>readonly BindableString Foo = new();</c> で宣言し、書き込みは <see cref="SetBase"/>
/// (基底値の差し替え — 状態レイヤ / override は維持)。
/// DevTools override (<see cref="SetOverride"/>) と状態レイヤ (<see cref="SetState"/>) を持ち、
/// <see cref="Get"/> は「override &gt; アクティブ状態レイヤ &gt; 基底」で解決する。</para>
/// </summary>
[InterpolatedStringHandler]
public sealed class BindableString
{
    private string? _literalValue;
    private Signal<string>? _signal;
    private Func<string>? _getter;
    private List<Func<string>>? _parts;
    private bool _set;
    private Signal<string>? _override;   // DevTools 書換用、null=素通し
    private StateSlots? _states;         // 状態別レイヤ

    private sealed class StateSlots
    {
        public Widget Owner = null!;
        public readonly BindableString?[] Values = new BindableString?[7];   // WidgetState のメンバー数
        public byte Mask;
    }

    private static readonly WidgetState[] StatePriority =
        [WidgetState.Disabled, WidgetState.Pressed, WidgetState.Hover, WidgetState.Focused, WidgetState.Checked, WidgetState.Selected];

    /// <summary>未設定 (factory default 相当)。フィールド宣言 <c>= new()</c> で使う。</summary>
    public BindableString() { }

    /// <summary>値直接版。</summary>
    public BindableString(string value)
    {
        _literalValue = value;
        _set = true;
    }

    /// <summary>Signal 版。</summary>
    public BindableString(Signal<string> signal)
    {
        _signal = signal;
        _set = true;
    }

    /// <summary>Func 版。</summary>
    public BindableString(Func<string> getter)
    {
        _getter = getter;
        _set = true;
    }

    /// <summary>Interpolated string handler 用 ctor (C# が <c>$"..."</c> 構築時に自動で呼ぶ)。</summary>
    public BindableString(int literalLength, int formattedCount)
    {
        _parts = new List<Func<string>>(formattedCount * 2 + 1);
        _set = true;
    }

    public void AppendLiteral(string s) { string c = s; _parts!.Add(() => c); }
    public void AppendFormatted<T>(T value) { T c = value; _parts!.Add(() => c?.ToString() ?? ""); }
    /// <summary>Signal&lt;T&gt; を hole で受けたとき、reactive に値を読む。</summary>
    public void AppendFormatted<T>(Signal<T> signal) { _parts!.Add(() => signal.Value?.ToString() ?? ""); }

    /// <summary>「未設定」(factory default) かどうか。状態レイヤ/override は含まない。</summary>
    public bool HasValue => _set;

    /// <summary>現在の文字列値を取得 (毎回再評価)。override &gt; アクティブ状態レイヤ &gt; 基底。
    /// 状態判定は owner の signal を読むため、Effect 内なら依存追跡される。</summary>
    public string Get()
    {
        if (_override is not null) return _override.Value;
        if (_states is { Mask: not 0 } ss)
        {
            foreach (WidgetState s in StatePriority)
            {
                if ((ss.Mask & (1 << (int)s)) == 0) continue;
                if (ss.Owner.IsStateActive(s)) return ss.Values[(int)s]!.GetBase();
            }
        }
        return GetBase();
    }

    private string GetBase()
    {
        if (_parts is { } parts)
        {
            var sb = new StringBuilder();
            foreach (var p in parts) sb.Append(p());
            return sb.ToString();
        }
        if (_getter is not null) return _getter();
        if (_signal is not null) return _signal.Value;
        return _literalValue ?? "";
    }

    /// <summary>未設定なら fallback (override / アクティブ状態レイヤも解決に含む)。</summary>
    public string Or(string fallback)
        => _set || _override is not null || _states is { Mask: not 0 } ? Get() : fallback;

    /// <summary>Func&lt;string&gt; 形式で取得 (受け渡し用)。override/状態レイヤ込みで評価される。</summary>
    public Func<string> ToGetter() => Get;

    /// <summary>基底値を書き換える (状態レイヤ / DevTools override は維持)。ソース生成ファクトリ/SetProp が使う。</summary>
    public void SetBase(BindableString v)
    {
        _literalValue = v._literalValue;
        _signal = v._signal;
        _getter = v._getter;
        _parts = v._parts;
        _set = v._set;
    }

    /// <summary>状態レイヤを積む (Tailwind の <c>hover:</c> 相当)。owner の <see cref="Widget.IsStateActive"/> で判定。</summary>
    public void SetState(WidgetState state, BindableString value, Widget owner)
    {
        _states ??= new StateSlots();
        _states.Owner = owner;
        _states.Values[(int)state] = value;
        _states.Mask |= (byte)(1 << (int)state);
    }

    /// <summary>DevTools 用書き戻し。</summary>
    public void SetOverride(string value)
    {
        if (_override is null) _override = new Signal<string>(value);
        else _override.Value = value;
    }

    /// <summary>override を除去して元の挙動に戻す。</summary>
    public void ClearOverride() { _override = null; }

    public bool HasOverride => _override is not null;

    public static implicit operator BindableString(string value) => new(value);
    public static implicit operator BindableString(Signal<string> signal) => new(signal);
    public static implicit operator BindableString(Func<string> getter) => new(getter);
}
