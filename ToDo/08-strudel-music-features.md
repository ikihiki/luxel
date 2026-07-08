# 08 — Strudel: 音楽機能拡張 (記法・scale/chord・filter/delay・MIDI out・wav 音色)

## 概要

Strudel v1 で明示的にスコープ外とした音楽機能群。独立性が高く、以下の 5 つのサブタスクに分割して個別に着手できる (推奨順に記載)。すべて Docs/Strudel ([src/Luxel.Gallery/Stories/Docs/DocsStrudel.cs](../src/Luxel.Gallery/Stories/Docs/DocsStrudel.cs)) の「v1 スコープ外」リストに対応 — 実装したら同リストから削除して本文に節を足す。

## 共通の背景

- **2 層構成 (依存は Strudel → Audio の一方通行)**:
  - [src/Luxel.Strudel/](../src/Luxel.Strudel/): `Fraction` (有理数時間)、`TimeArc`+`Hap` (Whole/Part、HasOnset)、`Pattern<T>` (TimeArc→Hap 列の純関数、時間変形 = クエリ逆写像 + 結果順写像の対)、`MiniNotation` (bd sd / ~ / [] / * / / <> / @ / ? / :n / ,)、`StrudelEval` (チェーン式インタプリタ、値 = 数値/文字列/パターン/変換)、`StrudelKit` (プロシージャル音色、固定シード xorshift)、`StrudelScheduler` (スロット制、窓クエリ、時刻は Fraction で組んでから double 化)。
  - [src/Luxel.Audio/Sequencing/](../src/Luxel.Audio/Sequencing/): `ControlMap` (固定フィールド record struct: Instrument/N/Note/Gain/Pan/Speed、Merge = 右優先)、`ScheduledEvent` (絶対秒)、`IEventSink` (Schedule(窓)+Hush)、`IInstrument`+`InstrumentBank` (Fallback = note だけでも鳴る)、`StreamMixerSink` (サンプル精度ミキサ、PCM チャンク 100ms、等パワー pan、speed=線形補間リサンプル、tanh ソフトクリップ、KeepLastChunk でテスト検証、CopyPeaks で UI 波形)。
- **テストの流儀**: tests/Luxel.Tests/Strudel{Pattern,MiniNotation,Eval,Audio}Tests.cs (~66 本)。パターンはゴールデン文字列 ("0-1/4:bd …")、ミキサはインパルス音色でサンプル位置検証、EndToEnd はパターン→PCM の決定性比較。**新機能もこの 3 段 (パターン文字列 / PCM サンプル / E2E 決定性) で書く。**
- **決定性**: 乱数は固定シード xorshift のみ。wall-clock 禁止。

## サブタスク A — 記法の残り: `!` (繰り返し) と `.` (グループ) ✅ 完了 (2026-07-08)

**実装済み**: `ParseTerm` に後置 `!n` (数値省略で 2 回)、`ParseSequence` に単独 `!` (直前ステップ複製) と `.` グループ区切り (`PeekIsDotSeparator` = 前後空白の単独ドットのみ区切り扱い、`0.5` 等のトークン内ドットは非区切り)。`.` グループは各グループ等分 → `Pat.TimeCat`。テスト +7 (StrudelMiniNotationTests)。Docs/Strudel の記法表に 2 行追加 + v1 スコープ外から記法項目を削除。golden Docs_Strudel を vk/dx 更新。全 856 passed、vk e2e 73/73 diff 0。

- `MiniNotation.cs` のパーサ拡張。Strudel 本家の意味論: `bd!3` = `bd bd bd` (要素を横に展開)、`bd . sd sd . hh` = `.` 区切りの各グループが等分 (= `[bd] [sd sd] [hh]` の糖衣)。
- パースエラーは位置付き `MiniNotationError` を維持。
- テスト: MiniNotationTests にゴールデン文字列で (`"bd!3"` → `"0-1/3:bd 1/3-2/3:bd 2/3-1:bd"` 相当)。展開等価性 (`bd!3` == `bd bd bd`、`a . b c` == `[a] [b c]`) も比較で。

