using System.Numerics;
using Luxel.Controls;
using Luxel.SceneEdit;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>SceneEditorView — シーンエディタ (ADR-0016 / ToDo 27 GE-1) のビュー。エンティティの選択/移動/複製/削除を
/// 編集する。編集意味論は canvas 非依存の Luxel.SceneEdit (Transaction スタック 3 本目)、空間の知識 (座標変換/ヒット/
/// カメラ/描画) は ISceneSpaceAdapter に閉じる — M11 は 2D アダプタ、3D アダプタは M12 でシェル無改修で足す。</summary>
public static class SceneEditorViewStory
{
    // Player / Enemy / Coin の 3 エンティティ (transform2d のみ — 見た目はプレースホルダボックス)
    private static SceneDoc SampleScene()
    {
        SceneEntity E(int id, string name, float x, float y)
            => SceneEntity.Of(id, name, SceneSchemas.NewComponent(SceneSchemas.Transform2D)
                .With("pos", SceneValue.Of(new Vector2(x, y))));
        return SceneDoc.Of(SceneSpace.TwoD, [E(1, "Player", 120, 100), E(2, "Enemy", 300, 180), E(3, "Coin", 470, 120)]);
    }

    [Story("Controls/SceneEditorView/Basic", Height = 440)]
    public static Widget Basic(StoryContext ctx)
    {
        SceneEditorView ed = SceneEditorView(source: SampleScene(), viewWidth: 620f, viewHeight: 360f);

        ctx.Play("basic", async d =>
        {
            await d.Snap();                              // 3 エンティティ + グリッド
            Vector2 p2 = ed.EntityScreenCenter(2);
            await d.Click(p2.X, p2.Y);                   // Enemy を選択 → 軸ハンドルが出る
            await d.Expect(() => ed.IsSelected(2), "クリックで選択");
            await d.Snap("selected");
            Vector2 from = ed.EntityScreenCenter(2);
            await d.Drag(from.X, from.Y, from.X + 60, from.Y + 60);   // 本体ドラッグ = 自由移動
            await d.Expect(() => ed.EntityPos2D(2) == new Vector2(360, 240), "ドラッグで移動");
            await d.Snap("moved");
            await d.Key(Key.Z, ctrl: true);
            await d.Expect(() => ed.EntityPos2D(2) == new Vector2(300, 180), "undo で戻る (1 undo)");
        });

        ctx.Play("handle", async d =>
        {
            Vector2 p1 = ed.EntityScreenCenter(1);
            await d.Click(p1.X, p1.Y);                   // Player を選択
            await d.Expect(() => ed.IsSelected(1), "選択でハンドル表示");
            // X 軸ハンドル (中心の右 30px) を掴んで斜めにドラッグ → X だけ動く
            await d.Drag(p1.X + 30, p1.Y, p1.X + 80, p1.Y + 40);
            await d.Expect(() => ed.EntityPos2D(1) == new Vector2(170, 100), "X 軸拘束移動 (Y 不変)");
            await d.Snap("axis");
        });

        ctx.Play("marquee", async d =>
        {
            // 空白から Player + Enemy を囲む範囲選択 (Coin は外)
            Vector2 a = ed.ClientOf(new Vector2(10, 10));
            Vector2 b = ed.ClientOf(new Vector2(400, 250));
            await d.Drag(a.X, a.Y, b.X, b.Y);
            await d.Expect(() => ed.SelectionCount == 2, "範囲選択で 2 エンティティ");
            await d.Snap("box");
        });

        ctx.Play("keys", async d =>
        {
            Vector2 p3 = ed.EntityScreenCenter(3);
            await d.Click(p3.X, p3.Y);                   // フォーカス
            await d.Key(Key.A, ctrl: true);
            await d.Expect(() => ed.SelectionCount == 3, "Ctrl+A で全選択");
            await d.Key(Key.Delete);
            await d.Expect(() => ed.EntityCount == 0, "Delete で削除 (1 undo)");
            await d.Key(Key.Z, ctrl: true);
            await d.Expect(() => ed.EntityCount == 3, "undo で復活 (選択も戻る)");
            await d.Key(Key.D, ctrl: true);              // 複製 (+24,+24 オフセット)
            await d.Expect(() => ed.EntityCount == 6, "Ctrl+D で複製");
            await d.Snap("duplicated");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("SceneEditorView (シーンエディタ)"),
                Muted("Luxel.SceneEdit (不変 + Transaction) を空間アダプタ経由で描く薄いシェル。クリック/範囲選択・本体ドラッグ移動・軸ハンドル (X=赤/Y=緑)・Ctrl+D 複製・ホイールズーム・中ボタン pan。"),
                ed]];
    }
}
