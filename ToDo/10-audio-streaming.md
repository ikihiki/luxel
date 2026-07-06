# 10 — Audio: ストリーミング再生

## 概要

現状の Luxel.Audio は「16bit PCM 全展開クリップ」のみ (Docs/Audio に記載) — BGM のような長尺音源はメモリに全部展開するしかない。ファイルから逐次デコードしながら再生するストリーミング経路を追加する。

## 背景と現状

- **Luxel.Audio** (src/Luxel.Audio/): XAudio2 バックエンド。実窓専用ストーリー Audio/Tone (RealWindowOnly) がある。headless では NullAudioBackend。
- **既存のチャンク供給の前例**: Strudel の `StreamMixerSink` ([src/Luxel.Audio/Sequencing/](../src/Luxel.Audio/Sequencing/)) は「PCM チャンク 100ms を生成して `BuffersQueued < 3` までポンプ」という**まさにストリーミングの骨格**を既に持っている。音源が「シーケンサが焼く PCM」か「ファイルからデコードした PCM」かの違いだけ — この供給モデル (毎 Tick ポンプ、キュー深さ 3) を踏襲する。
- 駆動: ReplRoot の AddAnimation で毎 Tick ポンプしている前例 (StrudelStory)。Framework アプリならフレームループから。

## 実装方針

1. **抽象**: `IAudioStream { int SampleRate; int Channels; int Read(Span<float> dst); }` (Read は読めたサンプル数、0 = 終端)。ループ再生は wrapper (`LoopingStream`) で。
2. **デコーダ**:
   - wav (RIFF PCM): 自前パーサで逐次読み (依存なし)。[08](08-strudel-music-features.md) のサブタスク D と共用できる — 先に実装した方に合わせる。
   - 圧縮 (ogg/mp3): v1 は **NVorbis (ogg)** 1 パッケージだけ追加が現実的 (mp3 は特許問題は切れたが実装が重い)。依存追加を最小にしたければ wav ストリーミングのみを v1 とし、ogg は次段。
3. **再生経路**: `StreamingVoice` — XAudio2 の source voice に「チャンク再生完了 → 次チャンクをデコードして submit」。StreamMixerSink と同じ「Tick ポンプ + BuffersQueued < 3」方式なら専用スレッド不要 (フレームレート依存のジッタはキュー深さで吸収)。デコードがフレーム予算を食う場合のみバックグラウンドデコードを検討 (v1 はメインスレッドで可)。
4. **IO**: ファイルは FileStream 直読みで開始。リソース DAG (Luxel.Resources) には「ハンドルを返す」型 (ストリームは DAG のノードにしにくい) — `AudioStreamSource` のような薄い登録に留める。

## テスト

- デコーダ単体 (GPU/実デバイス不要・決定的): テスト内で生成した wav バイト列 → IAudioStream.Read の内容一致、チャンク境界跨ぎ、終端、ループ wrapper。
- ポンプロジック: IAudioStream (テスト用正弦波) → 「キューが常に 1..3 に保たれる」「終端で voice が止まる」を、XAudio2 をモックした IAudioBackend 面で検証 (NullAudioBackend の拡張 or テスト用スパイ)。
- 実音は Audio/Tone と同様 RealWindowOnly ストーリーで実機スモーク (自動 golden 対象外)。

## 罠・注意

- XAudio2 の buffer submit は再生完了前のバッファ再利用に注意 — チャンクバッファはキュー深さ分 (3+1) をリングで持つ。
- サンプルレート変換: ソースが 44.1kHz でデバイスが 48kHz のケース。XAudio2 の source voice はレート指定できる (voice 生成時にソースのレートを渡せば XAudio2 が変換) — 自前リサンプルは不要のはず。既存クリップ再生がどうしているかに合わせる。
- headless (NullAudioBackend) でストリーミング経路が例外を吐かない (E2E がここを通る可能性)。
- Docs/Audio (src/Luxel.Gallery/Stories/Docs/DocsRuntime.cs 内) の「16bit PCM 全展開のみ」記述を更新。

## スコープ外

- Doppler 効果 (Docs の将来枠、別タスク)、3D 空間音響、mp3 対応。