## サブタスク B — scale / chord

- 音階: `n("0 2 4").scale("C:minor")` 風 — 数値パターン (度数) をスケール表で半音オフセット → ControlMap.Note へ。スケール表 (major/minor/dorian/pentatonic 程度から) は static 辞書。
- コード: `chord("C Am F G")` は 1 イベント → 複数 Hap (同時発音) への展開。`Pattern<T>` は Hap 列なので同一 TimeArc に複数 Hap を返せばよい。
- StrudelEval にチェーンメソッド `scale`/`chord` を追加 (値種別「変換」の既存機構に乗せる)。
- テスト: EvalTests (チェーン評価) + PatternTests (度数→ノート番号のゴールデン)。

## サブタスク C — エフェクト: filter / delay

- ControlMap にフィールド追加: `Cutoff` (lpf)、`Resonance`、`DelayTime`/`DelayFeedback`/`DelayMix` など。record struct の固定フィールド方式を維持 (Merge = 右優先も自動で効く)。
- StreamMixerSink に DSP を実装:
  - filter: voice ごとの biquad LPF (係数は cutoff/resonance から。状態は voice に持つ)。
  - delay: スロット (または全体バス) ごとのフィードバックディレイライン (float 循環バッファ)。
- StrudelEval に `.lpf(800)` / `.delay(0.25)` 等のチェーンを追加 → ControlMap へ。
- テスト: インパルス音色でディレイの反射位置 (サンプル単位) を検証 / LPF は高周波正弦波の振幅減衰を assert。決定的なので数値比較可。

## サブタスク D — wav サンプル音色

- `IInstrument` の新実装 `SampleInstrument` (PCM float 配列 + ベース周波数、Speed/N でリサンプル再生 — StreamMixerSink の既存線形補間を流用できるか確認)。
- wav 読み込み: RIFF PCM 16bit/float の最小パーサを Luxel.Audio に (依存追加なしで書ける)。リソース DAG (Luxel.Resources) に載せるなら (型,uri) ノード + デコードステップ。
- InstrumentBank への登録口 (名前 → wav)。Gallery デモ用のサンプル音源はライセンスフリーの短い打楽器 1〜2 個を assets/ に (数十 KB)。
- テスト: 既知 PCM (プロシージャル生成した wav バイト列をテスト内で組む) をロード → スケジュール → KeepLastChunk のサンプル位置検証。**実 wav ファイルに依存しない**のが決定性の定石。

## サブタスク E — MIDI out sink

- `IEventSink` の新実装 `MidiOutSink`: ScheduledEvent → MIDI note on/off。ControlMap.Note/N → ノート番号、Gain → velocity。
- Windows MIDI API (winmm の midiOut* を P/Invoke、または Windows.Devices.Midi)。CsWin32 の前例 (Luxel.Platform) に倣う。
- タイミング: StrudelScheduler は窓で先読みスケジュールする — MIDI 送出は自前タイマスレッドで絶対秒 → 実時間へ (音声と違い PCM に焼けない)。**実デバイス依存なので自動テストはイベント列の生成までに留め、送出は実機スモーク扱い** (RealWindowOnly ストーリー相当)。
- headless では NullMidiOut にフォールバック。

## Docs / デモ

- 各サブタスク完了時に DocsStrudel の「v1 スコープ外」から該当項目を削除し、本文 + REPL 初期コード例を更新。
- Strudel/Repl の play は初期無音のまま (決定的)。エフェクトの絵は波形表示 (CopyPeaks) の golden で見せられる。

## 罠・注意

- 時刻は必ず Fraction で組んでから double 化 (0.1×n の蓄積誤差でテストが割れた前例)。
- `HasOnset` の意味論 (断片が頭を含むときだけ発音) を新パターン変形でも守る — 窓分割で二重発火しない。
- ControlMap のフィールド追加は Merge (右優先) の対称性を保つ。
- hush での投入済みチャンク破棄 (最大 300ms 鳴り切る) は既知の割り切り — エフェクト追加でも同じ。
