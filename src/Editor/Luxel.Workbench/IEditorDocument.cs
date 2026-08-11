using Luxel.UI;

namespace Luxel.Workbench;

/// <summary>
/// Workbench に載る 1 ドキュメントの**不透明ハンドル** (ADR-0010)。各エディタスタック
/// (テキスト = Luxel.Document、ノード = Luxel.NodeGraph、…) がこれを実装するアダプタを持ち、
/// **内部モデル (EditorState/NodeGraphState/…) は統一しない** — シェルは Kind とこの契約
/// だけを見る。view の実体化は呼び側 (DockHost 等) の責務で、本インターフェースは
/// canvas/GPU に触れない。
/// </summary>
public interface IEditorDocument
{
    /// <summary>ドキュメント種別 (プロバイダ登録キー、例: "text" / "node" / "strudel")。</summary>
    string Kind { get; }

    /// <summary>タブ等に出す表示名。</summary>
    string Title { get; }

    /// <summary>未保存変更があるか (タブの ● とダーティ集約用)。実装が保存/編集で更新する。</summary>
    Signal<bool> Dirty { get; }

    /// <summary>このドキュメントを編集する view を作る。呼ぶたび新しい widget
    /// (同一ドキュメントを複数ペインで開く分割ビューを許すため)。</summary>
    Widget CreateView();

    /// <summary>undo 可能か。リアクティブでない (メニュー enablement は表示時に読む)。</summary>
    bool CanUndo { get; }

    /// <summary>redo 可能か。</summary>
    bool CanRedo { get; }

    void Undo();

    void Redo();

    /// <summary>永続化表現へ直列化する (テキスト = 文字列そのもの、ノード = JSON、…)。
    /// 往復契約: <c>LoadFrom(Serialize())</c> が同じ内容を復元する。</summary>
    string Serialize();

    /// <summary>直列化表現から内容を読み込む (open の実体)。</summary>
    void LoadFrom(string content);

    /// <summary>メニュー/ツールバー寄与 (ADR-0013 の「面」)。アクティブなときシェルが
    /// CommandRegistry のビュー (MenuBar/CommandPalette/Toolbar/Keymap) へ合成する。既定 = なし。</summary>
    IReadOnlyList<CommandContribution> Contributions => [];
}

/// <summary>
/// Kind ↔ ドキュメント生成の工場 (ADR-0010)。ドメイン (エディタ構成) が Workspace へ登録する
/// — Unity の asset editor 登録と同じ向き。open は <see cref="CreateNew"/> +
/// <see cref="IEditorDocument.LoadFrom"/> で行うため new だけを持つ。
/// </summary>
public interface IDocumentProvider
{
    /// <summary>担当する種別 (<see cref="IEditorDocument.Kind"/> と一致)。</summary>
    string Kind { get; }

    /// <summary>メニュー等に出す種別名 (例: "テキスト" / "ノードグラフ")。</summary>
    string DisplayName { get; }

    /// <summary>空の新規ドキュメントを作る。</summary>
    IEditorDocument CreateNew();
}
