namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;
    using Object = UnityEngine.Object;

    internal sealed class DocsImageCaptureTests
    {
        private const int PngSignatureLength = 8;

        private static void AssertValidPngFile(EditorSurfaceCaptureResult result)
        {
            byte[] bytes = File.ReadAllBytes(result.OutputPath);
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
            Assert.That(
                result.DistinctColorCount,
                Is.GreaterThan(1),
                "The captured window must be non-blank; a single distinct color means the "
                    + "panel never painted and the capture is useless for documentation."
            );
        }

        private static void AssertRegionPainted(
            DocsImageCapture.CaptureRegionAnalysis analysis,
            string regionName,
            bool expected
        )
        {
            foreach (DocsImageCapture.CaptureRegionMetric metric in analysis.Metrics)
            {
                if (metric.Region.Name == regionName)
                {
                    Assert.That(
                        metric.Painted,
                        Is.EqualTo(expected),
                        $"Region '{regionName}' measured distinct={metric.DistinctColors}, "
                            + $"modalFraction={metric.ModalColorFraction:0.###}."
                    );
                    return;
                }
            }

            Assert.Fail($"The analysis carries no region named '{regionName}'.");
        }

        private static DocsImageCapture.CaptureRegionAnalysis BuildAnalysis(
            bool canaryPainted,
            bool gapHostRegionPainted,
            bool blankableRegionPainted
        )
        {
            return new DocsImageCapture.CaptureRegionAnalysis(
                new[]
                {
                    Metric(DocsImageCapture.CanaryRegionName, canaryPainted, false),
                    Metric("listview", gapHostRegionPainted, true),
                    Metric("bindable:m_Script", blankableRegionPainted, false),
                },
                canaryPainted
            );
        }

        private static DocsImageCapture.CaptureRegionMetric Metric(
            string regionName,
            bool painted,
            bool paintedOnGapHosts
        )
        {
            return new DocsImageCapture.CaptureRegionMetric(
                new DocsImageCapture.CaptureRegion(
                    regionName,
                    new Rect(0f, 0f, 10f, 10f),
                    paintedOnGapHosts
                ),
                painted ? 100 : 2,
                painted ? 0.5f : 1f
            );
        }

        private static void FillRect(
            Color32[] pixels,
            int width,
            int height,
            Rect uiRect,
            Color32 color
        )
        {
            int yStart = height - Mathf.RoundToInt(uiRect.yMax);
            int yEnd = height - Mathf.RoundToInt(uiRect.y);
            int xStart = Mathf.RoundToInt(uiRect.x);
            int xEnd = Mathf.RoundToInt(uiRect.xMax);
            for (int y = yStart; y < yEnd; y++)
            {
                for (int x = xStart; x < xEnd; x++)
                {
                    pixels[y * width + x] = color;
                }
            }
        }

        private static void FillVariedRect(Color32[] pixels, int width, int height, Rect uiRect)
        {
            int yStart = height - Mathf.RoundToInt(uiRect.yMax);
            int yEnd = height - Mathf.RoundToInt(uiRect.y);
            int xStart = Mathf.RoundToInt(uiRect.x);
            int xEnd = Mathf.RoundToInt(uiRect.xMax);
            for (int y = yStart; y < yEnd; y++)
            {
                for (int x = xStart; x < xEnd; x++)
                {
                    pixels[y * width + x] = new Color32(
                        (byte)(x * 7 % 256),
                        (byte)(y * 13 % 256),
                        (byte)((x + y) % 256),
                        255
                    );
                }
            }
        }

        private static byte[] EncodePixels(int width, int height, Color32[] pixels)
        {
            Texture2D texture = new(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            return png;
        }

        private static DocsImageCapture.CaptureRegion RegionOrNull(
            IReadOnlyList<DocsImageCapture.CaptureRegion> regions,
            string regionName
        )
        {
            foreach (DocsImageCapture.CaptureRegion region in regions)
            {
                if (region.Name == regionName)
                {
                    return region;
                }
            }

            return null;
        }

        [Test]
        public void ShouldExposeManifestShotNames()
        {
            Assert.That(
                DocsImageCapture.ShotNames,
                Is.EqualTo(new[] { "layout", "instance-actions", "create", "import", "settings" }),
                "The manifest must carry the README's documented shots."
            );
            for (int index = 0; index < DocsImageCapture.ShotNames.Count; index++)
            {
                Assert.That(
                    DocsImageCapture.ShotNames[index],
                    Is.Not.Empty.And.Not.Contains(' '),
                    "Shot names are file-name fragments and must not contain spaces."
                );
            }
        }

        [Test]
        public void ShouldFailClosedWhenShotIsUnknown(
            [Values(null, "", "hero", "Layout", "layout ")] string shotName
        )
        {
            Assert.Throws<ArgumentException>(() =>
                DocsImageCapture.CaptureShot(shotName, "Temp/DocsImageCaptures")
            );
        }

        [Test]
        public void ShouldFailClosedWhenOutputDirectoryIsInvalid(
            [Values(null, "", "   ")] string outputDirectory
        )
        {
            Assert.Throws<ArgumentException>(() =>
                DocsImageCapture.CaptureShot("layout", outputDirectory)
            );
            Assert.Throws<ArgumentException>(() => DocsImageCapture.CaptureAll(outputDirectory));
        }

        [TestCase(
            null,
            new string[0],
            "Temp/DocsImageCaptures",
            TestName = "Defaults without argument and environment"
        )]
        [TestCase(
            "env-value",
            new string[0],
            "env-value",
            TestName = "Environment value wins when no argument is present"
        )]
        [TestCase(
            "env-value",
            new[] { "-docsImageOutputDir", "Temp/FromArg" },
            "Temp/FromArg",
            TestName = "Argument wins over environment value"
        )]
        [TestCase(
            null,
            new[] { "-docsImageOutputDir", "Temp/FromArg" },
            "Temp/FromArg",
            TestName = "Argument wins without environment value"
        )]
        [TestCase(
            "env-value",
            new[] { "-docsImageOutputDir", "" },
            "env-value",
            TestName = "Empty argument value falls back to the environment value"
        )]
        [TestCase(
            null,
            new[] { "-docsImageOutputDir", "  " },
            "Temp/DocsImageCaptures",
            TestName = "Whitespace argument value falls back to the default"
        )]
        [TestCase(
            "env-value",
            new[] { "-docsImageOutputDir" },
            "env-value",
            TestName = "Argument without a value falls back to the environment value"
        )]
        public void ShouldResolveOutputDirectoryFromArgumentOverEnvironment(
            string environmentValue,
            string[] arguments,
            string expected
        )
        {
            Assert.That(
                DocsImageCapture.ResolveOutputDirectory(arguments, environmentValue),
                Is.EqualTo(expected)
            );
        }

        [Test]
        public void ShouldCaptureEveryManifestShotToValidNonBlankPng()
        {
            if (!EditorSurfaceCapture.IsSupported)
            {
                Assert.Ignore("Offscreen capture needs a graphics device.");
            }

            string outputDirectory = Path.Combine("Temp", "DocsImageCaptures");
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }

            /*
                CaptureAll owns the fixture assets, window preferences, and window lifecycle,
                so the test only restores the window Instance field that predates the run,
                like EditorSurfaceCaptureTests does around its real-window capture.
            */
            FieldInfo instanceField = typeof(DataVisualizerWindow).GetField(
                "Instance",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Assert.That(instanceField != null, "The window Instance field must exist.");
            object previousInstance = instanceField.GetValue(null);

            try
            {
                IReadOnlyList<EditorSurfaceCaptureResult> results = DocsImageCapture.CaptureAll(
                    outputDirectory
                );
                Assert.That(
                    results.Count,
                    Is.EqualTo(DocsImageCapture.ShotNames.Count),
                    "One result per manifest shot."
                );
                foreach (EditorSurfaceCaptureResult result in results)
                {
                    AssertValidPngFile(result);
                }
            }
            finally
            {
                instanceField.SetValue(null, previousInstance);
            }
        }

        [Test]
        public void ShouldClassifyUniformRegionAsUnpaintedAndVariedRegionAsPainted()
        {
            int width = 64;
            int height = 48;
            Color32[] pixels = new Color32[width * height];
            FillRect(
                pixels,
                width,
                height,
                new Rect(0f, 0f, width, height),
                new Color32(24, 24, 28, 255)
            );
            FillRect(
                pixels,
                width,
                height,
                new Rect(4f, 4f, 20f, 16f),
                new Color32(40, 40, 44, 255)
            );
            FillVariedRect(pixels, width, height, new Rect(30f, 4f, 24f, 16f));
            byte[] png = EncodePixels(width, height, pixels);

            List<DocsImageCapture.CaptureRegion> regions = new()
            {
                new("uniform", new Rect(4f, 4f, 20f, 16f), false),
                new("varied", new Rect(30f, 4f, 24f, 16f), false),
                new(DocsImageCapture.CanaryRegionName, new Rect(30f, 4f, 24f, 16f), false),
            };

            DocsImageCapture.CaptureRegionAnalysis analysis = DocsImageCapture.Analyze(
                png,
                new Rect(0f, 0f, width, height),
                regions
            );

            Assert.That(
                analysis.CanaryPainted,
                Is.True,
                "The canary region sits on varied pixels and must read as painted."
            );
            AssertRegionPainted(analysis, "uniform", false);
            AssertRegionPainted(analysis, "varied", true);
            AssertRegionPainted(analysis, DocsImageCapture.CanaryRegionName, true);
        }

        [Test]
        public void ShouldFailValidationWhenCapableHostLeavesExpectedRegionUnpainted()
        {
            DocsImageCapture.CaptureRegionAnalysis analysis = BuildAnalysis(
                canaryPainted: true,
                gapHostRegionPainted: true,
                blankableRegionPainted: false
            );

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                DocsImageCapture.ValidateRegionAnalysis(analysis)
            );
            Assert.That(
                exception.Message,
                Does.Contain("bindable:m_Script"),
                "The failure must name the region that did not paint."
            );
        }

        [Test]
        public void ShouldAssertOnlyGapHostPaintedRegionsWhenCanaryIsBlank(
            [Values(true, false)] bool gapHostRegionPainted
        )
        {
            DocsImageCapture.CaptureRegionAnalysis analysis = BuildAnalysis(
                canaryPainted: false,
                gapHostRegionPainted: gapHostRegionPainted,
                blankableRegionPainted: false
            );

            TestDelegate validate = () => DocsImageCapture.ValidateRegionAnalysis(analysis);
            if (gapHostRegionPainted)
            {
                Assert.DoesNotThrow(
                    validate,
                    "A gap host's known output stays reviewable when its painted regions hold."
                );
            }
            else
            {
                Assert.Throws<InvalidOperationException>(
                    validate,
                    "A blank regression in a region that paints even on gap hosts must fail."
                );
            }
        }

        [Test]
        public void ShouldTreatHiddenSubtreesAndZeroAreaElementsAsNotRendered()
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window =
                ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            window.position = new Rect(0f, 0f, 200f, 200f);
            EditorSurfaceCapture.ShowPopup(window);
            VisualElement root = window.rootVisualElement;

            VisualElement visible = new() { name = "visible-leaf" };
            visible.style.width = 20f;
            visible.style.height = 10f;
            root.Add(visible);

            VisualElement collapsedContainer = new();
            collapsedContainer.style.display = DisplayStyle.None;
            root.Add(collapsedContainer);
            VisualElement collapsedChild = new() { name = "collapsed-child" };
            collapsedChild.style.width = 20f;
            collapsedChild.style.height = 10f;
            collapsedContainer.Add(collapsedChild);

            VisualElement invisibleParent = new();
            invisibleParent.style.visibility = Visibility.Hidden;
            root.Add(invisibleParent);
            VisualElement invisibleChild = new() { name = "invisible-child" };
            invisibleChild.style.width = 20f;
            invisibleChild.style.height = 10f;
            invisibleParent.Add(invisibleChild);

            VisualElement zeroArea = new() { name = "zero-area" };
            zeroArea.style.width = 0f;
            zeroArea.style.height = 10f;
            root.Add(zeroArea);

            EditorSurfaceCapture.SettleLayout(window);

            Assert.That(
                DocsImageCapture.IsRenderedForCapture(visible),
                Is.True,
                "A laid-out visible element is expected to paint."
            );
            Assert.That(
                DocsImageCapture.IsRenderedForCapture(collapsedChild),
                Is.False,
                "A subtree under a display:none container is not rendered, whatever its own "
                    + "styles say."
            );
            Assert.That(
                DocsImageCapture.IsRenderedForCapture(invisibleChild),
                Is.False,
                "Visibility resolves per element, so an inherited hidden value means the "
                    + "element paints nothing."
            );
            Assert.That(
                DocsImageCapture.IsRenderedForCapture(zeroArea),
                Is.False,
                "A zero-area element has nothing to paint."
            );
        }

        [Test]
        public void ShouldCollectOnlyRenderedRegionsFromTheWindowTree()
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window =
                ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            window.position = new Rect(0f, 0f, 220f, 120f);
            EditorSurfaceCapture.ShowPopup(window);
            VisualElement root = window.rootVisualElement;

            VisualElement canary = new() { name = DocsImageCapture.CanaryRegionName };
            canary.style.width = 40f;
            canary.style.height = 12f;
            root.Add(canary);

            VisualElement collapsedContainer = new();
            collapsedContainer.style.display = DisplayStyle.None;
            root.Add(collapsedContainer);
            VisualElement hiddenSection = new() { name = "inspector-labels-section" };
            hiddenSection.style.width = 40f;
            hiddenSection.style.height = 12f;
            collapsedContainer.Add(hiddenSection);

            EditorSurfaceCapture.SettleLayout(window);

            IReadOnlyList<DocsImageCapture.CaptureRegion> regions = DocsImageCapture.CollectRegions(
                window,
                DocsImageCapture.LayoutShotName
            );
            Assert.That(
                regions,
                Has.Count.EqualTo(1),
                "Only the rendered canary is a region the capture must paint; the collapsed "
                    + "section has no pixels to verify."
            );
            DocsImageCapture.CaptureRegion collected = RegionOrNull(
                regions,
                DocsImageCapture.CanaryRegionName
            );
            Assert.That(
                collected != null,
                "The rendered canary is recorded with the region name the analyzer keys on."
            );
            Assert.That(
                collected.Rect,
                Is.EqualTo(canary.worldBound),
                "The recorded region carries the laid-out bounds the measurement maps into "
                    + "the PNG."
            );

            /*
                With every candidate hidden, the collection yields no canary and the
                analysis fails closed instead of asserting against an unarranged window.
            */
            canary.style.display = DisplayStyle.None;
            EditorSurfaceCapture.SettleLayout(window);
            IReadOnlyList<DocsImageCapture.CaptureRegion> hiddenRegions =
                DocsImageCapture.CollectRegions(window, DocsImageCapture.LayoutShotName);
            Assert.That(hiddenRegions, Is.Empty);

            Color32[] uniformPixels = new Color32[16];
            Color32 uniform = new(24, 24, 28, 255);
            for (int index = 0; index < uniformPixels.Length; index++)
            {
                uniformPixels[index] = uniform;
            }

            Assert.Throws<InvalidOperationException>(
                () =>
                    DocsImageCapture.Analyze(
                        EncodePixels(4, 4, uniformPixels),
                        new Rect(0f, 0f, 4f, 4f),
                        hiddenRegions
                    ),
                "Region analysis without the canary fails closed."
            );
        }

        [Test]
        public void ShouldCollectShotSubjectRegionOnlyWhenItIsRendered()
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window =
                ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            window.position = new Rect(0f, 0f, 220f, 120f);
            EditorSurfaceCapture.ShowPopup(window);

            VisualElement subject = new() { name = "settings-popover" };
            subject.style.width = 60f;
            subject.style.height = 24f;
            window.rootVisualElement.Add(subject);
            EditorSurfaceCapture.SettleLayout(window);

            IReadOnlyList<DocsImageCapture.CaptureRegion> regions = DocsImageCapture.CollectRegions(
                window,
                "settings"
            );
            DocsImageCapture.CaptureRegion subjectRegion = RegionOrNull(
                regions,
                "settings-popover"
            );
            Assert.That(
                subjectRegion != null,
                "The rendered popover is the region the settings shot must paint."
            );
            Assert.That(
                subjectRegion.Rect,
                Is.EqualTo(subject.worldBound),
                "The subject region carries the settled popover bounds."
            );

            subject.style.display = DisplayStyle.None;
            EditorSurfaceCapture.SettleLayout(window);
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                DocsImageCapture.CollectRegions(window, "settings")
            );
            Assert.That(
                exception.Message,
                Does.Contain("settings-popover"),
                "A hidden subject must fail closed with the element named."
            );
        }

        [Test]
        public void ShouldFailClosedWhenAShotSubjectElementIsMissing(
            [Values("create", "import", "settings")] string shotName
        )
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window =
                ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            window.position = new Rect(0f, 0f, 220f, 120f);
            EditorSurfaceCapture.ShowPopup(window);
            EditorSurfaceCapture.SettleLayout(window);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                DocsImageCapture.CollectRegions(window, shotName)
            );
            Assert.That(
                exception.Message,
                Does.Contain("element"),
                "The failure names the missing subject element and the shot."
            );
        }

        [Test]
        public void ShouldFailClosedWhenCollectRegionsIsCalledWithUnknownShot(
            [Values(null, "", "hero")] string shotName
        )
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window =
                ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            Assert.Throws<ArgumentException>(
                () => DocsImageCapture.CollectRegions(window, shotName),
                "An unknown shot name is a driver bug, not a capturable state."
            );
        }

        [Test]
        public void ShouldComputeSettingsCropFromPopoverBoundsClampedToWindow()
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window =
                ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            window.position = new Rect(0f, 0f, 300f, 200f);
            EditorSurfaceCapture.ShowPopup(window);
            VisualElement popover = new() { name = "settings-popover" };
            popover.style.position = Position.Absolute;
            popover.style.left = 20f;
            popover.style.top = 10f;
            popover.style.width = 100f;
            popover.style.height = 50f;
            window.rootVisualElement.Add(popover);
            EditorSurfaceCapture.SettleLayout(window);

            bool cropped = DocsImageCapture.TryComputeCropRect(
                window,
                "settings",
                out Rect cropRect
            );
            Assert.That(cropped, Is.True, "The settings shot crops to its popover.");
            Assert.That(
                cropRect,
                Is.EqualTo(Rect.MinMaxRect(12f, 2f, 128f, 68f)),
                "The crop pads the popover bounds by the shot's padding."
            );
        }

        [Test]
        public void ShouldComputeCreateCropFromTriggerAndPopoverUnionClampedToWindow()
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window =
                ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            window.position = new Rect(0f, 0f, 300f, 200f);
            EditorSurfaceCapture.ShowPopup(window);
            VisualElement root = window.rootVisualElement;

            /*
                The production popover is absolutely positioned while its trigger sits in
                the laid-out header, so the synthetic arrangement models exactly that.
            */
            VisualElement popover = new() { name = "create-popover" };
            popover.style.position = Position.Absolute;
            popover.style.left = 30f;
            popover.style.top = 20f;
            popover.style.width = 200f;
            popover.style.height = 100f;
            root.Add(popover);

            VisualElement trigger = new() { name = "create-object-button" };
            trigger.style.position = Position.Absolute;
            trigger.style.left = 250f;
            trigger.style.top = 5f;
            trigger.style.width = 20f;
            trigger.style.height = 20f;
            root.Add(trigger);
            EditorSurfaceCapture.SettleLayout(window);

            bool cropped = DocsImageCapture.TryComputeCropRect(window, "create", out Rect cropRect);
            Assert.That(cropped, Is.True, "The create shot crops to its trigger and popover.");
            Assert.That(
                cropRect,
                Is.EqualTo(Rect.MinMaxRect(22f, 0f, 278f, 128f)),
                "The crop covers the union of trigger and popover, padded and clamped to "
                    + "the window."
            );
        }

        [Test]
        public void ShouldKeepTheLayoutShotFullWindow()
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window =
                ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            window.position = new Rect(0f, 0f, 220f, 120f);
            EditorSurfaceCapture.ShowPopup(window);

            Assert.That(
                DocsImageCapture.TryComputeCropRect(
                    window,
                    DocsImageCapture.LayoutShotName,
                    out Rect _
                ),
                Is.False,
                "The layout shot ships the full window and must not crop."
            );
        }

        [Test]
        public void ShouldFailClosedWhenCropSubjectIsMissing(
            [Values("instance-actions", "create", "import", "settings")] string shotName
        )
        {
            using TestCleanupScope cleanup = new();
            EditorSurfaceCaptureHostWindow window =
                ScriptableObject.CreateInstance<EditorSurfaceCaptureHostWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            window.position = new Rect(0f, 0f, 220f, 120f);
            EditorSurfaceCapture.ShowPopup(window);
            EditorSurfaceCapture.SettleLayout(window);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                DocsImageCapture.TryComputeCropRect(window, shotName, out Rect _)
            );
            Assert.That(
                exception.Message,
                Does.Contain(shotName),
                "The failure names the shot whose crop subject is missing."
            );
        }

        [Test]
        public void ShouldCropCapturedPngToTheSubjectRectPreservingPixels()
        {
            int width = 64;
            int height = 48;
            Color32[] pixels = new Color32[width * height];
            FillRect(
                pixels,
                width,
                height,
                new Rect(0f, 0f, width, height),
                new Color32(24, 24, 28, 255)
            );
            FillVariedRect(pixels, width, height, new Rect(10f, 12f, 30f, 18f));

            string directory = Path.Combine("Temp", "DocsImageCaptureCropTests");
            Directory.CreateDirectory(directory);
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() =>
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            });

            string capturePath = Path.Combine(directory, "crop-source.png");
            File.WriteAllBytes(capturePath, EncodePixels(width, height, pixels));

            EditorSurfaceCaptureResult cropped = DocsImageCapture.CropCapturedPng(
                capturePath,
                new Rect(12f, 14f, 20f, 10f),
                new Rect(0f, 0f, width, height)
            );
            AssertValidPngFile(cropped);
            Assert.AreEqual(20, cropped.Width, "The crop width comes from the subject rect.");
            Assert.AreEqual(10, cropped.Height, "The crop height comes from the subject rect.");

            Texture2D decoded = new(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(
                    decoded.LoadImage(File.ReadAllBytes(capturePath)),
                    Is.True,
                    "The cropped file must decode for the pixel comparison."
                );
                Color32[] croppedPixels = decoded.GetPixels32();
                int firstSourceRow = height - 14 - 10;
                for (int row = 0; row < 10; row++)
                {
                    for (int column = 0; column < 20; column++)
                    {
                        Assert.AreEqual(
                            pixels[(firstSourceRow + row) * width + (12 + column)],
                            croppedPixels[row * 20 + column],
                            $"Crop pixel ({column},{row}) must match the source surface."
                        );
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(decoded);
            }
        }
    }
}
