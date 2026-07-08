using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>Docs: Strudel (パターン言語 + シーケンシング層) の概念ページ。</summary>
public static class DocsStrudel
{
    [Story("Docs/Strudel", Order = 55)]
    public static Widget Strudel(StoryContext ctx) => ctx.Snap(WithDocFonts(Docs(ctx, $$"""
        # ライブコーディング (Luxel.Strudel)

        [Strudel](https://strudel.cc) (TidalCycles の JS 版) のパターン言語を C# で実装したものです。実演は {{StoryRef(ctx, "Demos/Strudel/Repl")}} へ。2 層に分かれます:

        - **Luxel.Strudel** — パターン言語 (有理数時間のクエリモデル + ミニ記法 + チェーン式評価)
        - **Luxel.Audio.Sequencing** — 汎用シーケンシング層 (イベント表現 + サンプル精度ミキサ)。Strudel を知らず、ゲームの効果音シーケンスにも使えます

        ## クエリモデル — パターンは純関数

        `Pattern<T>` は「**時間区間 → イベント列**」の純関数です (状態なし)。時間の単位はサイクル (1 サイクル = 1 小節相当) で、`Fraction` (有理数) — float だと 3 連符の境界判定が誤差で壊れます。

        ```csharp
        Pattern<string> p = MiniNotation.Parse("bd sd hh*2");
        foreach (Hap<string> h in p.Query(new TimeArc(0, 1)))   // サイクル 0 を観測
            Console.WriteLine(h);   // [0,1/4) bd / [1/4,1/2) sd / [1/2,3/4) hh / [3/4,1) hh
        ```

        イベント (`Hap`) は「本来の全区間 (Whole)」と「クエリで見えた断片 (Part)」を持ち、**断片が頭を含むときだけ発音** (`HasOnset`) します — スケジューラがどんな窓幅で先読みしても二重発火しません。時間変形は「クエリを逆写像 → 結果を順写像」の対で実装され、`Fast/Slow/Rev/Every/Iter/Off/Degrade` などが合成できます。

        ## ミニ記法

        | 記法 | 意味 |
        | --- | --- |
        | `bd sd hh` | シーケンス (1 サイクルを均等分割) |
        | `~` | 休符 |
        | `[bd sd]` | グループ (1 ステップに圧縮)。`[a, b]` はグループ内スタック |
        | `a*2` / `a/2` | そのステップだけ速く / 遅く |
        | `<a b c>` | サイクルごとに交互 |
        | `a@3 b` | 重み (a がサイクルの 3/4) |
        | `a?` / `a?0.3` | 確率で間引く (時間ハッシュ — 決定的) |
        | `bd!3` / `bd ! !` | 繰り返し (横に複製 — `bd!3` = `bd bd bd`、数値省略で 2 回) |
        | `bd . sd sd . hh` | `.` 区切りグループ (各グループを等分 = `[bd] [sd sd] [hh]`) |
        | `bd:3` | サンプルバリエーション番号 |
        | `a , b` | スタック (同時再生) |

        ## チェーン式評価

        REPL の 1 行は JS eval ではなく極小インタプリタで解釈します (関数 + メソッドチェーン + リテラルのみ — 依存ゼロで決定的):

        ```csharp
        s("bd*2 [~ sd] hh*4").gain(0.9).every(2, rev)
        note("c3 eb3 g3 <bb3 c4>").s("saw").slow(2).jux(fast(2))
        stack(s("bd*2"), s("hh*4").gain("1 0.6"))
        cps(0.6)     // テンポ (cycles per second)
        silence      // このスロットを停止
        ```

        `rev` や `fast(2)` は単独では**変換** (パターン → パターン) で、`every/off/jux` の引数にそのまま渡せます。`gain` 等の値引数は数値かミニ記法文字列 (`"1 0.5"`) — 左のイベント構造を保ったまま値だけ合流します。

        ## 実行系 — Hap → ScheduledEvent → sink

        ```
        Pattern (有理時間・純関数)
          → StrudelScheduler   窓ごとにクエリ、cps でサイクル→秒へ (Fraction で厳密)
          → ScheduledEvent     絶対秒 + 持続 + ControlMap (MIDI 相当の汎用表現)
          → IEventSink         ミキサ / UI ハイライト / (将来) MIDI out
        ```

        `StreamMixerSink` はイベントを **PCM チャンク (100ms) に焼き込み**、1 本のストリーミング voice へ 300ms 先まで先行投入します。voice を都度 Play() するとフレーム精度 (±16ms) のジッタが出るため、サンプル位置に直接書きます — 精度は 1 サンプル。gain / 等パワー pan / speed (線形補間リサンプル) はイベント単位でここで掛かり、出力は tanh ソフトクリップです。

        評価 = `SetPattern` の**ホットスワップ**で、クロックは止まらず次の窓 (≤300ms) から新パターンが鳴ります。音色は全てプロシージャル生成 (bd/sd/hh/oh/cp/rim/lt/ht + sine/tri/saw/square、固定シード) — バイナリアセットなしで、**パターン → PCM が丸ごと決定的**なのでユニットテストで波形を検証しています。

        エディタは各ライブブロックが `CodeEditor` (ガター/等幅/横スクロール) で、`StrudelCodeLanguage` (`ICodeLanguage` 実装) を挿してあります: `StrudelEval` でパースした `StrudelEvalError` (MiniNotation の位置付きエラーも畳まれる) を**診断波線**に写し (評価するだけで音は出さない)、`.` の直後はメソッド・クォート内は音色を静的補完します。**Ctrl+Enter** はエディタの `OnKeyIntercept` で横取りし、そのブロックを Run (= ホットスワップ) します — 通常の Enter は改行のまま。

        > [!NOTE]
        > v1 スコープ外: MIDI out sink・サンプル (wav) 音色・scale/chord・エフェクト (filter/delay)。イベント表現が汎用なので、MIDI out は `IEventSink` 実装 1 つで足せます。
        """)));
}
