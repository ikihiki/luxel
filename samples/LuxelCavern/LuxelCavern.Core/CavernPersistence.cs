using Luxel.Settings;

namespace LuxelCavern.Core;

/// <summary>
/// セーブの保存/読込を <see cref="IFileStore"/> 上で行う薄い層 (JSON 直列化 = <see cref="CavernSave"/>)。
/// 実ファイルの場所は呼び出し側 — exe は <see cref="PhysicalFileStore"/> に <c>%APPDATA%/LuxelCavern</c> を
/// 渡し、テストは <see cref="InMemoryFileStore"/> を渡して file IO 非依存に検証する (Q06 IFileStore のドッグフード)。
/// 削除口が無いので「消去」は空書き込みで表現し、読込は空/壊れを「セーブ無し」として扱う (never throw)。
/// </summary>
public static class CavernPersistence
{
    /// <summary>セーブファイル名 (<see cref="IFileStore"/> のルート直下)。</summary>
    public const string SaveName = "cavern-save.json";

    /// <summary>進捗を保存 (上書き)。</summary>
    public static void Save(IFileStore files, CavernSave save) => files.Write(SaveName, save.ToJson());

    /// <summary>セーブが存在し読み込める状態か (タイトルの「つづきから」判定)。</summary>
    public static bool HasSave(IFileStore files) => TryLoad(files) is not null;

    /// <summary>セーブを読む。無い/空/壊れている場合は <c>null</c> (「最初から」へフォールバック、例外は投げない)。</summary>
    public static CavernSave? TryLoad(IFileStore files)
    {
        string? json = files.Read(SaveName);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return CavernSave.FromJson(json); }
        catch { return null; }   // 壊れたセーブはゲームを止めない — 新規開始に倒す
    }

    /// <summary>セーブを消去する (<see cref="IFileStore"/> に削除が無いので空を書く = 「セーブ無し」)。</summary>
    public static void Clear(IFileStore files) => files.Write(SaveName, string.Empty);
}
