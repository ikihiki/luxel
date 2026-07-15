# 29 — Luxel Studio シェル統合

## 概要

M11/M12 で実装した SceneEditorView / SceneInspector / Player / ScriptEditor / 出荷機能を、dogfood story 内のミニ構成から、Workbench + DockHost ベースの「Studio シェル」へ束ねる。Q53/Q57 では意図的に story 局所のボタンとペインで縦串を証明したため、ここでは実アプリとして反復利用できる操作面を作る。

## ゴール

- DockHost ベースの Studio 画面: SceneEditor、Inspector、AssetBrowser、ScriptEditor、Player View、Problems をドッキングできる。
- MenuBar / CommandPalette に New/Open/Save/Play/Pause/Step/Stop/Ship、Scene 操作、Script reload、Problems jump を登録する。
- scripts/*.csx を Workbench の `IEditorDocument` / `IDocumentProvider` として開き、保存時に PlayerBehaviours.Reload へ接続する。
- Problems ペインは csx コンパイル診断・実行時診断・AssetRef 欠落を統合表示する。
- Play View には editor gizmo / DevStats 相当の overlay toggle を置く。
- 2D/3D 混在プロジェクトを開き、SceneEditorView の space 自動選択と Player の scene request がシェル内でも動く。

## 非ゴール

- 3D のフル `scene_pbr` / glTF 展開 / Bepu 物理統合。
- タイムライン、プレハブ、ビジュアルスクリプティング、回転/スケールギズモ。
- マルチウィンドウや外部アセット監視の完全 UX。

## 実装方針

- 既存の `Luxel.Workbench` と `Luxel.Controls.DockHost` を正とし、Studio 専用の状態は薄く保つ。
- まず Gallery story `Apps/Studio/Shell` で hermetic な MemoryFileStorage プロジェクトを開く。実 FS app 化は story が安定してから。
- コマンドは `CommandRegistry` に集約し、ボタン直結ロジックを避ける。
- Player は ADR-0017 の契約どおり編集 SceneDoc から別 world を構築し、停止で破棄する。
- csx 診断は ADR-0018 の失敗契約をそのまま Problems に流す。

## 検証

- 単体: shell 状態遷移、document provider、diagnostic mapping。
- Gallery story: `Apps/Studio/Shell` play で open → edit scene → edit csx → play → compile error → fix → ship command mock を通す。
- full vk e2e diff 0。必要に応じて dx 対象 story を更新。
