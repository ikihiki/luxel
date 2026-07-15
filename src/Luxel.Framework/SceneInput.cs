using Luxel.Input;

namespace Luxel.Framework;

/// <summary>SceneManagerがScene所有のInputContextをどのように合成するか。</summary>
public enum SceneInputMode
{
    /// <summary>下層Sceneの入力を維持し、InputStackの通常のconsume規則で合成する。</summary>
    Shared,
    /// <summary>このSceneより下に描画されるScene所有InputContextをsuspendする。</summary>
    Modal,
}

/// <summary>
/// Sceneが所有するInputContextの任意契約。SceneManagerがActive期間だけInputStackへ登録し、
/// Suspend・遷移・Modal overlayに合わせて有効状態を管理する。
/// </summary>
public interface ISceneInputParticipant
{
    SceneInputMode InputMode => SceneInputMode.Shared;
    IReadOnlyList<InputContext> InputContexts { get; }
}

/// <summary>Overlay等が下層simulationを参照カウント付きで停止するための契約。</summary>
public interface IPausableScene
{
    bool IsPaused { get; }
    IDisposable AcquirePause();
}
