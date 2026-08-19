using Luxel.Controls;
using Luxel.Graphics.TwoD;
using Luxel.SceneEdit;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

public sealed class SceneHierarchyTests
{
    [Fact]
    public void CrudReparentVisibilityLockAndUndoUseSceneHistory()
    {
        SceneDoc source = SceneDoc.Of(SceneSpace.TwoD, [SceneEntity.Of(1, "Root")]);
        var document = new SceneDocument("scene", source, doc => EditorKit.SceneEditorView(source: doc));
        var selection = new EditorSelectionService();
        var hierarchy = new SceneHierarchyController("scene", document, selection);

        int child = hierarchy.Create("Child", 1);
        hierarchy.Rename(child, "Renamed");
        hierarchy.SetVisible(child, false);
        hierarchy.SetLocked(child, true);

        SceneHierarchyNode root = Assert.Single(hierarchy.Build());
        SceneHierarchyNode nested = Assert.Single(root.Children);
        Assert.Equal("Renamed", nested.Name);
        Assert.False(nested.Visible);
        Assert.True(nested.Locked);
        Assert.Equal(child, selection.Current.Value.MainEntityId);
        Assert.True(document.Dirty.Value);

        document.Undo();
        Assert.False(Assert.Single(Assert.Single(hierarchy.Build()).Children).Locked);
    }

    [Fact]
    public void ReparentRejectsSelfCyclesAndLockedTargets()
    {
        SceneDoc source = SceneDoc.Of(SceneSpace.TwoD, [SceneEntity.Of(1, "A"), SceneEntity.Of(2, "B")]);
        var document = new SceneDocument("scene", source, doc => EditorKit.SceneEditorView(source: doc));
        var hierarchy = new SceneHierarchyController("scene", document, new EditorSelectionService());
        hierarchy.Reparent(2, 1);

        Assert.Throws<InvalidOperationException>(() => hierarchy.Reparent(1, 2));
        Assert.Throws<InvalidOperationException>(() => hierarchy.Reparent(1, 1));
        hierarchy.SetLocked(1, true);
        Assert.Throws<InvalidOperationException>(() => hierarchy.Reparent(2, 1));
    }

    [Fact]
    public void MultiSelectionSynchronizesAndControllerRestoresSceneCallbackOnDispose()
    {
        SceneDoc source = SceneDoc.Of(SceneSpace.TwoD, [SceneEntity.Of(1, "A"), SceneEntity.Of(2, "B")]);
        var document = new SceneDocument("scene", source, doc => EditorKit.SceneEditorView(source: doc));
        var selection = new EditorSelectionService();
        int previousCalls = 0;
        document.View.OnSelectionChanged = _ => previousCalls++;
        var hierarchy = new SceneHierarchyController("scene", document, selection);

        selection.SelectEntities("scene", [1, 2], 2);
        Assert.Equal(2, document.View.SelectionCount);
        Assert.True(document.View.IsSelected(1));
        Assert.True(document.View.IsSelected(2));
        int beforeDispose = previousCalls;
        hierarchy.Dispose();
        document.View.SelectEntity(1);
        Assert.Equal(beforeDispose + 1, previousCalls);
    }

    [Fact]
    public void RealInputFocusKeyboardContextAndDragReparentUseHierarchySurface()
    {
        SceneDoc source = SceneDoc.Of(SceneSpace.TwoD, [SceneEntity.Of(1, "A"), SceneEntity.Of(2, "B")]);
        var document = new SceneDocument("scene", source, doc => EditorKit.SceneEditorView(source: doc));
        var controller = new SceneHierarchyController("scene", document, new EditorSelectionService());
        var view = new SceneHierarchyView(controller);
        using VectorFont font = VectorFont.LoadSystem();
        using var host = new UiHost(new RetainedCanvas(), font, 640, 600);
        host.SetRoot(view);
        Rect bounds = view.TreeBounds;

        host.PointerDown(bounds.X + 80, bounds.Y + 13);
        host.PointerUp(bounds.X + 80, bounds.Y + 13);
        Assert.True(view.TreeFocused);
        Assert.Equal(1, controller.SelectedEntityId);

        Assert.True(host.KeyDown(Key.Down));
        Assert.Equal(2, controller.SelectedEntityId);

        host.PointerDown(bounds.X + 80, bounds.Y + 39);
        host.PointerMove(bounds.X + 80, bounds.Y + 13);
        host.PointerUp(bounds.X + 80, bounds.Y + 13);
        Assert.Equal(2, Assert.Single(Assert.Single(controller.Build()).Children).EntityId);

        host.ContextClick(bounds.X + 80, bounds.Y + 13);
        Assert.Equal(1, view.ContextEntityId);
        controller.Dispose();
    }

