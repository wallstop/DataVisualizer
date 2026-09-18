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
        private const string InitialSizeAppliedKey =
            "WallstopStudios.Editor.DataVisualizer.InitialSizeApplied";
        private const string PreferredWindowSizeKey =
            "WallstopStudios.Editor.DataVisualizer.PreferredWindowSize";
        private const string TemporaryWindowClampSizeKey =
            "WallstopStudios.Editor.DataVisualizer.TemporaryWindowClampSize";
        private const float TestOuterWidth = 350f;
        private const float TestInnerWidth = 250f;
        private const float ChangedOuterWidth = 330f;
        private const float PaneWidthTolerance = 0.01f;
        private const float LayoutTimeoutSeconds = 5f;
        private const float DebounceTimeoutSeconds = 2f;
        private static readonly Rect FloatingWindowRect = new(100f, 100f, 1200f, 800f);

        private DataVisualizerWindow _window;
        private TwoPaneSplitView _outerSplitView;
        private bool _hadOriginalOuter;
        private float _originalOuter;
        private bool _hadOriginalInner;
        private float _originalInner;
        private bool _hadInitialSizeApplied;
        private bool _originalInitialSizeApplied;
        private bool _hadPreferredWindowSize;
        private string _originalPreferredWindowSize;
        private bool _hadTemporaryWindowClampSize;
        private string _originalTemporaryWindowClampSize;

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
            Assert.That(
                setFixedPaneDimension != null,
                "TwoPaneSplitView.SetFixedPaneDimension is required to drive a pane-width change."
            );
            setFixedPaneDimension.Invoke(splitView, new object[] { width });
        }

        private static void RestoreStringPreference(string key, bool existed, string value)
        {
            if (existed)
            {
                EditorPrefs.SetString(key, value);
            }
            else
            {
                EditorPrefs.DeleteKey(key);
            }
        }

        private static IEnumerator WaitForSavedWidth(float expectedWidth)
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

        [UnityTest]
        public IEnumerator ShouldPersistSplitterWidthChangeAfterDebounce()
        {
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(RestoreSplitterPreferences);
                cleanup.Defer(CloseLayoutTestWindows);
                cleanup.Defer(CloseDataVisualizerWindows);

                yield return OpenSplitterWindow(floating: false);

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
        public IEnumerator ShouldKeepUnchangedPaneWidthWhenOnlyOnePaneChanges()
        {
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(RestoreSplitterPreferences);
                cleanup.Defer(CloseLayoutTestWindows);
                cleanup.Defer(CloseDataVisualizerWindows);

                yield return OpenSplitterWindow(floating: true);
                yield return WaitForPersistedWidthsToMatchResolved();
                float innerBefore = EditorPrefs.GetFloat(SplitterInnerKey);

                ChangePaneWidth(_outerSplitView, ChangedOuterWidth);
                yield return WaitForPaneWidth(ChangedOuterWidth);
                yield return WaitForSavedWidth(ChangedOuterWidth);

                Assert.AreEqual(
                    innerBefore,
                    EditorPrefs.GetFloat(SplitterInnerKey),
                    "a save triggered by one pane change must not rewrite the unchanged sibling width"
                );
            }
        }

        [UnityTest]
        public IEnumerator ShouldNotPersistStaleStageWhenPaneWidthRevertsToSavedValue()
        {
            using (TestCleanupScope cleanup = new())
            {
                cleanup.Defer(RestoreSplitterPreferences);
                cleanup.Defer(CloseLayoutTestWindows);
                cleanup.Defer(CloseDataVisualizerWindows);

                yield return OpenSplitterWindow(floating: false);

                ChangePaneWidth(_outerSplitView, ChangedOuterWidth);
                yield return WaitForPaneWidth(ChangedOuterWidth);
                ChangePaneWidth(_outerSplitView, TestOuterWidth);
                yield return WaitForPaneWidth(TestOuterWidth);

                float settledAt = (float)EditorApplication.timeSinceStartup;
                while (
                    (float)EditorApplication.timeSinceStartup < settledAt + DebounceTimeoutSeconds
                )
                {
                    yield return null;
                }

                Assert.AreEqual(
                    TestOuterWidth,
                    EditorPrefs.GetFloat(SplitterOuterKey),
                    "a pane width that settles back to the saved value must cancel the pending save"
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

                yield return OpenSplitterWindow(floating: false);

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

        private IEnumerator OpenSplitterWindow(bool floating)
        {
            CloseDataVisualizerWindows();
            CloseLayoutTestWindows();
            _hadOriginalOuter = EditorPrefs.HasKey(SplitterOuterKey);
            _originalOuter = _hadOriginalOuter ? EditorPrefs.GetFloat(SplitterOuterKey) : 0f;
            _hadOriginalInner = EditorPrefs.HasKey(SplitterInnerKey);
            _originalInner = _hadOriginalInner ? EditorPrefs.GetFloat(SplitterInnerKey) : 0f;
            _hadInitialSizeApplied = EditorPrefs.HasKey(InitialSizeAppliedKey);
            _originalInitialSizeApplied =
                _hadInitialSizeApplied && EditorPrefs.GetBool(InitialSizeAppliedKey);
            _hadPreferredWindowSize = EditorPrefs.HasKey(PreferredWindowSizeKey);
            _originalPreferredWindowSize = _hadPreferredWindowSize
                ? EditorPrefs.GetString(PreferredWindowSizeKey)
                : null;
            _hadTemporaryWindowClampSize = EditorPrefs.HasKey(TemporaryWindowClampSizeKey);
            _originalTemporaryWindowClampSize = _hadTemporaryWindowClampSize
                ? EditorPrefs.GetString(TemporaryWindowClampSizeKey)
                : null;
            EditorPrefs.SetFloat(SplitterOuterKey, TestOuterWidth);
            EditorPrefs.SetFloat(SplitterInnerKey, TestInnerWidth);

            if (floating)
            {
                _window = EditorWindow.GetWindow<DataVisualizerWindow>("Data Visualizer", false);
                _window.position = FloatingWindowRect;
            }
            else
            {
                EditorWindow.GetWindow<LayoutTestWindow>("Data Visualizer Test Anchor");
                _window = EditorWindow.GetWindow<DataVisualizerWindow>(
                    "Data Visualizer",
                    false,
                    typeof(LayoutTestWindow)
                );
            }

            yield return WaitForSplitViews();
        }

        private IEnumerator WaitForPersistedWidthsToMatchResolved()
        {
            float deadline = (float)EditorApplication.timeSinceStartup + DebounceTimeoutSeconds;
            while ((float)EditorApplication.timeSinceStartup < deadline)
            {
                yield return null;

                if (
                    IsPersistedWidthToResolved(SplitterOuterKey)
                    && IsPersistedWidthToInnerResolved()
                )
                {
                    yield break;
                }
            }
        }

        private bool IsPersistedWidthToInnerResolved()
        {
            TwoPaneSplitView innerSplitView = _outerSplitView.flexedPane as TwoPaneSplitView;
            if (innerSplitView == null || innerSplitView.fixedPane == null)
            {
                return false;
            }

            return Mathf.Approximately(
                EditorPrefs.GetFloat(SplitterInnerKey),
                innerSplitView.fixedPane.resolvedStyle.width
            );
        }

        private bool IsPersistedWidthToResolved(string preferenceKey)
        {
            return Mathf.Approximately(
                EditorPrefs.GetFloat(preferenceKey),
                _outerSplitView.fixedPane.resolvedStyle.width
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

            RestoreStringPreference(
                PreferredWindowSizeKey,
                _hadPreferredWindowSize,
                _originalPreferredWindowSize
            );
            RestoreStringPreference(
                TemporaryWindowClampSizeKey,
                _hadTemporaryWindowClampSize,
                _originalTemporaryWindowClampSize
            );
            if (_hadInitialSizeApplied)
            {
                EditorPrefs.SetBool(InitialSizeAppliedKey, _originalInitialSizeApplied);
            }
            else
            {
                EditorPrefs.DeleteKey(InitialSizeAppliedKey);
            }
        }

        private IEnumerator WaitForSplitViews()
        {
            FieldInfo outerField = typeof(DataVisualizerWindow).GetField(
                "_outerSplitView",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(
                outerField != null,
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
    }
}
