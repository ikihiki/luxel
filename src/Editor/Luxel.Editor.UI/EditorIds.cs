namespace Luxel.Controls;

public static class EditorCommandIds
{
    public const string Save = "file.save";
    public const string SaveAs = "file.saveAs";
    public const string SaveAll = "file.saveAll";
    public const string Close = "file.close";
    public const string CloseProject = "file.closeProject";
    public const string Exit = "file.exit";
    public const string ResetLayout = "window.resetLayout";
    public const string FocusMode = "window.focusMode";
    public const string Undo = "edit.undo";
    public const string Redo = "edit.redo";
    public const string Play = "run.play";
    public const string Stop = "run.stop";
}

public static class EditorPaneIds
{
    public const string Hierarchy = "hierarchy";
    public const string Scene = "scene";
    public const string Inspector = "inspector";
    public const string Assets = "assets";
    public const string Documents = "documents";
    public const string Problems = "problems";
    public const string Output = "output";
    public const string Settings = "settings";
    public const string KeyBindings = "keybindings";
    public const string Play = "play";
}

public static class EditorDocumentProviderIds
{
    public const string Text = "text";
    public const string Code = "code";
    public const string Scene = "scene";
    public const string NodeGraph = "node-graph";
}