    [Fact]
    public void FilterRebuildPreservesFocusAndControllerLifetimeIsExternal()
    {
        SceneDoc source = SceneDoc.Of(SceneSpace.TwoD, [SceneEntity.Of(1, "Root"), SceneEntity.Of(2, "Needle")]);
        var document = new SceneDocument("scene", source, doc => EditorKit.SceneEditorView(source: doc));
        var selection = new EditorSelectionService();
        var controller = new SceneHierarchyController("scene", document, selection);
        controller.Reparent(2, 1);
        var view = new SceneHierarchyView(controller);
        using VectorFont font = VectorFont.LoadSystem();
        using (var host = new UiHost(new RetainedCanvas(), font, 640, 600))
        {
            host.SetRoot(view);
            Rect bounds = view.TreeBounds;
            host.PointerDown(bounds.X + 80, bounds.Y + 13);
            host.PointerUp(bounds.X + 80, bounds.Y + 13);
            Assert.True(view.TreeFocused);

            view.SetFilter("needle");
            host.SetRoot(view);
            Assert.False(controller.IsDisposed);
            Assert.True(view.TreeFocused);
            Assert.True(host.KeyDown(Key.Down));
            Assert.Equal(2, controller.SelectedEntityId);
        }

        Assert.False(controller.IsDisposed);
        selection.SelectEntities("scene", [1], 1);
        Assert.True(document.View.IsSelected(1));
        controller.Dispose();
        Assert.True(controller.IsDisposed);
    }

    [Fact]
    public void OverlappingControllersDisposeWithoutRestoringStaleSelectionCallbacks()
    {
        SceneDoc source = SceneDoc.Of(SceneSpace.TwoD, [SceneEntity.Of(1, "A"), SceneEntity.Of(2, "B")]);
        var document = new SceneDocument("scene", source, doc => EditorKit.SceneEditorView(source: doc));
        var firstSelection = new EditorSelectionService();
        var secondSelection = new EditorSelectionService();
        int previousCalls = 0;
        document.View.OnSelectionChanged = _ => previousCalls++;
        var first = new SceneHierarchyController("scene", document, firstSelection);
        var second = new SceneHierarchyController("scene", document, secondSelection);

        first.Dispose();
        document.View.SelectEntity(2);
        Assert.True(firstSelection.Current.Value.IsEmpty);
        Assert.Equal(2, secondSelection.Current.Value.MainEntityId);
        Assert.Equal(1, previousCalls);

        second.Dispose();
        document.View.SelectEntity(1);
        Assert.Equal(2, previousCalls);
        Assert.Equal(2, secondSelection.Current.Value.MainEntityId);
    }

    [Fact]
    public void DeleteCleansSelectionAndFilterPreservesExpansionState()
    {
        SceneDoc source = SceneDoc.Of(SceneSpace.TwoD, [SceneEntity.Of(1, "Root"), SceneEntity.Of(2, "Needle")]);
        var document = new SceneDocument("scene", source, doc => EditorKit.SceneEditorView(source: doc));
        var selection = new EditorSelectionService();
        var hierarchy = new SceneHierarchyController("scene", document, selection);
        hierarchy.Reparent(2, 1);
        hierarchy.SetExpanded(1, true);
        hierarchy.Select(2);
        hierarchy.Filter = "needle";

        Assert.Single(Assert.Single(hierarchy.Build()).Children);
        Assert.Contains(1, hierarchy.Expanded);
        hierarchy.Delete([2]);
        Assert.True(selection.Current.Value.IsEmpty);
        Assert.Contains(1, hierarchy.Expanded);
    }
}
