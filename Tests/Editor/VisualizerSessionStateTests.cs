namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using State;

    [TestFixture]
    public sealed class VisualizerSessionStateTests
    {
        [Test]
        public void SetSelectedNamespaceWhenDifferentValueReturnsTrue()
        {
            VisualizerSessionState state = new VisualizerSessionState();

            bool changed = state.Selection.SetSelectedNamespace("Namespace1");

            Assert.IsTrue(changed);
            Assert.AreEqual("Namespace1", state.Selection.SelectedNamespaceKey);
        }

        [Test]
        public void SetSelectedNamespaceWhenSameValueReturnsFalse()
        {
            VisualizerSessionState state = new VisualizerSessionState();
            state.Selection.SetSelectedNamespace("Namespace1");

            bool changed = state.Selection.SetSelectedNamespace("Namespace1");

            Assert.IsFalse(changed);
        }

        [Test]
        public void SetSelectedObjectsWhenSequenceMatchesReturnsFalse()
        {
            VisualizerSessionState state = new VisualizerSessionState();
            List<string> initialGuids = new List<string> { "GuidA", "GuidB" };
            state.Selection.SetSelectedObjects(initialGuids);

            bool changed = state.Selection.SetSelectedObjects(initialGuids);

            Assert.IsFalse(changed);
            Assert.AreEqual(2, state.Selection.SelectedObjectGuids.Count);
        }

        [Test]
        public void SetSelectedObjectsWhenSequenceChangesReturnsTrue()
        {
            VisualizerSessionState state = new VisualizerSessionState();
            List<string> initialGuids = new List<string> { "GuidA", "GuidB" };
            state.Selection.SetSelectedObjects(initialGuids);

            List<string> updatedGuids = new List<string> { "GuidA", "GuidC" };
            bool changed = state.Selection.SetSelectedObjects(updatedGuids);

            Assert.IsTrue(changed);
            Assert.AreEqual("GuidC", state.Selection.SelectedObjectGuids[1]);
        }

        [Test]
        public void SetNamespaceCollapsedWhenCollapsingNewNamespaceReturnsTrue()
        {
            VisualizerSessionState state = new VisualizerSessionState();

            bool changed = state.Selection.SetNamespaceCollapsed("Namespace1", true);

            Assert.IsTrue(changed);
            Assert.Contains("Namespace1", new List<string>(state.Selection.CollapsedNamespaces));
        }

        [Test]
        public void SetNamespaceCollapsedWhenExpandingCollapsedNamespaceReturnsTrue()
        {
            VisualizerSessionState state = new VisualizerSessionState();
            state.Selection.SetNamespaceCollapsed("Namespace1", true);

            bool changed = state.Selection.SetNamespaceCollapsed("Namespace1", false);

            Assert.IsTrue(changed);
            Assert.IsFalse(state.Selection.CollapsedNamespaces.Contains("Namespace1"));
        }

        [Test]
        public void SetNamespaceCollapsedWhenExpandingNonCollapsedNamespaceReturnsFalse()
        {
            VisualizerSessionState state = new VisualizerSessionState();

            bool changed = state.Selection.SetNamespaceCollapsed("Namespace1", false);

            Assert.IsFalse(changed);
        }

        [Test]
        public void SetCurrentPageWhenNegativeValueNormalizesToZero()
        {
            VisualizerSessionState state = new VisualizerSessionState();

            state.Pagination.SetCurrentPage(-10);

            Assert.AreEqual(0, state.Pagination.CurrentPage);
        }

        [Test]
        public void SetItemsPerPageWhenSameValueReturnsFalse()
        {
            VisualizerSessionState state = new VisualizerSessionState();
            state.Pagination.SetItemsPerPage(10);

            bool changed = state.Pagination.SetItemsPerPage(10);

            Assert.IsFalse(changed);
        }

        [Test]
        public void SetQueryWhenSameValueReturnsFalse()
        {
            VisualizerSessionState state = new VisualizerSessionState();
            state.Search.SetQuery("SearchTerm");

            bool changed = state.Search.SetQuery("SearchTerm");

            Assert.IsFalse(changed);
        }

        [Test]
        public void SetHighlightIndexWhenValueDiffersReturnsTrue()
        {
            VisualizerSessionState state = new VisualizerSessionState();

            bool changed = state.Search.SetHighlightIndex(2);

            Assert.IsTrue(changed);
            Assert.AreEqual(2, state.Search.HighlightIndex);
        }

        [Test]
        public void SetActivePopoverWhenValueChangesClearsNestedPopover()
        {
            VisualizerSessionState state = new VisualizerSessionState();
            state.Popovers.SetActiveNestedPopover("Nested");

            bool changed = state.Popovers.SetActivePopover("Main");

            Assert.IsTrue(changed);
            Assert.AreEqual("Main", state.Popovers.ActivePopoverId);
            Assert.IsNull(state.Popovers.ActiveNestedPopoverId);
        }

        [Test]
        public void SetActivePopoverWhenSameValueReturnsFalse()
        {
            VisualizerSessionState state = new VisualizerSessionState();
            state.Popovers.SetActivePopover("Main");

            bool changed = state.Popovers.SetActivePopover("Main");

            Assert.IsFalse(changed);
        }

        [Test]
        public void SetAndLabelsWhenWhitespaceAndDuplicatesProvidedNormalizesList()
        {
            VisualizerSessionState state = new VisualizerSessionState();
            List<string> expected = new List<string> { "Alpha", "Beta" };

            bool changed = state.Labels.SetAndLabels(
                new List<string> { "Alpha", " ", null, "Beta" }
            );

            Assert.IsTrue(changed);
            CollectionAssert.AreEqual(expected, state.Labels.AndLabels);
        }

        [Test]
        public void SetAndLabelsWhenSameValuesProvidedReturnsFalse()
        {
            VisualizerSessionState state = new VisualizerSessionState();
            List<string> labels = new List<string> { "Alpha", "Beta" };
            state.Labels.SetAndLabels(labels);

            bool changed = state.Labels.SetAndLabels(labels);

            Assert.IsFalse(changed);
        }

        [Test]
        public void DragStateSetModifiersWhenInactiveKeepsFlagsCleared()
        {
            VisualizerSessionState state = new VisualizerSessionState();
            VisualizerSessionState.DragState dragState = state.Drag;

            bool changed = dragState.SetModifiers(true, true, true);

            Assert.IsFalse(changed);
            Assert.IsFalse(dragState.AltPressed);
            Assert.IsFalse(dragState.ControlPressed);
            Assert.IsFalse(dragState.ShiftPressed);
        }

        [Test]
        public void DragStateSetModifiersWhenActiveUpdatesFlags()
        {
            VisualizerSessionState state = new VisualizerSessionState();
            VisualizerSessionState.DragState dragState = state.Drag;
            dragState.SetOperation(VisualizerSessionState.DragState.DragOperationKind.Object);

            bool changed = dragState.SetModifiers(true, false, true);

            Assert.IsTrue(changed);
            Assert.IsTrue(dragState.AltPressed);
            Assert.IsFalse(dragState.ControlPressed);
            Assert.IsTrue(dragState.ShiftPressed);
        }

        [Test]
        public void DragStateSetOperationClearsModifiersWhenReset()
        {
            VisualizerSessionState state = new VisualizerSessionState();
            VisualizerSessionState.DragState dragState = state.Drag;
            dragState.SetOperation(VisualizerSessionState.DragState.DragOperationKind.Object);
            dragState.SetModifiers(true, true, true);

            bool changed = dragState.SetOperation(
                VisualizerSessionState.DragState.DragOperationKind.None
            );

            Assert.IsTrue(changed);
            Assert.IsFalse(dragState.AltPressed);
            Assert.IsFalse(dragState.ControlPressed);
            Assert.IsFalse(dragState.ShiftPressed);
        }
    }
}
