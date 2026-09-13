namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System.Collections;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    public sealed class SplitterWidthPersistenceTests
    {
        private const string SplitterOuterKey =
            "WallstopStudios.Editor.DataVisualizer.SplitterOuterFixedPaneWidth";
        private const string SplitterInnerKey =
            "WallstopStudios.Editor.DataVisualizer.SplitterInnerFixedPaneWidth";
        private const float TestOuterWidth = 350f;
        private const float TestInnerWidth = 250f;
        private const float ChangedOuterWidth = 330f;
        private const float PaneWidthTolerance = 0.01f;
        private const float LayoutTimeoutSeconds = 5f;
        private const float DebounceTimeoutSeconds = 2f;

        private DataVisualizerWindow _window;
        private TwoPaneSplitView _outerSplitView;
        private bool _hadOriginalOuter;
        private float _originalOuter;
        private bool _hadOriginalInner;
        private float _originalInner;

        private static void CloseDataVisualizerWindows()
        {
            foreach (
                DataVisualizerWindow window in Resources.FindObjectsOfTypeAll<DataVisualizerWindow>()
            )
            {
                window.Close();
            }
        }

        private static void CloseLayoutTestWindows()
        {
            foreach (LayoutTestWindow window in Resources.FindObjectsOfTypeAll<LayoutTestWindow>())
            {
                window.Close();
            }
        }

        private static void ChangePaneWidth(TwoPaneSplitView splitView, float width)
        {
            MethodInfo setFixedPaneDimension = typeof(TwoPaneSplitView).GetMethod(
                "SetFixedPaneDimension",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.IsNotNull(
                setFixedPaneDimension,
                "TwoPaneSplitView.SetFixedPaneDimension is required to drive a pane-width change."
            );
            setFixedPaneDimension.Invoke(splitView, new object[] { width });
        }

        [UnityTest]
        public IEnumerator ShouldPersistSplitterWidthChangeAfterDebounce()
        {
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(RestoreSplitterPreferences);
                cleanup.Defer(CloseLayoutTestWindows);
                cleanup.Defer(CloseDataVisualizerWindows);

                yield return OpenSplitterWindow();

                Assert.AreEqual(
                    TestOuterWidth,
                    EditorPrefs.GetFloat(SplitterOuterKey),
                    "the restored width must not be rewritten before a pane change"
                );

                ChangePaneWidth(_outerSplitView, ChangedOuterWidth);
                yield return WaitForPaneWidth(ChangedOuterWidth);
                yield return WaitForSavedWidth(ChangedOuterWidth);

                Assert.That(
                    EditorPrefs.GetFloat(SplitterOuterKey),
                    Is.EqualTo(ChangedOuterWidth).Within(PaneWidthTolerance),
                    "the changed outer pane width must persist once the debounce settles"
                );
            }
        }

        [UnityTest]
        public IEnumerator ShouldFlushPendingSplitterWidthChangeWhenWindowDisables()
        {
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(RestoreSplitterPreferences);
                cleanup.Defer(CloseLayoutTestWindows);
                cleanup.Defer(CloseDataVisualizerWindows);

                yield return OpenSplitterWindow();

                ChangePaneWidth(_outerSplitView, ChangedOuterWidth);
                yield return null;
                yield return null;

                Assert.AreEqual(
                    TestOuterWidth,
                    EditorPrefs.GetFloat(SplitterOuterKey),
                    "the changed width must still be pending inside the debounce window"
                );

                CloseDataVisualizerWindows();
                yield return null;

                Assert.That(
                    EditorPrefs.GetFloat(SplitterOuterKey),
                    Is.EqualTo(ChangedOuterWidth).Within(PaneWidthTolerance),
                    "closing the window inside the debounce window must flush the pending width"
                );
            }
        }

        private IEnumerator OpenSplitterWindow()
        {
            CloseDataVisualizerWindows();
            CloseLayoutTestWindows();
            _hadOriginalOuter = EditorPrefs.HasKey(SplitterOuterKey);
            _originalOuter = _hadOriginalOuter ? EditorPrefs.GetFloat(SplitterOuterKey) : 0f;
            _hadOriginalInner = EditorPrefs.HasKey(SplitterInnerKey);
            _originalInner = _hadOriginalInner ? EditorPrefs.GetFloat(SplitterInnerKey) : 0f;
            EditorPrefs.SetFloat(SplitterOuterKey, TestOuterWidth);
            EditorPrefs.SetFloat(SplitterInnerKey, TestInnerWidth);

            EditorWindow.GetWindow<LayoutTestWindow>("Data Visualizer Test Anchor");
            _window = EditorWindow.GetWindow<DataVisualizerWindow>(
                "Data Visualizer",
                false,
                typeof(LayoutTestWindow)
            );

            yield return WaitForSplitViews();
        }

        private IEnumerator WaitForSplitViews()
        {
            FieldInfo outerField = typeof(DataVisualizerWindow).GetField(
                "_outerSplitView",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.IsNotNull(
                outerField,
                "DataVisualizer must declare the '_outerSplitView' field."
            );

            float deadline = (float)EditorApplication.timeSinceStartup + LayoutTimeoutSeconds;
            while ((float)EditorApplication.timeSinceStartup < deadline)
            {
                yield return null;

                _outerSplitView = (TwoPaneSplitView)outerField.GetValue(_window);
                if (
                    _outerSplitView != null
                    && _outerSplitView.fixedPane != null
                    && Mathf.Approximately(
                        _outerSplitView.fixedPane.resolvedStyle.width,
                        TestOuterWidth
                    )
                )
                {
                    yield break;
                }
            }

            Assert.Fail(
                $"The split views never settled at the restored outer width {TestOuterWidth}; "
                    + $"last resolved width was {_outerSplitView?.fixedPane?.resolvedStyle.width}."
            );
        }

        private IEnumerator WaitForPaneWidth(float expectedWidth)
        {
            VisualElement fixedPane = _outerSplitView.fixedPane;
            float deadline = (float)EditorApplication.timeSinceStartup + DebounceTimeoutSeconds;
            while ((float)EditorApplication.timeSinceStartup < deadline)
            {
                yield return null;

                if (Mathf.Approximately(fixedPane.resolvedStyle.width, expectedWidth))
                {
                    yield break;
                }
            }

            Assert.Fail(
                $"The fixed pane width never settled at {expectedWidth}; "
                    + $"last resolved width was {fixedPane.resolvedStyle.width}."
            );
        }

        private IEnumerator WaitForSavedWidth(float expectedWidth)
        {
            float deadline = (float)EditorApplication.timeSinceStartup + DebounceTimeoutSeconds;
            while (
                (float)EditorApplication.timeSinceStartup < deadline
                && !Mathf.Approximately(EditorPrefs.GetFloat(SplitterOuterKey), expectedWidth)
            )
            {
                yield return null;
            }

            Assert.That(
                EditorPrefs.GetFloat(SplitterOuterKey),
                Is.EqualTo(expectedWidth).Within(PaneWidthTolerance),
                "the debounced save must persist the changed pane width"
            );
        }

        private void RestoreSplitterPreferences()
        {
            if (_hadOriginalOuter)
            {
                EditorPrefs.SetFloat(SplitterOuterKey, _originalOuter);
            }
            else
            {
                EditorPrefs.DeleteKey(SplitterOuterKey);
            }

            if (_hadOriginalInner)
            {
                EditorPrefs.SetFloat(SplitterInnerKey, _originalInner);
            }
            else
            {
                EditorPrefs.DeleteKey(SplitterInnerKey);
            }
        }
    }
}
