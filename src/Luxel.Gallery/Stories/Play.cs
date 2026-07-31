using Luxel.UI;
﻿namespace Luxel.Gallery;

/// <summary>play 内の Expect 失敗 / ドライバ操作の失敗。ランナーがテスト失敗として報告する。</summary>
public sealed class PlayError(string message) : Exception(message);

/// <summary>
/// play (ストーリーに同居する対話テスト — 本家 Storybook の play 関数相当) の操作ドライバ。
/// 入力注入 → フレーム前進 → golden チェックポイント (<see cref="Snap"/>) を提供する。
/// <list type="bullet">
/// <item>**E2E ランナー**: step は固定 dt で同期完了 — 実行全体が決定的</item>
/// <item>**Gallery 内再生**: step は実フレーム待ちの Task — 同じ play がその場で目に見えて動く
///   (Interactions パネル)。継続はギャラリースレッドで同期実行される</item>
/// </list>
/// <para>**決定性の規約**: 時間はすべて step 経由で進む。play 内で <c>Task.Delay</c> や
/// wall-clock を使わないこと (ランナーの結果が実行環境依存になる)。</para>
/// </summary>
public sealed class PlayDriver
{
    private readonly UiHost _host;
    private readonly Func<int, Task> _step;   // n フレーム進める (ランナー: 固定 dt 同期 / live: 実フレーム待ち)
    private readonly Action<string> _snap;    // golden チェックポイント (名前 "" = 既定)
    private readonly Action<string>? _onOp;   // 操作の記録 (Interactions パネル用、null = 記録なし)

    public PlayDriver(UiHost host, Func<int, Task> step, Action<string> snap, Action<string>? onOp = null)
    {
        _host = host;
        _step = step;
        _snap = snap;
        _onOp = onOp;
    }

    /// <summary>ストーリーの UiHost (低レベル操作/検査用)。通常は下の操作 API で足りる。</summary>
    public UiHost Host => _host;

    private void Note(string label) => _onOp?.Invoke(label);

    /// <summary>n フレーム進める (トランジション/アニメの静定待ちに)。</summary>
    public Task Step(int frames = 1) => _step(frames);

    /// <summary>クリック (押下 → 1 フレーム → 解放 → 2 フレーム)。座標はストーリーのクライアント座標。
    /// <paramref name="mods"/>/<paramref name="button"/> で修飾キー・ボタンを指定できる (Alt+Click 等)。</summary>
    public async Task Click(float x, float y, KeyModifiers mods = KeyModifiers.None, PointerButton button = PointerButton.Left)
    {
        Note($"Click ({x:0}, {y:0}){ModLabel(mods)}");
        _host.PointerDown(x, y, button, mods);
        await _step(1);
        _host.PointerUp(x, y, button, mods);
        await _step(2);
    }

    /// <summary>widget の中心をクリックする (実体化済みであること — WorldPos/Size を使う)。</summary>
    public Task Click(Widget w, KeyModifiers mods = KeyModifiers.None, PointerButton button = PointerButton.Left) => Click(
        w.WorldPos.X + w.Size.Width / 2, w.WorldPos.Y + w.Size.Height / 2, mods, button);

    /// <summary>右クリック (コンテキストメニュー要求)。</summary>
    public async Task RightClick(float x, float y, KeyModifiers mods = KeyModifiers.None)
    {
        Note($"RightClick ({x:0}, {y:0}){ModLabel(mods)}");
        _host.ContextClick(x, y, mods);
        await _step(2);
    }

    /// <summary>ドラッグ (押下 → 補間移動 × moves → 解放)。<paramref name="button"/>/<paramref name="mods"/> で
    /// 中ボタン pan や Alt ドラッグを指定できる。</summary>
    public async Task Drag(float fromX, float fromY, float toX, float toY, int moves = 8,
        PointerButton button = PointerButton.Left, KeyModifiers mods = KeyModifiers.None)
    {
        Note($"Drag ({fromX:0}, {fromY:0}) → ({toX:0}, {toY:0}){ModLabel(mods)}");
        _host.PointerDown(fromX, fromY, button, mods);
        await _step(1);
        for (int i = 1; i <= moves; i++)
        {
            float t = (float)i / moves;
            _host.PointerMove(fromX + (toX - fromX) * t, fromY + (toY - fromY) * t, mods);
            await _step(1);
        }
        _host.PointerUp(toX, toY, button, mods);
        await _step(2);
    }

    private static string ModLabel(KeyModifiers m) => m == KeyModifiers.None ? "" :
        $" [{(m.HasFlag(KeyModifiers.Ctrl) ? "Ctrl+" : "")}{(m.HasFlag(KeyModifiers.Shift) ? "Shift+" : "")}{(m.HasFlag(KeyModifiers.Alt) ? "Alt+" : "")}{(m.HasFlag(KeyModifiers.Meta) ? "Meta+" : "")}]";

    /// <summary>ホイール。</summary>
    public async Task Wheel(float x, float y, float delta)
    {
        Note($"Wheel {delta:+0.##;-0.##} @ ({x:0}, {y:0})");
        _host.Wheel(x, y, delta);
        await _step(2);
    }

    /// <summary>キー入力 (フォーカス先へ)。</summary>
    public async Task Key(Key key, bool shift = false, bool ctrl = false, bool alt = false)
    {
        Note($"Key {(ctrl ? "Ctrl+" : "")}{(shift ? "Shift+" : "")}{(alt ? "Alt+" : "")}{key}");
        _host.KeyDown(key, shift, ctrl, alt);
        await _step(1);
    }

    /// <summary>文字入力 (フォーカス先へ 1 文字ずつ)。</summary>
    public async Task Type(string text)
    {
        Note($"Type \"{text}\"");
        foreach (char c in text)
        {
            _host.Char(c.ToString());
            await _step(1);
        }
        await _step(1);
    }

    /// <summary>golden チェックポイント。ランナーでは撮影 + 比較 (--update で更新)、Gallery 内再生では記録のみ。
    /// ファイル名: <c>{Story}[.{Play 名}][.{name}].{backend}.png</c> —
    /// 無名 play の最初の無名 Snap は従来の snap と同じファイル名になる (移行互換)。</summary>
    public Task Snap(string name = "")
    {
        _snap(name);
        return Task.CompletedTask;
    }

    /// <summary>アサーション。false なら play 失敗 (<see cref="PlayError"/>)。</summary>
    public Task Expect(bool condition, string label = "")
    {
        Note($"Expect {(condition ? "✓" : "✗")}{(label.Length > 0 ? $" {label}" : "")}");
        if (!condition) throw new PlayError($"Expect 失敗{(label.Length > 0 ? $": {label}" : "")}");
        return Task.CompletedTask;
    }

    /// <summary>条件版 Expect — 評価前に 1 フレーム進める (直前の入力の反映待ち)。</summary>
    public async Task Expect(Func<bool> condition, string label = "")
    {
        await _step(1);
        await Expect(condition(), label);
    }
}

/// <summary>ストーリーに登録された play (名前 + 本体)。名前 "" = 既定 (1 つだけのときの通例)。</summary>
public sealed record StoryPlay(string Name, Func<PlayDriver, Task> Body);
