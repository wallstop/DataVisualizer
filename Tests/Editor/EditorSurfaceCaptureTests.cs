namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.IO;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    internal sealed class EditorSurfaceCaptureTests
    {
        private const int PngSignatureLength = 8;

        /*
            The window keys a popup-hosted DataVisualizer may rewrite through its own
            splitter-width flush and one-time sizing path. Snapshot and restore them so a
            capture leaves the host's saved window state untouched, mirroring the hygiene of
            SplitterWidthPersistenceTests.
        */
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

        private static string CreateCapturePath(string fileName)
        {
            return Path.Combine("Temp", "DataVisualizerCaptures", fileName);
        }

        private static void DeferRestoreWindowPreferences(TestCleanupScope cleanup)
        {
            bool hadOuter = EditorPrefs.HasKey(PrefsSplitterOuterKey);
            float outer = hadOuter ? EditorPrefs.GetFloat(PrefsSplitterOuterKey) : 0f;
            bool hadInner = EditorPrefs.HasKey(PrefsSplitterInnerKey);
            float inner = hadInner ? EditorPrefs.GetFloat(PrefsSplitterInnerKey) : 0f;
            bool hadInitialSizeApplied = EditorPrefs.HasKey(PrefsInitialSizeAppliedKey);
            bool initialSizeApplied =
                hadInitialSizeApplied && EditorPrefs.GetBool(PrefsInitialSizeAppliedKey);
            bool hadPreferredWindowSize = EditorPrefs.HasKey(PrefsPreferredWindowSizeKey);
            string preferredWindowSize = hadPreferredWindowSize
                ? EditorPrefs.GetString(PrefsPreferredWindowSizeKey)
                : null;
            bool hadTemporaryWindowClampSize = EditorPrefs.HasKey(PrefsTemporaryWindowClampSizeKey);
            string temporaryWindowClampSize = hadTemporaryWindowClampSize
                ? EditorPrefs.GetString(PrefsTemporaryWindowClampSizeKey)
                : null;

            cleanup.Defer(() =>
            {
                RestoreFloatPreference(PrefsSplitterOuterKey, hadOuter, outer);
                RestoreFloatPreference(PrefsSplitterInnerKey, hadInner, inner);
                if (hadInitialSizeApplied)
                {
                    EditorPrefs.SetBool(PrefsInitialSizeAppliedKey, initialSizeApplied);
                }
                else
                {
                    EditorPrefs.DeleteKey(PrefsInitialSizeAppliedKey);
                }

                RestoreStringPreference(
                    PrefsPreferredWindowSizeKey,
                    hadPreferredWindowSize,
                    preferredWindowSize
                );
                RestoreStringPreference(
                    PrefsTemporaryWindowClampSizeKey,
                    hadTemporaryWindowClampSize,
                    temporaryWindowClampSize
                );
            });
        }

        private static void RestoreFloatPreference(string key, bool existed, float value)
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

        private static void AddDeterministicSurface(VisualElement root)
        {
            root.style.backgroundColor = new Color(0.12f, 0.14f, 0.18f, 1f);
            AddSwatch(root, new Color(0.85f, 0.33f, 0.25f, 1f), 24f, 24f, 96f, 64f);
            AddSwatch(root, new Color(0.3f, 0.69f, 0.31f, 1f), 152f, 24f, 96f, 64f);
            AddSwatch(root, new Color(0.16f, 0.5f, 0.73f, 1f), 24f, 112f, 96f, 64f);
        }

        private static void AddSwatch(
            VisualElement root,
            Color color,
            float left,
            float top,
            float width,
            float height
        )
        {
            VisualElement swatch = new()
            {
                style =
                {
                    position = Position.Absolute,
                    left = left,
                    top = top,
                    width = width,
                    height = height,
                    backgroundColor = color,
                },
            };
            root.Add(swatch);
        }

        private static void AssertValidPngFile(string path, EditorSurfaceCaptureResult result)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Assert.That(
                bytes.Length,
                Is.GreaterThan(PngSignatureLength + 16),
                $"A captured PNG must carry its signature and IHDR header, got {bytes.Length} bytes."
            );
            byte[] signature = { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
            for (int index = 0; index < PngSignatureLength; index++)
            {
                Assert.That(
                    bytes[index],
                    Is.EqualTo(signature[index]),
                    "Captured files must be real PNGs."
                );
            }

            int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            Assert.AreEqual(
                result.Width,
                width,
                "The PNG IHDR width must match the captured surface."
            );
            Assert.AreEqual(
                result.Height,
                height,
                "The PNG IHDR height must match the captured surface."
            );
            Assert.AreEqual(
                result.ByteCount,
                bytes.Length,
                "Every encoded byte must reach the file."
            );
        }

        private static void InvokeCleanup(DataVisualizerWindow window)
        {
            MethodInfo cleanupMethod = typeof(DataVisualizerWindow).GetMethod(
                "Cleanup",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(cleanupMethod != null, "The window Cleanup method must exist.");
            cleanupMethod.Invoke(window, Array.Empty<object>());
        }

        [Test]
        public void ShouldFailClosedWhenWindowIsNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                EditorSurfaceCapture.Capture(null, "Temp/x.png")
            );
        }

        [Test]
        public void ShouldFailClosedWhenOutputPathIsNullOrWhitespace(
            [Values(null, "", "   ")] string outputPath
        )
        {
            EditorWindow window = ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            try
            {
                Assert.Throws<ArgumentException>(() =>
                    EditorSurfaceCapture.Capture(window, outputPath)
                );
            }
            finally
            {
                EditorSurfaceCapture.CloseWindow(window);
            }
        }

        [Test]
        public void ShouldCaptureDeterministicSurfaceWithIdenticalBytesAcrossRuns()
        {
            if (!EditorSurfaceCapture.IsSupported)
            {
                Assert.Ignore("Offscreen capture needs a graphics device.");
            }

            using TestCleanupScope cleanup = new();
            string firstPath = CreateCapturePath("deterministic-first.png");
            string secondPath = CreateCapturePath("deterministic-second.png");
            EditorSurfaceCaptureHostWindow window =
                ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            window.position = new Rect(0f, 0f, 320f, 200f);
            EditorSurfaceCapture.ShowPopup(window);
            AddDeterministicSurface(window.rootVisualElement);

            EditorSurfaceCaptureResult first = EditorSurfaceCapture.Capture(window, firstPath);
            EditorSurfaceCaptureResult second = EditorSurfaceCapture.Capture(window, secondPath);

            AssertValidPngFile(firstPath, first);
            AssertValidPngFile(secondPath, second);
            Assert.AreEqual(
                first.Width,
                second.Width,
                "Repeated captures of one static surface must agree on width."
            );
            Assert.AreEqual(
                first.Height,
                second.Height,
                "Repeated captures of one static surface must agree on height."
            );
            Assert.That(
                File.ReadAllBytes(secondPath),
                Is.EqualTo(File.ReadAllBytes(firstPath)),
                "Repeated captures of one static surface must be byte-identical."
            );
            Assert.That(
                first.DistinctColorCount,
                Is.AtLeast(4),
                "The captured surface must show the background plus the placed swatches."
            );
        }

        [Test]
        public void ShouldCaptureDataVisualizerWindowToValidNonBlankPng()
        {
            if (!EditorSurfaceCapture.IsSupported)
            {
                Assert.Ignore("Offscreen capture needs a graphics device.");
            }

            FieldInfo instanceField = typeof(DataVisualizerWindow).GetField(
                "Instance",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Assert.That(instanceField != null, "The window Instance field must exist.");
            object previousInstance = instanceField.GetValue(null);
            using TestCleanupScope cleanup = new();
            DeferRestoreWindowPreferences(cleanup);
            cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
            cleanup.Defer(() => instanceField.SetValue(null, previousInstance));
            string outputPath = CreateCapturePath("data-visualizer-window.png");
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));

            EditorSurfaceCapture.ShowPopup(window);
            window.CreateGUI();
            EditorSurfaceCaptureResult result = EditorSurfaceCapture.Capture(window, outputPath);

            AssertValidPngFile(outputPath, result);
            Assert.That(
                result.DistinctColorCount,
                Is.GreaterThan(1),
                "The captured window must be non-blank; a single distinct color means the "
                    + "panel never painted and the capture is useless for documentation."
            );
            InvokeCleanup(window);
        }
    }
}
