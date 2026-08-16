using System.Numerics;
using Luxel.Audio;
using Luxel.Input;
using Luxel.Input.XInput;
using Luxel.Platform;
using Luxel.Platform.Windows;
using Luxel.Framework.UI;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using Luxel.UI.Tailwind;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>実ウィンドウ専用ストーリー ([Story(RealWindowOnly = true)] — snap 回帰は SKIP)。
/// 音声再生・実デバイス入力・第 2 ウィンドウなど、offscreen の決定的描画にならない機能のデモを置く。</summary>
[StoryMeta("RealWindow")]
public static class RealWindowStories
{
    private static AudioMixer? _mixer;   // プロセスで 1 個 (XAudio2 マスタリングボイス)

    [Story(RealWindowOnly = true)]
    public static StoryResult AudioTone(StoryContext ctx)
    {
        void Play(float freq, string name)
        {
            if (_mixer is null)
            {
                var backend = new XAudio2Backend();
                backend.Initialize();
                _mixer = new AudioMixer(backend);
            }
            _mixer.Tick();   // 完了 voice を pool へ回収 (デモはフレームループを持たないのでここで)
            _mixer.PlayOneShot(Tone(freq));
            ctx.Log($"play {name} ({freq:0.##}Hz)");
        }
        return Frame(VStack(10)[
            Label("XAudio2 のワンショット再生 — 実窓専用 (snap では SKIP)"),
            Muted("AudioClip は 0.4 秒のサイン波を CPU で合成 (Pcm16Mono44k)"),
            HStack(8)[
                Button(_ => Play(440f, "A4"), "A4 (440Hz)"),
                Button(_ => Play(523.25f, "C5"), "C5"),
                Button(_ => Play(659.26f, "E5"), "E5")]]);
    }

    [Story(RealWindowOnly = true)]
    public static StoryResult Gamepad(StoryContext ctx)
    {
        // 物理層: XInput を毎フレーム Poll して差分イベントを bus へ
        var source = new XInputSource();
        var bus = new InputBus();

        // アクション層: 意味名 (Move/Look/Jump/Fire) にバインドし、ゲーム側はアクションだけを見る
        var gameplay = new InputContext("Gameplay");
        Axis2DAction move = gameplay.Add(new Axis2DAction("Move"));
        move.AxisPairs.Add((AxisCode.GamepadLeftStickX, AxisCode.GamepadLeftStickY));
        Axis2DAction look = gameplay.Add(new Axis2DAction("Look"));
        look.AxisPairs.Add((AxisCode.GamepadRightStickX, AxisCode.GamepadRightStickY));
        ButtonAction jump = gameplay.Add(new ButtonAction("Jump", KeyCode.GamepadA));
        Axis1DAction fire = gameplay.Add(new Axis1DAction("Fire"));
        fire.Axes.Add(AxisCode.GamepadRightTrigger);
        ButtonAction[] face =
        [
            jump,
            gameplay.Add(new ButtonAction("B", KeyCode.GamepadB)),
            gameplay.Add(new ButtonAction("X", KeyCode.GamepadX)),
            gameplay.Add(new ButtonAction("Y", KeyCode.GamepadY)),
        ];
        var stack = new InputStack();
        stack.Push(gameplay);
        jump.Triggered += () => ctx.Log("Jump (GamepadA)");

        return Frame(VStack(8)[
            Label("XInput → InputBus → InputStack → アクション層 (Move/Look/Jump/Fire)"),
            Muted("Xbox 互換コントローラを接続して操作 — 未接続時は Poll が no-op"),
            Canvas2D(384, 260, animate: (s, _) =>
            {
                source.Poll(bus);      // 物理: 差分イベントを enqueue
                stack.Update(bus);     // アクション: held/axes を進めて各 Action を解決 (bus はここで消費)
                DrawPad(s, move.Value.Value, look.Value.Value, face, fire.Value.Value);
            })]);
    }

