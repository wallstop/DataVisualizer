namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;
    using PlayModeDataObject = WallstopStudios.DataVisualizer.Tests.Runtime.PlayModeDataObject;

    /*
        Manifest-driven documentation-image driver for #114. This is the automation the
        capture manifest slice owes the docs effort: it arranges a known window state
        (fixture assets, selected type and object, fixed splitter widths) and produces the
        documented shots through EditorSurfaceCapture, so imagery is regenerated from code
        instead of hand-staged screen captures.

        The driver never writes into docs/ by default; callers name an output directory and
        review the results before committing them. Fixture assets are created under
        Assets/DataVisualizerDocsCapture and deleted on every exit path, and the window
        preferences the arrangement touches are snapshotted and restored, mirroring the
        hygiene of EditorSurfaceCaptureTests.

        The window's Instance field, namespace-controller field, Cleanup, and SelectObject
        members are private or internal with no public equivalent, and Unity's compilation
        does not honor InternalsVisibleTo for the editor-to-tests assembly pair (see
        ObjectIdExtensions). They are reached reflectively, exactly like the merged
        EditorSurfaceCaptureTests do; a Unity rename fails closed here with an exception
        instead of silently capturing the wrong state.

        The SelectType object load is asynchronous; the three repaint/render cycles inside
        EditorSurfaceCapture.Capture give it time to populate on the capture host. With
        many thousands of assets a shot may show the list mid-load, which is visible in the
        result and acceptable for a driver whose output a human reviews before committing.
    */
    internal static class DocsImageCapture
    {
        private const string FixtureFolder = "Assets/DataVisualizerDocsCapture";
        private const float WindowWidth = 1180f;
        private const float WindowHeight = 780f;
        private const string DefaultOutputDirectory = "Temp/DocsImageCaptures";
        private const string OutputDirectoryArgument = "-docsImageOutputDir";
        private const string OutputDirectoryEnvironmentVariable = "DATAVISUALIZER_DOCS_IMAGE_DIR";

        private const string PrefsSplitterOuterKey =
            "WallstopStudios.Editor.DataVisualizer.SplitterOuterFixedPaneWidth";
        private const string PrefsSplitterInnerKey =
            "WallstopStudios.Editor.DataVisualizer.SplitterInnerFixedPaneWidth";
        private const string PrefsInitialSizeAppliedKey =
            "WallstopStudios.Editor.DataVisualizer.InitialSizeApplied";
        private const string PrefsPreferredWindowSizeKey =
            "WallstopStudios.Editor.DataVisualizer.PreferredWindowSize";
        private const string PrefsTemporaryWindowClampSizeKey =
            "WallstopStudios.Editor.DataVisualizer.TemporaryWindowClampSize";

        internal static IReadOnlyList<string> ShotNames => ManifestShotNames;

        private static readonly string[] FixtureTitles =
        {
            "Player Save",
            "World Settings",
            "Quest Log",
            "Inventory Pack",
            "Skill Tree",
            "Audio Bank",
            "Cutscene Director",
            "Damage Table",
        };

        private static readonly IReadOnlyList<string> ManifestShotNames = new[] { "layout" };

        private static readonly BindingFlags ReflectedInstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly BindingFlags ReflectedStaticMembers =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /*
            Batch-mode entry point for
            `unity -batchmode -quit -executeMethod
            WallstopStudios.DataVisualizer.Tests.Editor.DocsImageCapture.RunFromCommandLine`.
            The output directory comes from `-docsImageOutputDir <path>` or the
            DATAVISUALIZER_DOCS_IMAGE_DIR environment variable; failures throw so the
            batch exit code reports them.
        */
        public static void RunFromCommandLine()
        {
            string outputDirectory = ResolveOutputDirectory(
                Environment.GetCommandLineArgs(),
                Environment.GetEnvironmentVariable(OutputDirectoryEnvironmentVariable)
            );
            IReadOnlyList<EditorSurfaceCaptureResult> results = CaptureAll(outputDirectory);
            foreach (EditorSurfaceCaptureResult result in results)
            {
                Debug.Log(
                    $"[DocsImageCapture] {result.OutputPath}: {result.Width}x{result.Height}, "
                        + $"{result.ByteCount} bytes, {result.DistinctColorCount} distinct colors."
                );
            }
        }

        /*
            Captures every shot in the manifest into outputDirectory. Fails closed on the
            first error; completed shots stay on disk for inspection, and every teardown
            path still runs.
        */
        internal static IReadOnlyList<EditorSurfaceCaptureResult> CaptureAll(string outputDirectory)
        {
            ValidateOutputDirectory(outputDirectory);
            List<EditorSurfaceCaptureResult> results = new(ManifestShotNames.Count);
            foreach (string shotName in ManifestShotNames)
            {
                results.Add(CaptureShot(shotName, outputDirectory));
            }

            return results;
        }

        internal static EditorSurfaceCaptureResult CaptureShot(
            string shotName,
            string outputDirectory
        )
        {
            if (!IsKnownShot(shotName))
            {
                throw new ArgumentException(
                    $"Unknown shot '{shotName}'. Known shots: {string.Join(", ", ManifestShotNames)}.",
                    nameof(shotName)
                );
            }

            ValidateOutputDirectory(outputDirectory);
            if (!EditorSurfaceCapture.IsSupported)
            {
                throw new InvalidOperationException(
                    "Docs image capture needs a graphics device; this editor is running "
                        + "without one, so capture refuses to write a blank image."
                );
            }

            string fileName = $"data-visualizer-{shotName}.png";
            string capturePath = Path.Combine(outputDirectory, fileName);

            WindowPreferenceSnapshot preferences = WindowPreferenceSnapshot.Capture();
            FieldInfo instanceField = typeof(DataVisualizerWindow).GetField(
                "Instance",
                ReflectedStaticMembers
            );
            if (instanceField == null)
            {
                throw new InvalidOperationException(
                    "The DataVisualizer window exposes no Instance field; the capture "
                        + "driver cannot preserve the host's window instance."
                );
            }

            object previousInstance = instanceField.GetValue(null);
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            try
            {
                preferences.ApplyWidePanes();
                CreateFixtureAssets();

                EditorSurfaceCapture.ShowPopup(window);
                window.position = new Rect(40f, 40f, WindowWidth, WindowHeight);
                window.CreateGUI();
                SelectFixtureType(window);
                SelectObject(window, LoadFirstFixtureAsset());

                return EditorSurfaceCapture.Capture(window, capturePath);
            }
            finally
            {
                InvokeCleanup(window);
                EditorSurfaceCapture.CloseWindow(window);
                AssetGuidTypeIndex.Shared.Cancel();
                instanceField.SetValue(null, previousInstance);
                preferences.Restore();
                DeleteFixtureAssets();
            }
        }

        internal static string ResolveOutputDirectory(string[] arguments, string environmentValue)
        {
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (
                    string.Equals(
                        arguments[index],
                        OutputDirectoryArgument,
                        StringComparison.Ordinal
                    )
                )
                {
                    string value = arguments[index + 1];
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                return environmentValue;
            }

            return DefaultOutputDirectory;
        }

        private static bool IsKnownShot(string shotName)
        {
            for (int index = 0; index < ManifestShotNames.Count; index++)
            {
                if (string.Equals(ManifestShotNames[index], shotName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ValidateOutputDirectory(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException(
                    "Capture needs an output directory.",
                    nameof(outputDirectory)
                );
            }
        }

        private static void CreateFixtureAssets()
        {
            if (AssetDatabase.IsValidFolder(FixtureFolder))
            {
                AssetDatabase.DeleteAsset(FixtureFolder);
            }

            AssetDatabase.CreateFolder("Assets", Path.GetFileName(FixtureFolder));
            for (int index = 0; index < FixtureTitles.Length; index++)
            {
                PlayModeDataObject asset = ScriptableObject.CreateInstance<PlayModeDataObject>();
                AssetDatabase.CreateAsset(asset, $"{FixtureFolder}/{FixtureTitles[index]}.asset");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void DeleteFixtureAssets()
        {
            if (AssetDatabase.IsValidFolder(FixtureFolder))
            {
                AssetDatabase.DeleteAsset(FixtureFolder);
                AssetDatabase.Refresh();
            }
        }

        private static PlayModeDataObject LoadFirstFixtureAsset()
        {
            string assetPath = $"{FixtureFolder}/{FixtureTitles[0]}.asset";
            PlayModeDataObject asset = AssetDatabase.LoadAssetAtPath<PlayModeDataObject>(assetPath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"The fixture asset '{assetPath}' did not load; the window would "
                        + "capture an unarranged state."
                );
            }

            return asset;
        }

        private static void SelectFixtureType(DataVisualizerWindow window)
        {
            FieldInfo controllerField = typeof(DataVisualizerWindow).GetField(
                "_namespaceController",
                ReflectedInstanceMembers
            );
            object controller = controllerField?.GetValue(window);
            MethodInfo selectType = controllerField?.FieldType.GetMethod(
                "SelectType",
                ReflectedInstanceMembers,
                binder: null,
                types: new[] { typeof(DataVisualizerWindow), typeof(Type) },
                modifiers: null
            );
            if (controller == null || selectType == null)
            {
                throw new InvalidOperationException(
                    "The DataVisualizer window exposes no namespace controller SelectType "
                        + "member; the capture driver cannot select the fixture type."
                );
            }

            selectType.Invoke(controller, new object[] { window, typeof(PlayModeDataObject) });
        }

        private static void SelectObject(DataVisualizerWindow window, ScriptableObject asset)
        {
            MethodInfo selectObject = typeof(DataVisualizerWindow).GetMethod(
                "SelectObject",
                ReflectedInstanceMembers,
                binder: null,
                types: new[] { typeof(ScriptableObject) },
                modifiers: null
            );
            if (selectObject == null)
            {
                throw new InvalidOperationException(
                    "The DataVisualizer window exposes no SelectObject member; the capture "
                        + "driver cannot stage the inspector."
                );
            }

            selectObject.Invoke(window, new object[] { asset });
        }

        private static void InvokeCleanup(DataVisualizerWindow window)
        {
            MethodInfo cleanup = typeof(DataVisualizerWindow).GetMethod(
                "Cleanup",
                ReflectedInstanceMembers
            );
            cleanup?.Invoke(window, null);
        }

        /*
            Snapshot of the five window EditorPrefs keys the arrangement touches, restored
            on Dispose so a capture leaves the host's saved window state untouched.
        */
        private sealed class WindowPreferenceSnapshot
        {
            private readonly bool _hadOuter;
            private readonly float _outer;
            private readonly bool _hadInner;
            private readonly float _inner;
            private readonly bool _hadInitialSizeApplied;
            private readonly bool _initialSizeApplied;
            private readonly bool _hadPreferredWindowSize;
            private readonly string _preferredWindowSize;
            private readonly bool _hadTemporaryWindowClampSize;
            private readonly string _temporaryWindowClampSize;

            private WindowPreferenceSnapshot(
                bool hadOuter,
                float outer,
                bool hadInner,
                float inner,
                bool hadInitialSizeApplied,
                bool initialSizeApplied,
                bool hadPreferredWindowSize,
                string preferredWindowSize,
                bool hadTemporaryWindowClampSize,
                string temporaryWindowClampSize
            )
            {
                _hadOuter = hadOuter;
                _outer = outer;
                _hadInner = hadInner;
                _inner = inner;
                _hadInitialSizeApplied = hadInitialSizeApplied;
                _initialSizeApplied = initialSizeApplied;
                _hadPreferredWindowSize = hadPreferredWindowSize;
                _preferredWindowSize = preferredWindowSize;
                _hadTemporaryWindowClampSize = hadTemporaryWindowClampSize;
                _temporaryWindowClampSize = temporaryWindowClampSize;
            }

            internal static WindowPreferenceSnapshot Capture()
            {
                return new WindowPreferenceSnapshot(
                    hadOuter: EditorPrefs.HasKey(PrefsSplitterOuterKey),
                    outer: EditorPrefs.GetFloat(PrefsSplitterOuterKey),
                    hadInner: EditorPrefs.HasKey(PrefsSplitterInnerKey),
                    inner: EditorPrefs.GetFloat(PrefsSplitterInnerKey),
                    hadInitialSizeApplied: EditorPrefs.HasKey(PrefsInitialSizeAppliedKey),
                    initialSizeApplied: EditorPrefs.GetBool(PrefsInitialSizeAppliedKey),
                    hadPreferredWindowSize: EditorPrefs.HasKey(PrefsPreferredWindowSizeKey),
                    preferredWindowSize: EditorPrefs.GetString(PrefsPreferredWindowSizeKey),
                    hadTemporaryWindowClampSize: EditorPrefs.HasKey(
                        PrefsTemporaryWindowClampSizeKey
                    ),
                    temporaryWindowClampSize: EditorPrefs.GetString(
                        PrefsTemporaryWindowClampSizeKey
                    )
                );
            }

            private static void RestoreFloat(string key, bool existed, float value)
            {
                if (existed)
                {
                    EditorPrefs.SetFloat(key, value);
                }
                else
                {
                    EditorPrefs.DeleteKey(key);
                }
            }

            private static void RestoreString(string key, bool existed, string value)
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

            /*
                The docs layout needs wide namespace and object panes, so the saved splitter
                widths are set before the window builds its UI; the window's own one-time
                sizing keys are cleared so the fixed capture rectangle survives.
            */
            internal void ApplyWidePanes()
            {
                EditorPrefs.SetFloat(PrefsSplitterOuterKey, 330f);
                EditorPrefs.SetFloat(PrefsSplitterInnerKey, 620f);
                EditorPrefs.DeleteKey(PrefsInitialSizeAppliedKey);
                EditorPrefs.DeleteKey(PrefsPreferredWindowSizeKey);
                EditorPrefs.DeleteKey(PrefsTemporaryWindowClampSizeKey);
            }

            internal void Restore()
            {
                RestoreFloat(PrefsSplitterOuterKey, _hadOuter, _outer);
                RestoreFloat(PrefsSplitterInnerKey, _hadInner, _inner);
                if (_hadInitialSizeApplied)
                {
                    EditorPrefs.SetBool(PrefsInitialSizeAppliedKey, _initialSizeApplied);
                }
                else
                {
                    EditorPrefs.DeleteKey(PrefsInitialSizeAppliedKey);
                }

                RestoreString(
                    PrefsPreferredWindowSizeKey,
                    _hadPreferredWindowSize,
                    _preferredWindowSize
                );
                RestoreString(
                    PrefsTemporaryWindowClampSizeKey,
                    _hadTemporaryWindowClampSize,
                    _temporaryWindowClampSize
                );
            }
        }
    }
}
