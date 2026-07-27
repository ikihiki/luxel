namespace Luxel.Platform;

/// <summary>IME候補ウィンドウの配置に使う、クライアント領域内の論理座標矩形。</summary>
public readonly record struct TextInputRect(float X, float Y, float Width, float Height);

/// <summary>ウィンドウに結び付いたOS入力メソッドの状態。</summary>
public interface IWindowTextInputContext : IDisposable
{
    bool Active { get; }
    bool ShouldDispatchTextInput { get; }
}

/// <summary>バックエンド固有の入力メソッドコンテキストを生成するオプション機能。</summary>
public interface IWindowTextInputContextFactory
{
    IWindowTextInputContext Create(NativeWindow window, Func<ITextInputClient?> getClient, Func<float>? getScale = null);
}

/// <summary>
/// OSの入力メソッドがフォーカス中のテキスト編集面を操作するための共通契約。
/// UIフレームワーク固有型やOS固有型を公開せず、選択、置換、キャレット位置、変換装飾だけを扱う。
/// </summary>
public interface ITextInputClient
{
    string Text { get; }
    (int Start, int Length) Selection { get; }
    void Select(int start, int end);
    void Replace(int start, int end, string text);
    TextInputRect? CaretRect { get; }

    /// <summary>
    /// 変換中範囲と変換対象節を通知する。length=0で装飾を解除する。
    /// </summary>
    void SetCompositionHighlight(int start, int length, int targetStart, int targetLength);
}