    /// <summary>スティック 2 つ (枠 + 現在位置ドット) と ABXY・右トリガのバーを描く。</summary>
    private static void DrawPad(Scene2D s, Vector2 move, Vector2 look, ButtonAction[] face, float fire)
    {
        void Stick(float cx, float cy, Vector2 v, uint color)
        {
            s.StrokeRoundedRect(Tw.Slate500, 2, cx - 56, cy - 56, 112, 112, 56);
            s.FillCircle(color, cx + v.X * 48, cy - v.Y * 48, 12);   // Y は上が正 → 画面座標へ反転
        }
        Stick(90, 110, move, Tw.Blue500);
        Stick(230, 110, look, Tw.Green500);

        // ABXY (Xbox 配置: A 下 / B 右 / X 左 / Y 上)。押下中は塗り
        (float dx, float dy, uint on)[] pos = [(0, 28, Tw.Green500), (28, 0, Tw.Red500), (-28, 0, Tw.Blue500), (0, -28, Tw.Amber500)];
        for (int i = 0; i < 4; i++)
        {
            float bx = 330 + pos[i].dx, by = 110 + pos[i].dy;
            if (face[i].IsActive.Value) s.FillCircle(pos[i].on, bx, by, 11);
            else s.StrokeRoundedRect(Tw.Slate500, 2, bx - 11, by - 11, 22, 22, 11);
        }

        // 右トリガ (Fire): 0..1 バー
        s.StrokeRoundedRect(Tw.Slate500, 1, 20, 210, 344, 18, 9);
        if (fire > 0.01f) s.FillRoundedRect(Tw.Red500, 22, 212, MathF.Max(6, 340 * fire), 14, 7);
    }

    private static WindowSystem? _secondWindows;
    private static WindowManager? _secondManager;

    [Story(RealWindowOnly = true)]
    public static StoryResult SecondWindow(StoryContext ctx)
    {
        Signal<string> state = new(_secondManager is null ? "未作成" : "表示中");
        void DisposeSecond()
        {
            _secondManager?.Dispose();
            _secondManager = null;
            _secondWindows?.Dispose();
            _secondWindows = null;
        }
        void Open()
        {
            if (_secondManager is not null) return;
            // UI向けウィンドウ管理は Luxel.Framework.UI が所有し、Win32はバックエンドとして注入する。
            _secondWindows = new WindowSystem(Win32WindowBackend.Create());
            _secondManager = new WindowManager(ctx.Device, ctx.Font, _secondWindows);
            Signal<int> n = new(0);
            _secondManager.CreateUiWindow(
                new Luxel.Platform.Abstraction.WindowDesc("Luxel — 2 つ目のウィンドウ", 480, 320),
                "second-window",
                () => Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))
                [VStack(10)[
                    Heading("独立した OS ウィンドウ", 2),
                    Label("この窓は Luxel.Framework.UI の WindowManager が管理する独立した UiHost を持つ"),
                    HStack(8)[
                        Button(_ => n.Value++, "カウント"),
                        Text($" {n} ", 16, vAlign: Align.Center)]]]);
            state.Value = "表示中";
            ctx.Log("WindowManagerで第2ウィンドウを作成 (480×320)");
        }
        void Close()
        {
            if (_secondManager is null) return;
            DisposeSecond();
            state.Value = "破棄済み";
            ctx.Log("第2ウィンドウを破棄");
        }
        void TickSecond()
        {
            if (_secondManager is null) return;
            if (_secondManager.RunFrame(1f / 60f)) return;
            DisposeSecond();
            state.Value = "閉じました";
        }
        return Frame(VStack(10)[
            Label("WindowManager — UIウィンドウ、キャンバス、UiHost、提示処理を束ねる実行ループ"),
            Muted("Gallery のフレームごとに RunFrame を呼び出して駆動します"),
            HStack(8)[
                Button(_ => Open(), "開く"),
                Button(_ => Close(), "破棄", variant: Variant.Outline),
                Text($" 状態: {state}", 13, vAlign: Align.Center)],
            Canvas2D(8, 8, animate: (_, _) => TickSecond())]);
    }

    /// <summary>0.4 秒のサイン波 (44.1kHz mono 16bit)。立ち上がり/終端フェードでクリックノイズを防ぐ。</summary>
    private static AudioClip Tone(float freq)
    {
        AudioFormat fmt = AudioFormat.Pcm16Mono44k;
        int n = (int)(fmt.SampleRate * 0.4f);
        byte[] pcm = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            float env = MathF.Min(1f, MathF.Min(i / 800f, (n - i) / 3000f));
            short s = (short)(MathF.Sin(MathF.Tau * freq * i / fmt.SampleRate) * env * short.MaxValue * 0.35f);
            pcm[i * 2] = (byte)s;
            pcm[i * 2 + 1] = (byte)(s >> 8);
        }
        return new AudioClip(fmt, pcm, $"tone{freq:0}");
    }
}
