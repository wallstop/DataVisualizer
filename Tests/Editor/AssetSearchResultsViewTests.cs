namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.Search;
    using WallstopStudios.DataVisualizer.Editor.UI;

    public sealed class AssetSearchResultsViewTests
    {
        private static List<(AssetSearchDocument Document, SearchResultMatchInfo Match)> MakeHits(int count)
        {
            List<(AssetSearchDocument Document, SearchResultMatchInfo Match)> hits = new();
            for (int index = 0; index < count; index++)
            {
                AssetSearchDocument document = new(index.ToString(), "Duplicate name with a long descriptive title", typeof(AssetSearchResultsViewTests), null);
                hits.Add((document, document.Match(new[] { "duplicate" })));
            }
            return hits;
        }

        [Test]
        public void ShouldAppendContinuationAndNavigateByGuidWithDuplicateNames()
        {
            AssetSearchDocument chosen = null;
            bool closed = false;
            AssetSearchResultsView view = new(document => chosen = document, () => { }, () => closed = true);
            var hits = MakeHits(30);
            Assert.That(view.SetResults(hits, "30 available matches"));
            view.ResultsList.selectedIndex = 29;
            hits.Add(MakeHits(31)[30]);
            Assert.That(view.SetResults(hits, "31 available matches"));
            Assert.AreEqual(31, view.ResultCount);
            Assert.That(!view.SetResults(hits, "31 available matches"));
            Assert.That(view.HandleNavigation(KeyCode.Return));
            Assert.AreEqual("29", chosen.Guid);
            Assert.That(view.HandleNavigation(KeyCode.Escape));
            Assert.That(closed);
        }

        [UnityTest]
        public IEnumerator ShouldKeepRealizedRowsBoundedAndNavigateARecycledLastResult()
        {
            using TestCleanupScope cleanup = new();
            LayoutTestWindow window = EditorWindow.CreateWindow<LayoutTestWindow>();
            cleanup.Defer(window.Close);
            window.position = new Rect(60, 60, 360, 480);
            window.rootVisualElement.style.width = 360;
            window.rootVisualElement.style.height = 480;
            window.ShowPopup();
            AssetSearchDocument chosen = null;
            bool wentBack = false;
            AssetSearchResultsView view = new(document => chosen = document, () => wentBack = true, () => { });
            window.rootVisualElement.Add(view);
            view.SetResults(MakeHits(1000), "1000 matches · Managed types");
            for (int frame = 0; frame < 5; frame++)
            {
                window.Repaint();
                yield return null;
            }
            Assert.That(view.ResultsList.Query<VisualElement>(className: "search-result-item").ToList().Count, Is.LessThan(40));
            Assert.That(view.worldBound.xMax, Is.LessThanOrEqualTo(window.rootVisualElement.worldBound.xMax + 1));
            view.ResultsList.ScrollToItem(999);
            view.ResultsList.selectedIndex = 999;
            for (int frame = 0; frame < 5; frame++)
            {
                window.Repaint();
                yield return null;
            }
            List<VisualElement> rows = view.ResultsList.Query<VisualElement>(className: "search-result-item").ToList();
            Assert.That(rows.Count, Is.LessThan(40));
            Assert.That(rows.Exists(row => row.userData is AssetSearchDocument document && document.Guid == "999"));
            using (KeyDownEvent enter = KeyDownEvent.GetPooled(Event.KeyboardEvent("return")))
            {
                enter.target = view.ResultsList;
                view.ResultsList.SendEvent(enter);
            }
            Assert.That(chosen != null);
            Assert.AreEqual("999", chosen.Guid);
            chosen = null;
            VisualElement last = rows.Find(row => row.userData is AssetSearchDocument document && document.Guid == "999");
            using (PointerDownEvent pointer = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0 }))
            {
                pointer.target = last;
                last.SendEvent(pointer);
            }
            Assert.That(chosen != null);
            Assert.AreEqual("999", chosen.Guid);
            chosen = null;
            Button back = view.Q<Button>("search-back-button");
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = back;
                back.SendEvent(submit);
            }
            Assert.That(wentBack);
            Assert.That(chosen == null);
        }

        [Test]
        public void ShouldRestoreSelectedGuidWhenItArrivesDuringContinuation()
        {
            AssetSearchResultsView view = new(_ => { }, () => { }, () => { });
            view.SetResults(MakeHits(40), "40 matches");
            view.ResultsList.selectedIndex = 30;
            var next = MakeHits(20);
            view.SetResults(next, "20 matches so far");
            Assert.AreEqual(-1, view.ResultsList.selectedIndex);
            next.AddRange(MakeHits(40).GetRange(20, 20));
            view.SetResults(next, "40 matches");
            Assert.AreEqual(30, view.ResultsList.selectedIndex);
        }

        [Test]
        public void ShouldPreserveGuidSelectionWhenResultsAreReplaced()
        {
            AssetSearchResultsView view = new(_ => { }, () => { }, () => { });
            var first = MakeHits(40);
            view.SetResults(first, "40 matches");
            view.ResultsList.selectedIndex = 30;
            var second = MakeHits(40);
            second.Reverse();
            view.SetResults(second, "40 matches");
            Assert.AreEqual(9, view.ResultsList.selectedIndex);
            view.SetResults(MakeHits(2), "2 available matches · Indexing…");
            Assert.AreEqual(-1, view.ResultsList.selectedIndex);
            Assert.That(!view.HandleNavigation(KeyCode.Return));
        }
    }
}
