namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    public sealed class SearchObservationTests
    {
        [UnityTest]
        public IEnumerator ShouldObserveZeroMatchesAndRejectClearedOrDismissedQueries()
        {
            using TestCleanupScope cleanup = new();
            FieldInfo instance = typeof(DataVisualizerWindow).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = instance.GetValue(null);
            cleanup.Defer(() => instance.SetValue(null, previous));
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            EditorSurfaceCapture.ShowPopup(window);
            window.CreateGUI();
            Assert.That(window.IsActiveSearchComplete, Is.False);
            TextField field = window.rootVisualElement.Q<TextField>("global-search-field");
            string absent = Guid.NewGuid().ToString("N");
            field.value = absent;
            for (int tick = 0; tick < 1000 && !window.IsActiveSearchComplete; tick++)
            {
                window.AdvanceSearchQuery();
                yield return null;
            }
            Assert.That(window.IsActiveSearchComplete, Is.True);
            Assert.That(window.ActiveSearchText, Is.EqualTo(absent));
            Assert.That(window.ActiveSearchMatchCount, Is.Zero);
            Assert.That(window.ActiveSearchPreviewRowCount, Is.Zero);
            Assert.That(window.ActiveSearchReachedLimit, Is.False);
            Assert.That(window.ActiveSearchScannedCount, Is.EqualTo(window.ActiveSearchRecordCount));
            int generation = window.ActiveSearchGeneration;
            field.value = string.Empty;
            Assert.That(window.IsActiveSearchComplete, Is.False);
            Assert.That(window.ActiveSearchText == null);
            Assert.That(generation < window.ActiveSearchGeneration);
            field.value = absent;
            for (int tick = 0; tick < 1000 && !window.IsActiveSearchComplete; tick++)
            {
                window.AdvanceSearchQuery();
                yield return null;
            }
            Assert.That(window.IsActiveSearchComplete, Is.True);
            yield return null;
            using (KeyDownEvent escape = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
            {
                escape.target = window.rootVisualElement;
                window.rootVisualElement.SendEvent(escape);
            }
            yield return null;
            Assert.That(window.IsActiveSearchComplete, Is.False);
            Assert.That(window.ActiveSearchText == null);
        }
    }
}
