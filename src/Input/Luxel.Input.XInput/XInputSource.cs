using System.Runtime.InteropServices;
using Luxel.Input;

namespace Luxel.Input.XInput;

/// <summary>
/// Windows XInput (Xbox 互換 Gamepad) を <see cref="IInputSource"/> として <see cref="InputBus"/> に流す。
/// 起動時にプラットフォームで controller が存在しない場合、<see cref="Poll"/> は何もしない (安全な no-op)。
///
/// dead zone は Microsoft 推奨値 (LT/RT 30、Stick 7849) を採用。差分検出で「変化があった軸」だけ event 化。
/// </summary>
public sealed class XInputSource : IInputSource
{
    public string Name => "XInput";

    private const int ThumbDeadZone = 7849;
    private const int TriggerDeadZone = 30;
    private const short ThumbMax = short.MaxValue;
    private const byte TriggerMax = byte.MaxValue;

    private readonly uint _userIndex;
    private ushort _lastButtons;
    private float _lastLX, _lastLY, _lastRX, _lastRY, _lastLT, _lastRT;
    private bool _wasConnected;

    public XInputSource(uint userIndex = 0) { _userIndex = userIndex; }

    public void Poll(InputBus bus)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (XInputGetState(_userIndex, out XINPUT_STATE state) != 0)
        {
            // 切断: 前 tick で押されていたボタン/軸を全て 0 に戻す (KeyUp / Axis 0 で emit)
            if (_wasConnected) { ResetAll(bus); _wasConnected = false; }
            return;
        }
        _wasConnected = true;
        var gp = state.Gamepad;

        // ボタン差分
        ushort diff = (ushort)(gp.wButtons ^ _lastButtons);
        for (int i = 0; i < 16; i++)
        {
            ushort mask = (ushort)(1 << i);
            if ((diff & mask) == 0) continue;
            bool down = (gp.wButtons & mask) != 0;
            var kc = MapButton(mask);
            if (kc == KeyCode.None) continue;
            bus.EnqueueKey(kc, down);
        }
        _lastButtons = gp.wButtons;

        // Stick axes (dead zone 適用)
        float lx = ApplyDeadZone(gp.sThumbLX, ThumbDeadZone, ThumbMax);
        float ly = ApplyDeadZone(gp.sThumbLY, ThumbDeadZone, ThumbMax);
        float rx = ApplyDeadZone(gp.sThumbRX, ThumbDeadZone, ThumbMax);
        float ry = ApplyDeadZone(gp.sThumbRY, ThumbDeadZone, ThumbMax);
        if (lx != _lastLX) { bus.EnqueueAxis(AxisCode.GamepadLeftStickX, lx); _lastLX = lx; }
        if (ly != _lastLY) { bus.EnqueueAxis(AxisCode.GamepadLeftStickY, ly); _lastLY = ly; }
        if (rx != _lastRX) { bus.EnqueueAxis(AxisCode.GamepadRightStickX, rx); _lastRX = rx; }
        if (ry != _lastRY) { bus.EnqueueAxis(AxisCode.GamepadRightStickY, ry); _lastRY = ry; }

        // Trigger axes (0..1)
        float lt = ApplyTriggerDeadZone(gp.bLeftTrigger);
        float rt = ApplyTriggerDeadZone(gp.bRightTrigger);
        if (lt != _lastLT) { bus.EnqueueAxis(AxisCode.GamepadLeftTrigger, lt); _lastLT = lt; }
        if (rt != _lastRT) { bus.EnqueueAxis(AxisCode.GamepadRightTrigger, rt); _lastRT = rt; }
    }

    private void ResetAll(InputBus bus)
    {
        for (int i = 0; i < 16; i++)
        {
            ushort mask = (ushort)(1 << i);
            if ((_lastButtons & mask) == 0) continue;
            var kc = MapButton(mask);
            if (kc != KeyCode.None) bus.EnqueueKey(kc, false);
        }
        _lastButtons = 0;
        if (_lastLX != 0) { bus.EnqueueAxis(AxisCode.GamepadLeftStickX, 0); _lastLX = 0; }
        if (_lastLY != 0) { bus.EnqueueAxis(AxisCode.GamepadLeftStickY, 0); _lastLY = 0; }
        if (_lastRX != 0) { bus.EnqueueAxis(AxisCode.GamepadRightStickX, 0); _lastRX = 0; }
        if (_lastRY != 0) { bus.EnqueueAxis(AxisCode.GamepadRightStickY, 0); _lastRY = 0; }
        if (_lastLT != 0) { bus.EnqueueAxis(AxisCode.GamepadLeftTrigger, 0); _lastLT = 0; }
        if (_lastRT != 0) { bus.EnqueueAxis(AxisCode.GamepadRightTrigger, 0); _lastRT = 0; }
    }

    private static float ApplyDeadZone(short v, int deadZone, short max)
    {
        int a = Math.Abs((int)v);
        if (a < deadZone) return 0f;
        float sign = v < 0 ? -1f : 1f;
        return sign * (a - deadZone) / (float)(max - deadZone);
    }

    private static float ApplyTriggerDeadZone(byte v)
    {
        if (v < TriggerDeadZone) return 0f;
        return (v - TriggerDeadZone) / (float)(TriggerMax - TriggerDeadZone);
    }

    private static KeyCode MapButton(ushort mask) => mask switch
    {
        0x0001 => KeyCode.GamepadDPadUp,
        0x0002 => KeyCode.GamepadDPadDown,
        0x0004 => KeyCode.GamepadDPadLeft,
        0x0008 => KeyCode.GamepadDPadRight,
        0x0010 => KeyCode.GamepadStart,
        0x0020 => KeyCode.GamepadBack,
        0x0040 => KeyCode.GamepadLeftStick,
        0x0080 => KeyCode.GamepadRightStick,
        0x0100 => KeyCode.GamepadLB,
        0x0200 => KeyCode.GamepadRB,
        0x1000 => KeyCode.GamepadA,
        0x2000 => KeyCode.GamepadB,
        0x4000 => KeyCode.GamepadX,
        0x8000 => KeyCode.GamepadY,
        _ => KeyCode.None,
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState(uint userIndex, out XINPUT_STATE state);
}
