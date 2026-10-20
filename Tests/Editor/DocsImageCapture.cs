namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Styles;
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;
    using Object = UnityEngine.Object;
    using PlayModeDataObject = WallstopStudios.DataVisualizer.Tests.Runtime.PlayModeDataObject;

    /*
        Manifest-driven documentation-image driver for #114. This is the automation the
        capture manifest slice owes the docs effort: it arranges a known window state
        (fixture assets, selected type and object, fixed splitter widths) and produces the
        documented shots through EditorSurfaceCapture, so imagery is regenerated from code
        instead of hand-staged screen captures.

        The manifest carries six shots. The layout shot is the full window. The other
        five serve the README and docs-site sections that the hand-taken video-frame
        JPGs used to illustrate: instance-actions focuses the objects pane whose rows
        carry the clone/rename/move/delete controls, create opens the create popover
        that asks for a new asset name, import opens the manage-visible-types popover
        (the namespace search dropdown with the project's types), search opens the
        global search popover with a typed query and its highlighted matches, and
        settings opens the settings popover with its persistence toggles and data
        folder field. The popovers are in-panel VisualElements, so the offscreen panel
        render composites them; each is opened through the window's own popover flow,
        then cropped shots cut the captured PNG down to the subject bounds recorded
        from the live window, so a section image shows the state it documents instead
        of duplicating the hero.

        The driver never writes into docs/ by default; callers name an output directory and
        review the results before committing them. Fixture assets are created under
        a unique Assets/DataVisualizerDocsCapture-* folder and deleted on every exit path. The window
        preferences the arrangement touches are snapshotted and restored, mirroring the
        hygiene of EditorSurfaceCaptureTests.

        The window's Instance field, popover fields, Cleanup, LoadInitialContent,
        SelectObject, the popover content builders, and PositionAndDisplayPopover
        members are private or internal with no public equivalent, and Unity's
        compilation does not honor InternalsVisibleTo for the editor-to-tests
        assembly pair (see ObjectIdExtensions). They are reached reflectively,
        exactly like the merged EditorSurfaceCaptureTests do; a Unity upgrade that
        removes one fails closed here with an exception instead of silently
        capturing the wrong state. Popovers open through the window's own content
        builders plus the extracted production positioning step
        PositionAndDisplayPopover, invoked synchronously: the capture's layout and
        render passes never tick the panel scheduler, so a capture that waited for
        OpenPopover's scheduled reveal would leave the popover hidden.

        CreateGUI defers its content load to a scheduled tick, and SelectType returns
        silently when the type is missing from the tree, so the driver invokes the initial
        load synchronously and verifies both selections instead of trusting them. The
        persisted last-selected namespace/type/object state that the arrangement rewrites
        is snapshotted and restored through the window's own PersistSettings.

        The SelectType object load is asynchronous; the three repaint/render cycles inside
        EditorSurfaceCapture.Capture give it time to populate on the capture host. With
        many thousands of assets a shot may show the list mid-load, which is visible in the
        result and acceptable for a driver whose output a human reviews before committing.

        Every capture is validated against the laid-out panel: subtrees Unity's offscreen
        renderer should have painted are measured in the PNG, and a capture that omits
        them fails closed. A uniform region is the signature of a subtree Unity skipped;
        the RCA recorded on issue #114 measured this exactly (a blanked subtree contributes
        zero pixels, so its region is one background color). On the macOS 6000.4 host the
        asset-name field, the labels section, InspectorElement internals, unselected
        namespace rows, and the IMGUI-backed read-only drawer were omitted while the
        Windows 6000.5 host painted every region, so the asset-name field is the canary:
        when it is blank the host has the recorded UIR gap and validation asserts only
        regions that painted even on such a host, keeping that host's known output
        reviewable while a capable host's hero is enforced pixel-faithful. Each shot's
        subject region (its open popover or the row action bar) joins the guarded set,
        so a capable host fails closed on a capture that omitted the state the shot
        exists to document.

        Validation measures only rendered subtrees. The saved settings persist namespace
        collapse state, so a host can legitimately hide type rows behind display:none
        containers; those regions have no pixels to paint and are excluded from the
        guarded set instead of failing every capture on such a host.
    */
    internal static class DocsImageCapture
    {
        /*
            The asset-name field is the canary for the host UIR gap recorded on issue #114.
            A blanked subtree contributes zero pixels, so its region reads as one uniform
            background color; painted rows carry borders and text antialiasing. The floor
            and ceiling separate those shapes with margin on both measured hosts.
        */
        internal const string CanaryRegionName = "inspector-asset-name-field";

        internal const string LayoutShotName = "layout";

        private const string InstanceActionsShotName = "instance-actions";
        private const string CreateShotName = "create";
        private const string ImportShotName = "import";
        private const string SearchShotName = "search";
        private const string SettingsShotName = "settings";

        private const string SettingsPopoverFieldName = "_settingsPopover";
        private const string SettingsButtonFieldName = "_settingsButton";
        private const string TypeAddPopoverFieldName = "_typeAddPopover";
        private const string TypeAddButtonFieldName = "_addTypeButton";
        private const string CreatePopoverFieldName = "_createPopover";
        private const string CreateButtonFieldName = "_createObjectButton";
        private const string SearchPopoverFieldName = "_searchPopover";
        private const string SearchFieldFieldName = "_searchField";

        private const string SettingsPopoverElementName = "settings-popover";
        private const string TypeAddPopoverElementName = "type-add-popover";
        private const string CreatePopoverElementName = "create-popover";
        private const string CreateButtonElementName = "create-object-button";
        private const string TypeAddButtonElementName = "add-type-button";
        private const string SearchPopoverElementName = "search-popover";
        private const string SearchFieldElementName = "global-search-field";
        private const string SearchResultsFieldName = "_currentSearchResultItems";
        private const string SearchCacheFieldName = "_allManagedObjectsCache";
        private const string SearchCachePopulatedFieldName = "_isSearchCachePopulated";
        private const string SearchQuery = "a";
        private const string ObjectColumnElementName = "object-column";
        private const string ObjectItemActionsClass = "object-item-actions";

        private const float CropPadding = 8f;
        private const int PngSignatureLength = 8;

        private const string FixtureFolderPrefix = "DataVisualizerDocsCapture-";
        private const float WindowWidth = 1180f;
        private const float WindowHeight = 780f;
        private const string DefaultOutputDirectory = "Temp/DocsImageCaptures";
        private const string OutputDirectoryArgument = "-docsImageOutputDir";
        private const string OutputDirectoryEnvironmentVariable = "DATAVISUALIZER_DOCS_IMAGE_DIR";

        private const string PrefsSplitterOuterKey = "WallstopStudios.Editor.DataVisualizer.SplitterOuterFixedPaneWidth";
        private const string PrefsSplitterInnerKey = "WallstopStudios.Editor.DataVisualizer.SplitterInnerFixedPaneWidth";
        private const string PrefsInitialSizeAppliedKey = "WallstopStudios.Editor.DataVisualizer.InitialSizeApplied";
        private const string PrefsPreferredWindowSizeKey = "WallstopStudios.Editor.DataVisualizer.PreferredWindowSize";
        private const string PrefsTemporaryWindowClampSizeKey = "WallstopStudios.Editor.DataVisualizer.TemporaryWindowClampSize";

        private const int PaintedDistinctColorFloor = 12;
        private const float PaintedModalFractionCeiling = 0.97f;

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

        private static readonly IReadOnlyList<string> ManifestShotNames = new[]
        {
            LayoutShotName,
            InstanceActionsShotName,
            CreateShotName,
            ImportShotName,
            SearchShotName,
            SettingsShotName,
        };

        private static readonly BindingFlags ReflectedInstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly BindingFlags ReflectedStaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

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
            string outputDirectory = ResolveOutputDirectory(Environment.GetCommandLineArgs(), Environment.GetEnvironmentVariable(OutputDirectoryEnvironmentVariable));
            IReadOnlyList<EditorSurfaceCaptureResult> results = CaptureAll(outputDirectory);
            foreach (EditorSurfaceCaptureResult result in results)
            {
                Debug.Log($"[DocsImageCapture] {result.OutputPath}: {result.Width}x{result.Height}, " + $"{result.ByteCount} bytes, {result.DistinctColorCount} distinct colors.");
            }
        }

        public static void CaptureCreationFramesFromCommandLine()
        {
            string outputDirectory = ResolveOutputDirectory(Environment.GetCommandLineArgs(), Environment.GetEnvironmentVariable(OutputDirectoryEnvironmentVariable));
            CaptureFrame(LayoutShotName, outputDirectory, crop: false);
            CaptureFrame(CreateShotName, outputDirectory, crop: false);
            Debug.Log($"[DocsImageCapture] Creation frames written to {outputDirectory}.");
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

        internal static EditorSurfaceCaptureResult CaptureShot(string shotName, string outputDirectory)
        {
            return CaptureFrame(shotName, outputDirectory, crop: true);
        }

        internal static EditorSurfaceCaptureResult CaptureFrame(string shotName, string outputDirectory, bool crop, float width = WindowWidth)
        {
            if (!IsKnownShot(shotName))
            {
                throw new ArgumentException($"Unknown shot '{shotName}'. Known shots: {string.Join(", ", ManifestShotNames)}.", nameof(shotName));
            }

            ValidateOutputDirectory(outputDirectory);
            if (!EditorSurfaceCapture.IsSupported)
            {
                throw new InvalidOperationException(
                    "Docs image capture needs a graphics device; this editor is running " + "without one, so capture refuses to write a blank image."
                );
            }

            string suffix = crop ? string.Empty : "-frame";
            string fileName = $"data-visualizer-{shotName}{suffix}.png";
            string capturePath = Path.Combine(outputDirectory, fileName);

            using TestCleanupScope fixtureCleanup = new();
            using TestCleanupScope cleanup = new();
            WindowPreferenceSnapshot preferences = WindowPreferenceSnapshot.Capture();
            cleanup.Defer(preferences.Restore);
            FieldInfo instanceField = typeof(DataVisualizerWindow).GetField("Instance", ReflectedStaticMembers);
            if (instanceField == null)
            {
                throw new InvalidOperationException("The DataVisualizer window exposes no Instance field; the capture " + "driver cannot preserve the host's window instance.");
            }

            object previousInstance = instanceField.GetValue(null);
            cleanup.Defer(() => instanceField.SetValue(null, previousInstance));
            cleanup.Defer(() => AssetGuidTypeIndex.Shared.Cancel());
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            PersistedSelectionSnapshot persistedSelection = null;
            cleanup.Defer(() => persistedSelection?.Restore(window));
            cleanup.Defer(() => InvokeCleanup(window));

            preferences.ApplyWidePanes();
            string fixtureFolder = CreateFixtureFolder(fixtureCleanup);
            CreateFixtureAssets(fixtureFolder);
            persistedSelection = PersistedSelectionSnapshot.Capture(window);

            EditorSurfaceCapture.ShowPopup(window);
            window.position = new Rect(40f, 40f, width, WindowHeight);
            window.CreateGUI();
            InvokeInitialContentLoad(window);
            NamespaceController controller = GetNamespaceController(window);
            SelectFixtureType(window, controller);
            VerifySelectedType(controller);
            StageFixtureObjects(window, fixtureFolder, cleanup);
            EditorSurfaceCapture.SettleLayout(window);
            SelectObject(window, LoadFirstFixtureAsset(fixtureFolder));
            VerifySelectedObject(window);
            if (!crop)
            {
                EditorSurfaceCapture.SettleLayout(window);
            }

            ArrangeShotState(window, shotName, fixtureFolder);
            PauseCaptureRootTasks(window);
            int stagedSearchGeneration = (int)typeof(DataVisualizerWindow).GetField("_searchCacheGeneration", ReflectedInstanceMembers).GetValue(window);

            EditorSurfaceCaptureResult result = EditorSurfaceCapture.Capture(window, capturePath);
            if (shotName == SearchShotName)
            {
                VerifySearchFixtures(window, stagedSearchGeneration, fixtureFolder);
            }
            ValidateCapturedRegions(window, capturePath, shotName);
            return crop ? CropCapturedShot(window, shotName, capturePath, result) : result;
        }

        internal static int PauseCaptureRootTasks(EditorWindow window)
        {
            VisualElement root = window.rootVisualElement;
            object scheduler = root.panel?.GetType().GetProperty("scheduler", ReflectedInstanceMembers)?.GetValue(root.panel);
            if (scheduler == null)
            {
                throw new InvalidOperationException("The capture panel exposes no scheduler; deferred work cannot be isolated.");
            }
            List<IVisualElementScheduledItem> owned = new();
            foreach (string fieldName in new[] { "m_ScheduledItems", "m_ScheduleTransactions" })
            {
                if (scheduler.GetType().GetField(fieldName, ReflectedInstanceMembers)?.GetValue(scheduler) is not System.Collections.IEnumerable items)
                {
                    throw new InvalidOperationException($"The capture scheduler exposes no {fieldName} collection; deferred work cannot be isolated.");
                }
                foreach (object candidate in items)
                {
                    if (candidate is IVisualElementScheduledItem item && item.element == root && item.isActive && !owned.Contains(item))
                    {
                        owned.Add(item);
                    }
                }
            }
            foreach (IVisualElementScheduledItem item in owned)
            {
                item.Pause();
            }
            return owned.Count;
        }

        internal static void StageSearchInput(List<ScriptableObject> cache, TextField field, string query)
        {
            cache.Sort(
                static (left, right) =>
                {
                    int name = string.Compare(left.name, right.name, StringComparison.Ordinal);
                    return name != 0 ? name : string.Compare(left.GetType().FullName, right.GetType().FullName, StringComparison.Ordinal);
                }
            );
            /*
                Offscreen capture moves the tree to another panel, which emits a synthetic
                blur. Keep the real typed query instead of restoring placeholder text.
                This callback lives only on the capture window, which the driver closes.
            */
            field.RegisterCallback<FocusOutEvent>(SuppressCaptureBlur, TrickleDown.TrickleDown);
            field.Focus();
            field.RemoveFromClassList(TextField.ussClassName + "__placeholder");
            field.value = query;
            field.Blur();
        }

        internal static string ResolveOutputDirectory(string[] arguments, string environmentValue)
        {
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], OutputDirectoryArgument, StringComparison.Ordinal))
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

        /*
            Fails closed when the captured PNG omits subtrees the settled panel laid out.
            Region rects are recorded from the live window before teardown, measured against
            the written file, and every region a capable host must paint is asserted; on a
            host with the recorded UIR gap only the regions that host painted are asserted.
            The shot name selects the subject regions (the open popover or the row action
            bar) that join the guarded set for that shot.
        */
        internal static void ValidateCapturedRegions(EditorWindow window, string capturePath, string shotName)
        {
            Rect rootBounds = window.rootVisualElement.worldBound;
            ValidateRegionAnalysis(Analyze(File.ReadAllBytes(capturePath), rootBounds, CollectRegions(window, shotName)));
        }

        /*
            Pure decision over measured regions, so the gap-host skip and the fail-closed
            paths stay unit-testable without a live window. A blanked canary means the host
            exhibits the recorded gap: log it and require only regions that painted even
            there, so a future total-blank regression still fails on every host.
        */
        internal static void ValidateRegionAnalysis(CaptureRegionAnalysis analysis)
        {
            if (!analysis.CanaryPainted)
            {
                Debug.LogWarning(
                    "[DocsImageCapture] Host offscreen render omits '"
                        + CanaryRegionName
                        + "' (Unity UI Toolkit renderer gap recorded on issue #114); asserting "
                        + "only regions that paint on such hosts."
                );
            }

            foreach (CaptureRegionMetric metric in analysis.Metrics)
            {
                bool required = analysis.CanaryPainted || metric.Region.PaintedOnGapHosts;
                if (required && !metric.Painted)
                {
                    throw new InvalidOperationException(
                        $"Captured region '{metric.Region.Name}' did not paint (distinct colors: "
                            + $"{metric.DistinctColors}, modal fraction: "
                            + $"{metric.ModalColorFraction:0.###}); the capture would mislead "
                            + "documentation readers."
                    );
                }
            }
        }

        /*
            The layout shot ships the full window; every other shot crops the captured PNG
            down to its subject so a section image documents its state instead of
            duplicating the hero. The rect comes from the live window's settled bounds and
            stays inside the window, so the mapping into the captured pixels is exact.
        */
        internal static bool TryComputeCropRect(EditorWindow window, string shotName, out Rect cropRect)
        {
            VisualElement root = window.rootVisualElement;
            switch (shotName)
            {
                case LayoutShotName:
                    cropRect = default;
                    return false;
                case InstanceActionsShotName:
                    cropRect = PaddedToRootBounds(root, RequiredElementBounds(root, ObjectColumnElementName, shotName));
                    return true;
                case CreateShotName:
                    cropRect = PaddedToRootBounds(
                        root,
                        Union(RequiredElementBounds(root, CreatePopoverElementName, shotName), RequiredElementBounds(root, CreateButtonElementName, shotName))
                    );
                    return true;
                case ImportShotName:
                    cropRect = PaddedToRootBounds(
                        root,
                        Union(RequiredElementBounds(root, TypeAddPopoverElementName, shotName), RequiredElementBounds(root, TypeAddButtonElementName, shotName))
                    );
                    return true;
                case SearchShotName:
                    cropRect = PaddedToRootBounds(
                        root,
                        Union(RequiredElementBounds(root, SearchPopoverElementName, shotName), RequiredElementBounds(root, SearchFieldElementName, shotName))
                    );
                    return true;
                case SettingsShotName:
                    cropRect = PaddedToRootBounds(root, RequiredElementBounds(root, SettingsPopoverElementName, shotName));
                    return true;
                default:
                    throw new ArgumentException($"Unknown shot '{shotName}'. Known shots: {string.Join(", ", ManifestShotNames)}.", nameof(shotName));
            }
        }

        /*
            Measures every region against the captured PNG. Region rects are recorded in
            panel point space and mapped into image space with the bounds of the captured
            surface, the same mapping the issue #114 RCA used; UI Toolkit measures from the
            top while the pixel array starts at the bottom row.
        */
        internal static CaptureRegionAnalysis Analyze(byte[] pngBytes, Rect sourceBounds, IReadOnlyList<CaptureRegion> regions)
        {
            if (sourceBounds.width < 1f || sourceBounds.height < 1f)
            {
                throw new InvalidOperationException(
                    $"The captured surface laid out to {sourceBounds.width}x{sourceBounds.height}; " + "region analysis has no surface to measure."
                );
            }

            Texture2D readback = new(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!readback.LoadImage(pngBytes))
                {
                    throw new InvalidOperationException("The captured file is not a decodable PNG; region analysis cannot run.");
                }

                Color32[] pixels = readback.GetPixels32();
                List<CaptureRegionMetric> metrics = new(regions.Count);
                bool canaryFound = false;
                bool canaryPainted = false;
                foreach (CaptureRegion region in regions)
                {
                    Rect pngRect = PixelRectForUiRect(region.Rect, sourceBounds, readback.width, readback.height);
                    int distinctColors = MeasureRegionColors(pixels, readback.width, readback.height, pngRect, out float modalColorFraction);
                    metrics.Add(new CaptureRegionMetric(region, distinctColors, modalColorFraction));
                    if (region.Name == CanaryRegionName)
                    {
                        canaryFound = true;
                        canaryPainted = metrics[metrics.Count - 1].Painted;
                    }
                }

                if (!canaryFound)
                {
                    throw new InvalidOperationException(
                        $"The captured window exposes no '{CanaryRegionName}' region; the "
                            + "arrangement is not the documented state, so region validation "
                            + "cannot decide whether the capture is faithful."
                    );
                }

                return new CaptureRegionAnalysis(metrics, canaryPainted);
            }
            finally
            {
                Object.DestroyImmediate(readback);
            }
        }

        /*
            Cuts the captured PNG down to the crop rect and writes it over capturePath.
            The crop copies the exact decoded pixels, so the shipped file stays the
            validated surface minus everything outside the subject; byte identity across
            runs is preserved because both the decode and the encode are deterministic.
        */
        internal static EditorSurfaceCaptureResult CropCapturedPng(string capturePath, Rect uiCropRect, Rect sourceBounds)
        {
            byte[] fullPng = File.ReadAllBytes(capturePath);
            Texture2D source = new(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(fullPng))
                {
                    throw new InvalidOperationException("The captured file is not a decodable PNG; the crop cannot run.");
                }

                Rect pixelRect = PixelRectForUiRect(uiCropRect, sourceBounds, source.width, source.height);
                int x0 = Mathf.Clamp(Mathf.RoundToInt(pixelRect.x), 0, source.width - 1);
                int y0 = Mathf.Clamp(Mathf.RoundToInt(pixelRect.y), 0, source.height - 1);
                int croppedWidth = Mathf.Clamp(Mathf.RoundToInt(pixelRect.width), 1, source.width - x0);
                int croppedHeight = Mathf.Clamp(Mathf.RoundToInt(pixelRect.height), 1, source.height - y0);

                Color32[] sourcePixels = source.GetPixels32();
                Color32[] croppedPixels = new Color32[croppedWidth * croppedHeight];
                int firstSourceRow = source.height - y0 - croppedHeight;
                for (int row = 0; row < croppedHeight; row++)
                {
                    int sourceRow = firstSourceRow + row;
                    for (int column = 0; column < croppedWidth; column++)
                    {
                        croppedPixels[row * croppedWidth + column] = sourcePixels[sourceRow * source.width + x0 + column];
                    }
                }

                Texture2D cropped = new(croppedWidth, croppedHeight, TextureFormat.RGB24, false, true);
                try
                {
                    cropped.SetPixels32(croppedPixels);
                    cropped.Apply(false, false);
                    byte[] png = cropped.EncodeToPNG();
                    if (png == null || png.Length <= PngSignatureLength)
                    {
                        throw new InvalidOperationException("PNG encoding of the cropped capture produced no usable bytes.");
                    }

                    File.WriteAllBytes(capturePath, png);
                    return new EditorSurfaceCaptureResult(capturePath, croppedWidth, croppedHeight, png.Length, EditorSurfaceCapture.CountDistinctColors(cropped));
                }
                finally
                {
                    Object.DestroyImmediate(cropped);
                }
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        /*
            Regions come from the live window tree, so the guarded set follows whatever the
            window actually built. Namespace/type rows are recorded per element with their
            selection state (selected rows painted even on the macOS gap host); the asset
            name field, labels section, script row, and IMGUI-backed read-only drawer did
            not paint there, while the title and description fields and the object list did.

            The shot name adds that shot's subject region: the open popover or the row
            action bar the shot exists to document. Subject regions fail closed when their
            element is missing or unrendered, because a shot captured without its subject
            is exactly the misleading image the validation exists to block. Only rendered
            subtrees are recorded for the common set: a persisted collapsed namespace hides
            its type rows behind a display:none container, and a region that cannot paint
            would otherwise clamp to a one-pixel sample that always fails the painted check.
        */
        internal static IReadOnlyList<CaptureRegion> CollectRegions(EditorWindow window, string shotName)
        {
            if (!IsKnownShot(shotName))
            {
                throw new ArgumentException($"Unknown shot '{shotName}'. Known shots: {string.Join(", ", ManifestShotNames)}.", nameof(shotName));
            }

            VisualElement root = window.rootVisualElement;
            List<CaptureRegion> regions = new();
            foreach (VisualElement group in root.Query<VisualElement>(null, StyleConstants.NamespaceItemClass).ToList())
            {
                AddRenderedRegion(regions, group.name, group, group.ClassListContains(StyleConstants.SelectedClass));
            }

            foreach (VisualElement item in root.Query<VisualElement>(null, StyleConstants.TypeItemClass).ToList())
            {
                AddRenderedRegion(regions, item.name, item, item.ClassListContains(StyleConstants.SelectedClass));
            }

            AddRegion(root, regions, CanaryRegionName, false);
            AddRegion(root, regions, "inspector-labels-section", false);

            InspectorElement inspector = root.Q<InspectorElement>();
            if (inspector != null)
            {
                foreach (BindableElement bindable in inspector.Query<BindableElement>().ToList())
                {
                    string bindingPath = bindable.bindingPath;
                    if (bindingPath == "m_Script")
                    {
                        AddRenderedRegion(regions, "bindable:m_Script", bindable, false);
                    }
                    else if (bindingPath == "_title" || bindingPath == "_description")
                    {
                        AddRenderedRegion(regions, "bindable:" + bindingPath, bindable, true);
                    }
                }
            }

            List<IMGUIContainer> imguiContainers = root.Query<IMGUIContainer>().ToList();
            for (int index = 0; index < imguiContainers.Count; index++)
            {
                AddRenderedRegion(regions, "imgui:" + index, imguiContainers[index], false);
            }

            ListView listView = root.Q<ListView>();
            if (listView != null)
            {
                AddRenderedRegion(regions, "listview", listView, true);
            }

            AddSubjectRegion(root, regions, shotName);

            return regions;
        }

        /*
            A region is only expected to paint when it is actually rendered. UI Toolkit
            removes a display:none subtree from layout entirely and resolves visibility
            per element (inherited values are already baked into resolvedStyle), while a
            zero-area element has nothing to paint. Namespace collapse state persists in
            the saved settings, so hidden rows are a normal host state, not a broken one.
        */
        internal static bool IsRenderedForCapture(VisualElement element)
        {
            if (element == null)
            {
                return false;
            }

            Rect worldBound = element.worldBound;
            if (worldBound.width < 1f || worldBound.height < 1f)
            {
                return false;
            }

            if (element.resolvedStyle.visibility == Visibility.Hidden)
            {
                return false;
            }

            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current.resolvedStyle.display == DisplayStyle.None)
                {
                    return false;
                }
            }

            return true;
        }

        internal static string CreateFixtureFolder(TestCleanupScope cleanup)
        {
            string folderName = FixtureFolderPrefix + Guid.NewGuid().ToString("N");
            string folderPath = "Assets/" + folderName;
            string folderGuid = null;
            string markerPath = folderPath + "/.capture-owner";
            bool markerWritten = false;
            cleanup.Defer(() =>
            {
                bool ownsFolder =
                    !string.IsNullOrEmpty(folderGuid)
                    && string.Equals(AssetDatabase.AssetPathToGUID(folderPath, AssetPathToGUIDOptions.OnlyExistingAssets), folderGuid, StringComparison.Ordinal);
                /* Unity can reuse a deleted folder's GUID when the same path is recreated. */
                bool ownsContents = !markerWritten || (File.Exists(markerPath) && string.Equals(File.ReadAllText(markerPath), folderName, StringComparison.Ordinal));
                if (ownsFolder && ownsContents)
                {
                    AssetDatabase.DeleteAsset(folderPath);
                }
            });
            folderGuid = AssetDatabase.CreateFolder("Assets", folderName);
            if (string.IsNullOrEmpty(folderGuid))
            {
                throw new InvalidOperationException($"Could not create capture fixture folder '{folderPath}'.");
            }
            folderPath = AssetDatabase.GUIDToAssetPath(folderGuid);
            markerPath = folderPath + "/.capture-owner";
            File.WriteAllText(markerPath, folderName);
            markerWritten = true;
            return folderPath;
        }

        /*
            Maps a panel-space rect into the pixel space of a PNG that captured the given
            source bounds: UI Toolkit measures from the top-left while the pixel array starts
            at the bottom row.
        */
        private static Rect PixelRectForUiRect(Rect uiRect, Rect sourceBounds, int pixelWidth, int pixelHeight)
        {
            float scaleX = pixelWidth / sourceBounds.width;
            float scaleY = pixelHeight / sourceBounds.height;
            return new Rect((uiRect.x - sourceBounds.x) * scaleX, (uiRect.y - sourceBounds.y) * scaleY, uiRect.width * scaleX, uiRect.height * scaleY);
        }

        /*
            Rewrites the validated capture in place as the shot's cropped image when the
            shot defines a crop; full-window shots return the capture untouched.
        */
        private static EditorSurfaceCaptureResult CropCapturedShot(EditorWindow window, string shotName, string capturePath, EditorSurfaceCaptureResult result)
        {
            if (!TryComputeCropRect(window, shotName, out Rect cropRect))
            {
                return result;
            }

            return CropCapturedPng(capturePath, cropRect, window.rootVisualElement.worldBound);
        }

        /*
            Bounds of a named element the crop or the guarded region set depends on. A
            missing or unrendered element means the arrangement broke, so the shot fails
            closed instead of cropping or guarding against a stale rect.
        */
        private static Rect RequiredElementBounds(VisualElement root, string elementName, string shotName)
        {
            VisualElement element = root.Q(elementName);
            if (element == null)
            {
                throw new InvalidOperationException($"The window exposes no '{elementName}' element; the '{shotName}' shot " + "cannot prove its subject was arranged.");
            }

            if (!IsRenderedForCapture(element))
            {
                throw new InvalidOperationException($"The '{elementName}' element is not rendered; the '{shotName}' shot " + "would document a state that is not on screen.");
            }

            return element.worldBound;
        }

        private static Rect PaddedToRootBounds(VisualElement root, Rect bounds)
        {
            Rect rootBounds = root.worldBound;
            return Rect.MinMaxRect(
                Mathf.Max(rootBounds.xMin, bounds.xMin - CropPadding),
                Mathf.Max(rootBounds.yMin, bounds.yMin - CropPadding),
                Mathf.Min(rootBounds.xMax, bounds.xMax + CropPadding),
                Mathf.Min(rootBounds.yMax, bounds.yMax + CropPadding)
            );
        }

        private static Rect Union(Rect first, Rect second)
        {
            return Rect.MinMaxRect(Mathf.Min(first.xMin, second.xMin), Mathf.Min(first.yMin, second.yMin), Mathf.Max(first.xMax, second.xMax), Mathf.Max(first.yMax, second.yMax));
        }

        private static void AddRenderedRegion(List<CaptureRegion> regions, string regionName, VisualElement element, bool paintedOnGapHosts)
        {
            if (IsRenderedForCapture(element))
            {
                regions.Add(new CaptureRegion(regionName, element.worldBound, paintedOnGapHosts));
            }
        }

        private static void AddRegion(VisualElement root, List<CaptureRegion> regions, string elementName, bool paintedOnGapHosts)
        {
            VisualElement element = root.Q(elementName);
            if (element != null)
            {
                AddRenderedRegion(regions, elementName, element, paintedOnGapHosts);
            }
        }

        /*
            Adds the region the shot exists to document: the open popover for the popover
            shots and the row action bar for instance-actions. Unlike the common regions,
            a missing or unrendered subject fails closed, because a capture without its
            subject is the misleading image the validation exists to block.
        */
        private static void AddSubjectRegion(VisualElement root, List<CaptureRegion> regions, string shotName)
        {
            switch (shotName)
            {
                case InstanceActionsShotName:
                    AddFirstRenderedRowActions(root, regions, shotName);
                    break;
                case CreateShotName:
                    AddRequiredRegion(root, regions, CreatePopoverElementName, shotName);
                    break;
                case ImportShotName:
                    AddRequiredRegion(root, regions, TypeAddPopoverElementName, shotName);
                    break;
                case SearchShotName:
                    AddRequiredRegion(root, regions, SearchPopoverElementName, shotName);
                    break;
                case SettingsShotName:
                    AddRequiredRegion(root, regions, SettingsPopoverElementName, shotName);
                    break;
            }
        }

        private static void AddRequiredRegion(VisualElement root, List<CaptureRegion> regions, string elementName, string shotName)
        {
            regions.Add(new CaptureRegion(elementName, RequiredElementBounds(root, elementName, shotName), false));
        }

        private static void AddFirstRenderedRowActions(VisualElement root, List<CaptureRegion> regions, string shotName)
        {
            foreach (VisualElement actions in root.Query<VisualElement>(null, ObjectItemActionsClass).ToList())
            {
                if (!IsRenderedForCapture(actions))
                {
                    continue;
                }

                regions.Add(new CaptureRegion("object-row-actions", actions.worldBound, false));
                return;
            }

            throw new InvalidOperationException(
                $"The window exposes no rendered '{ObjectItemActionsClass}' row; the " + $"'{shotName}' shot cannot prove the row actions painted."
            );
        }

        private static int MeasureRegionColors(Color32[] pixels, int width, int height, Rect pngRect, out float modalColorFraction)
        {
            int x0 = Mathf.Clamp(Mathf.RoundToInt(pngRect.x), 0, width - 1);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(pngRect.y), 0, height - 1);
            int regionWidth = Mathf.Clamp(Mathf.RoundToInt(pngRect.width), 1, width - x0);
            int regionHeight = Mathf.Clamp(Mathf.RoundToInt(pngRect.height), 1, height - y0);
            Dictionary<int, int> counts = new();
            int total = 0;
            int stepX = Mathf.Max(1, regionWidth / 100);
            int stepY = Mathf.Max(1, regionHeight / 100);
            for (int row = 0; row < regionHeight; row += stepY)
            {
                int pixelRow = height - 1 - (y0 + row);
                for (int column = 0; column < regionWidth; column += stepX)
                {
                    Color32 pixel = pixels[pixelRow * width + (x0 + column)];
                    int key = (pixel.r << 16) | (pixel.g << 8) | pixel.b;
                    counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
                    total++;
                }
            }

            int modalCount = 0;
            foreach (int count in counts.Values)
            {
                modalCount = Mathf.Max(modalCount, count);
            }

            modalColorFraction = total == 0 ? 1f : (float)modalCount / total;
            return counts.Count;
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
                throw new ArgumentException("Capture needs an output directory.", nameof(outputDirectory));
            }
        }

        private static void CreateFixtureAssets(string fixtureFolder)
        {
            using TestCleanupScope transientAssets = new();
            foreach (string title in FixtureTitles)
            {
                PlayModeDataObject asset = ScriptableObject.CreateInstance<PlayModeDataObject>();
                transientAssets.Defer(() =>
                {
                    if (asset != null && !EditorUtility.IsPersistent(asset))
                    {
                        Object.DestroyImmediate(asset);
                    }
                });
                asset.Title = title;
                asset.Description = $"Default data for {title.ToLowerInvariant()}.";
                AssetDatabase.CreateAsset(asset, $"{fixtureFolder}/{title}.asset");
                /* Stable authored IDs keep image content independent of per-run asset GUIDs. */
                using SerializedObject serialized = new(asset);
                serialized.FindProperty(nameof(BaseDataObject._assetGuid)).stringValue = "docs-" + title.Replace(" ", "-").ToLowerInvariant();
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SetLabels(asset, title == FixtureTitles[0] ? new[] { "Reviewed", "Urgent" } : new[] { "Reviewed" });
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void StageFixtureObjects(DataVisualizerWindow window, string fixtureFolder, TestCleanupScope cleanup)
        {
            if (typeof(DataVisualizerWindow).GetField("_asyncLoadTask", ReflectedInstanceMembers)?.GetValue(window) is IVisualElementScheduledItem pending)
            {
                pending.Pause();
            }
            typeof(DataVisualizerWindow).GetField("_isLoadingObjectsAsync", ReflectedInstanceMembers).SetValue(window, false);
            ((Queue<string>)typeof(DataVisualizerWindow).GetField("_pendingObjectGuids", ReflectedInstanceMembers).GetValue(window)).Clear();
            InvokeWindowMethod(window, "UpdateLoadingIndicator", new[] { typeof(int), typeof(int) }, new object[] { 0, 0 });
            List<ScriptableObject> objects = (List<ScriptableObject>)typeof(DataVisualizerWindow).GetField("_selectedObjects", ReflectedInstanceMembers).GetValue(window);
            objects.Clear();
            foreach (string title in FixtureTitles)
            {
                PlayModeDataObject asset = AssetDatabase.LoadAssetAtPath<PlayModeDataObject>($"{fixtureFolder}/{title}.asset");
                if (asset == null)
                {
                    throw new InvalidOperationException($"Capture fixture '{title}' did not load.");
                }
                objects.Add(asset);
            }
            objects.Sort(static (left, right) => string.Compare(left.name, right.name, StringComparison.Ordinal));

            TypeLabelFilterConfig original = (TypeLabelFilterConfig)
                typeof(DataVisualizerWindow).GetProperty("CurrentTypeLabelFilterConfig", ReflectedInstanceMembers).GetValue(window);
            cleanup.Defer(() => InvokeWindowMethod(window, "SaveLabelFilterConfig", new[] { typeof(TypeLabelFilterConfig) }, new object[] { original }));
            TypeLabelFilterConfig staged = new() { typeFullName = original.typeFullName, isCollapsed = false };
            InvokeWindowMethod(window, "SaveLabelFilterConfig", new[] { typeof(TypeLabelFilterConfig) }, new object[] { staged });
            InvokeWindowMethod(window, "UpdateLabelAreaAndFilter", Type.EmptyTypes, Array.Empty<object>());
        }

        private static PlayModeDataObject LoadFirstFixtureAsset(string fixtureFolder)
        {
            string assetPath = $"{fixtureFolder}/{FixtureTitles[0]}.asset";
            PlayModeDataObject asset = AssetDatabase.LoadAssetAtPath<PlayModeDataObject>(assetPath);
            if (asset == null)
            {
                throw new InvalidOperationException($"The fixture asset '{assetPath}' did not load; the window would " + "capture an unarranged state.");
            }

            return asset;
        }

        /*
            CreateGUI defers content building to a scheduled tick; the driver needs the
            namespace tree built now, so the private loader runs synchronously here.
        */
        private static void InvokeInitialContentLoad(DataVisualizerWindow window)
        {
            MethodInfo loadInitialContent = typeof(DataVisualizerWindow).GetMethod(
                "LoadInitialContent",
                ReflectedInstanceMembers,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null
            );
            if (loadInitialContent == null)
            {
                throw new InvalidOperationException("The DataVisualizer window exposes no LoadInitialContent member; the " + "capture driver cannot build the namespace tree.");
            }

            loadInitialContent.Invoke(window, null);
        }

        private static NamespaceController GetNamespaceController(DataVisualizerWindow window)
        {
            FieldInfo controllerField = typeof(DataVisualizerWindow).GetField("_namespaceController", ReflectedInstanceMembers);
            object controller = controllerField?.GetValue(window);
            if (controller == null)
            {
                throw new InvalidOperationException("The DataVisualizer window exposes no namespace controller; the capture " + "driver cannot select the fixture type.");
            }

            return (NamespaceController)controller;
        }

        private static void SelectFixtureType(DataVisualizerWindow window, NamespaceController controller)
        {
            controller.SelectType(window, typeof(PlayModeDataObject));
        }

        /*
            SelectType returns silently when the type is missing from the namespace tree, so
            the arrangement is verified instead of trusted: a missed selection fails closed.
        */
        private static void VerifySelectedType(NamespaceController controller)
        {
            if (controller.SelectedType != typeof(PlayModeDataObject))
            {
                throw new InvalidOperationException(
                    "The fixture type was not selected after SelectType (selected: "
                        + $"{controller.SelectedType?.FullName ?? "null"}); the capture would "
                        + "show an unarranged window."
                );
            }
        }

        private static void VerifySelectedObject(DataVisualizerWindow window)
        {
            FieldInfo selectedObjectField = typeof(DataVisualizerWindow).GetField("_selectedObject", ReflectedInstanceMembers);
            object selected = selectedObjectField?.GetValue(window);
            if (selected is not PlayModeDataObject)
            {
                throw new InvalidOperationException("The fixture asset was not selected after SelectObject; the capture " + "would show an empty inspector.");
            }
        }

        private static void SelectObject(DataVisualizerWindow window, ScriptableObject asset)
        {
            MethodInfo selectObject = typeof(DataVisualizerWindow).GetMethod(
                "SelectObjectAndNavigate",
                ReflectedInstanceMembers,
                binder: null,
                types: new[] { typeof(ScriptableObject) },
                modifiers: null
            );
            if (selectObject == null)
            {
                throw new InvalidOperationException("The DataVisualizer window exposes no SelectObject member; the capture " + "driver cannot stage the inspector.");
            }

            selectObject.Invoke(window, new object[] { asset });
        }

        /*
            Opens the state each non-layout shot documents. The popovers open through the
            window's own content builders and the production positioning step; the reveal
            happens synchronously because the capture's layout and render passes never
            tick the panel scheduler, so waiting for the scheduled reveal would leave the
            popover hidden.
        */
        private static void ArrangeShotState(DataVisualizerWindow window, string shotName, string fixtureFolder)
        {
            switch (shotName)
            {
                case CreateShotName:
                    InvokeWindowMethod(window, "BuildCreatePopoverContent", new[] { typeof(Type) }, new object[] { typeof(PlayModeDataObject) });
                    StagePopoverForCapture(window, CreatePopoverFieldName, CreateButtonFieldName);
                    break;
                case ImportShotName:
                    if (ReadWindowElement(window, "_typeAddSearchField") is not TextField typeSearch)
                    {
                        throw new InvalidOperationException("The type finder has no search field.");
                    }
                    typeSearch.value = "Creation";
                    StagePopoverForCapture(window, TypeAddPopoverFieldName, TypeAddButtonFieldName);
                    break;
                case SearchShotName:
                    StageSearchState(window, fixtureFolder);
                    break;
                case SettingsShotName:
                    InvokeWindowMethod(window, "BuildSettingsPopoverContent", Type.EmptyTypes, Array.Empty<object>());
                    StagePopoverForCapture(window, SettingsPopoverFieldName, SettingsButtonFieldName);
                    break;
            }
        }

        private static void SuppressCaptureBlur(FocusOutEvent change)
        {
            change.StopImmediatePropagation();
        }

        /*
            Stages the global search the way a user sees it after typing a query. The
            search cache is injected with the fixture assets and marked populated instead
            of awaited: the async population ticks on editor updates the capture's
            synchronous flow never runs, and injecting exactly the fixtures keeps the
            shot deterministic whatever else the host project contains. The query then
            goes through the field itself so the production change callback builds the
            highlighted results, the popover is revealed through the shared positioning
            step, and a result-less match fails closed instead of documenting an empty
            popover.
        */
        private static void VerifySearchFixtures(DataVisualizerWindow window, int stagedGeneration, string fixtureFolder)
        {
            if (
                typeof(DataVisualizerWindow).GetField(SearchResultsFieldName, ReflectedInstanceMembers)?.GetValue(window) is not List<VisualElement> results
                || results.Count != FixtureTitles.Length
            )
            {
                int currentGeneration = (int)typeof(DataVisualizerWindow).GetField("_searchCacheGeneration", ReflectedInstanceMembers).GetValue(window);
                int count = (typeof(DataVisualizerWindow).GetField(SearchResultsFieldName, ReflectedInstanceMembers).GetValue(window) as List<VisualElement>)?.Count ?? -1;
                throw new InvalidOperationException(
                    $"The search capture must contain exactly the owned fixture results: count={count}; generation={stagedGeneration}->{currentGeneration}."
                );
            }
            foreach (VisualElement row in results)
            {
                if (row.userData is not ScriptableObject asset || asset == null || !AssetDatabase.GetAssetPath(asset).StartsWith(fixtureFolder + "/", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The search capture contains an asset outside its owned fixture root.");
                }
            }
        }

        private static void StageSearchState(DataVisualizerWindow window, string fixtureFolder)
        {
            FieldInfo cacheField = typeof(DataVisualizerWindow).GetField(SearchCacheFieldName, ReflectedInstanceMembers);
            if (cacheField?.GetValue(window) is not List<ScriptableObject> cache)
            {
                throw new InvalidOperationException("The DataVisualizer window exposes no search cache; the capture " + "driver cannot stage the search results.");
            }

            FieldInfo populatedField = typeof(DataVisualizerWindow).GetField(SearchCachePopulatedFieldName, ReflectedInstanceMembers);
            if (populatedField == null)
            {
                throw new InvalidOperationException("The DataVisualizer window exposes no search-cache flag; the capture " + "driver cannot stage the search results.");
            }

            if (typeof(DataVisualizerWindow).GetField("_searchCacheLoadTask", ReflectedInstanceMembers)?.GetValue(window) is IVisualElementScheduledItem pendingSearch)
            {
                pendingSearch.Pause();
            }
            AssetObjectCache indexedCache = typeof(DataVisualizerWindow).GetField("_assetObjectCache", ReflectedInstanceMembers)?.GetValue(window) as AssetObjectCache;
            if (indexedCache == null)
            {
                throw new InvalidOperationException("The capture needs the GUID-keyed search cache.");
            }
            indexedCache.Clear();
            for (int index = 0; index < FixtureTitles.Length; index++)
            {
                PlayModeDataObject asset = AssetDatabase.LoadAssetAtPath<PlayModeDataObject>($"{fixtureFolder}/{FixtureTitles[index]}.asset");
                if (asset == null)
                {
                    throw new InvalidOperationException($"The fixture asset '{FixtureTitles[index]}' did not load; the search shot " + "would capture an empty index.");
                }

                string path = AssetDatabase.GetAssetPath(asset);
                indexedCache.Upsert(AssetDatabase.AssetPathToGUID(path), asset, sorted: false);
            }
            indexedCache.Sort();

            populatedField.SetValue(window, true);
            TextField searchField = ReadWindowElement(window, SearchFieldFieldName) as TextField;
            if (searchField == null)
            {
                throw new InvalidOperationException("The DataVisualizer window exposes no search text field; the capture " + "driver cannot stage the search results.");
            }

            StageSearchInput(cache, searchField, SearchQuery);
            if (typeof(DataVisualizerWindow).GetField(SearchResultsFieldName, ReflectedInstanceMembers)?.GetValue(window) is not List<VisualElement> results || results.Count == 0)
            {
                throw new InvalidOperationException($"The staged query '{SearchQuery}' matched no fixture asset; the search shot " + "would document an empty popover.");
            }

            EditorSurfaceCapture.SettleLayout(window);
            StagePopoverForCapture(window, SearchPopoverFieldName, SearchFieldFieldName, shouldFocus: false);
        }

        /*
            Reveals a popover through the extracted production step OpenPopover schedules,
            so the shot shows the same placement and visibility a user gets.
        */
        private static void StagePopoverForCapture(DataVisualizerWindow window, string popoverFieldName, string triggerFieldName, bool shouldFocus = true)
        {
            VisualElement popover = ReadWindowElement(window, popoverFieldName);
            VisualElement trigger = ReadWindowElement(window, triggerFieldName);
            MethodInfo positionAndDisplay = typeof(DataVisualizerWindow).GetMethod(
                "PositionAndDisplayPopover",
                ReflectedInstanceMembers,
                binder: null,
                types: new[] { typeof(VisualElement), typeof(VisualElement), typeof(bool), typeof(bool) },
                modifiers: null
            );
            if (positionAndDisplay == null)
            {
                throw new InvalidOperationException(
                    "The DataVisualizer window exposes no PositionAndDisplayPopover member; " + "the capture driver cannot stage the popover states."
                );
            }

            positionAndDisplay.Invoke(window, new object[] { popover, trigger, false, shouldFocus });
        }

        private static void InvokeWindowMethod(DataVisualizerWindow window, string methodName, Type[] parameterTypes, object[] arguments)
        {
            MethodInfo method = typeof(DataVisualizerWindow).GetMethod(methodName, ReflectedInstanceMembers, binder: null, types: parameterTypes, modifiers: null);
            if (method == null)
            {
                throw new InvalidOperationException($"The DataVisualizer window exposes no {methodName} member; the capture " + "driver cannot arrange the popover states.");
            }

            method.Invoke(window, arguments);
        }

        private static VisualElement ReadWindowElement(DataVisualizerWindow window, string fieldName)
        {
            FieldInfo field = typeof(DataVisualizerWindow).GetField(fieldName, ReflectedInstanceMembers);
            object value = field?.GetValue(window);
            if (value is not VisualElement element)
            {
                throw new InvalidOperationException(
                    $"The DataVisualizer window exposes no '{fieldName}' visual element; the " + "capture driver cannot arrange the popover states."
                );
            }

            return element;
        }

        private static void InvokeCleanup(DataVisualizerWindow window)
        {
            MethodInfo cleanup = typeof(DataVisualizerWindow).GetMethod("Cleanup", ReflectedInstanceMembers);
            cleanup?.Invoke(window, null);
        }

        /*
            One subtree the capture must paint, recorded in panel point space. Regions the
            macOS gap host painted anyway are marked, so validation can still assert them
            while skipping the rest on such a host.
        */
        internal sealed class CaptureRegion
        {
            internal string Name { get; }

            internal Rect Rect { get; }

            internal bool PaintedOnGapHosts { get; }

            internal CaptureRegion(string name, Rect rect, bool paintedOnGapHosts)
            {
                Name = name;
                Rect = rect;
                PaintedOnGapHosts = paintedOnGapHosts;
            }
        }

        internal sealed class CaptureRegionMetric
        {
            /*
                A uniform region is one background color; painted rows carry borders and text
                antialiasing, which yields dozens of distinct colors on both measured hosts.
            */
            internal bool Painted => DistinctColors >= PaintedDistinctColorFloor && ModalColorFraction <= PaintedModalFractionCeiling;

            internal CaptureRegion Region { get; }

            internal int DistinctColors { get; }

            internal float ModalColorFraction { get; }

            internal CaptureRegionMetric(CaptureRegion region, int distinctColors, float modalColorFraction)
            {
                Region = region;
                DistinctColors = distinctColors;
                ModalColorFraction = modalColorFraction;
            }
        }

        internal sealed class CaptureRegionAnalysis
        {
            internal IReadOnlyList<CaptureRegionMetric> Metrics { get; }

            internal bool CanaryPainted { get; }

            internal CaptureRegionAnalysis(IReadOnlyList<CaptureRegionMetric> metrics, bool canaryPainted)
            {
                Metrics = metrics;
                CanaryPainted = canaryPainted;
            }
        }

        /*
            Snapshot of the five window EditorPrefs keys the arrangement touches, restored
            on Dispose so a capture leaves the host's saved window state untouched.
        */
        internal sealed class WindowPreferenceSnapshot
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
                    hadTemporaryWindowClampSize: EditorPrefs.HasKey(PrefsTemporaryWindowClampSizeKey),
                    temporaryWindowClampSize: EditorPrefs.GetString(PrefsTemporaryWindowClampSizeKey)
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
                The docs layout uses the default balanced pane widths, set before the
                window builds its UI; the window's own one-time
                sizing keys are cleared so the fixed capture rectangle survives.
            */
            internal void ApplyWidePanes()
            {
                EditorPrefs.SetFloat(PrefsSplitterOuterKey, 320f);
                EditorPrefs.SetFloat(PrefsSplitterInnerKey, 280f);
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

                RestoreString(PrefsPreferredWindowSizeKey, _hadPreferredWindowSize, _preferredWindowSize);
                RestoreString(PrefsTemporaryWindowClampSizeKey, _hadTemporaryWindowClampSize, _temporaryWindowClampSize);
            }
        }

        /*
            Snapshot of the persisted selection state the arrangement rewrites (SelectType
            saves the last namespace/type, SelectObject saves the last object per type, into
            the settings asset or the user-state JSON depending on the host's persistence
            mode). Restore routes through the window's own PersistSettings so the write-back
            respects the same persistence mode and dirty/save handling. The settings-side
            members are internal with no public equivalent, so they are reached reflectively
            like the window lifecycle members above.
        */
        private sealed class PersistedSelectionSnapshot
        {
            private const string NamespaceKeyMember = "lastSelectedNamespaceKey";
            private const string TypeFullNameMember = "lastSelectedTypeFullName";
            private const string ObjectSelectionsMember = "lastObjectSelections";

            private readonly string _settingsNamespaceKey;
            private readonly string _settingsTypeFullName;
            private readonly List<LastObjectSelectionEntry> _settingsObjectSelections;
            private readonly string _userStateNamespaceKey;
            private readonly string _userStateTypeFullName;
            private readonly List<LastObjectSelectionEntry> _userStateObjectSelections;

            private PersistedSelectionSnapshot(
                string settingsNamespaceKey,
                string settingsTypeFullName,
                List<LastObjectSelectionEntry> settingsObjectSelections,
                string userStateNamespaceKey,
                string userStateTypeFullName,
                List<LastObjectSelectionEntry> userStateObjectSelections
            )
            {
                _settingsNamespaceKey = settingsNamespaceKey;
                _settingsTypeFullName = settingsTypeFullName;
                _settingsObjectSelections = settingsObjectSelections;
                _userStateNamespaceKey = userStateNamespaceKey;
                _userStateTypeFullName = userStateTypeFullName;
                _userStateObjectSelections = userStateObjectSelections;
            }

            internal static PersistedSelectionSnapshot Capture(DataVisualizerWindow window)
            {
                DataVisualizerSettings settings = ReadProperty<DataVisualizerSettings>(window, "Settings");
                DataVisualizerUserState userState = ReadProperty<DataVisualizerUserState>(window, "UserState");
                if (settings == null || userState == null)
                {
                    throw new InvalidOperationException(
                        "The DataVisualizer window exposes no Settings/UserState; the capture " + "driver cannot snapshot the persisted selection."
                    );
                }

                return new PersistedSelectionSnapshot(
                    settingsNamespaceKey: ReadMember<string>(settings, NamespaceKeyMember),
                    settingsTypeFullName: ReadMember<string>(settings, TypeFullNameMember),
                    settingsObjectSelections: CloneSelections(ReadMember<List<LastObjectSelectionEntry>>(settings, ObjectSelectionsMember)),
                    userStateNamespaceKey: userState.lastSelectedNamespaceKey,
                    userStateTypeFullName: userState.lastSelectedTypeFullName,
                    userStateObjectSelections: CloneSelections(userState.lastObjectSelections)
                );
            }

            private static T ReadProperty<T>(object target, string propertyName)
            {
                PropertyInfo property = target.GetType().GetProperty(propertyName, ReflectedInstanceMembers);
                return (T)property?.GetValue(target);
            }

            private static T ReadMember<T>(object target, string memberName)
            {
                FieldInfo field = target.GetType().GetField(memberName, ReflectedInstanceMembers);
                return (T)field?.GetValue(target);
            }

            private static void SetMember(object target, string memberName, object value)
            {
                FieldInfo field = target.GetType().GetField(memberName, ReflectedInstanceMembers);
                field?.SetValue(target, value);
            }

            private static List<LastObjectSelectionEntry> CloneSelections(List<LastObjectSelectionEntry> entries)
            {
                List<LastObjectSelectionEntry> clones = new();
                if (entries == null)
                {
                    return clones;
                }

                foreach (LastObjectSelectionEntry entry in entries)
                {
                    clones.Add(entry?.Clone());
                }

                return clones;
            }

            internal void Restore(DataVisualizerWindow window)
            {
                MethodInfo persistSettings = typeof(DataVisualizerWindow).GetMethod(
                    "PersistSettings",
                    ReflectedInstanceMembers,
                    binder: null,
                    types: new[] { typeof(Func<DataVisualizerSettings, bool>), typeof(Func<DataVisualizerUserState, bool>) },
                    modifiers: null
                );
                if (persistSettings == null)
                {
                    throw new InvalidOperationException(
                        "The DataVisualizer window exposes no PersistSettings member; the " + "capture driver cannot restore the persisted selection."
                    );
                }

                Func<DataVisualizerSettings, bool> settingsApplier = settings =>
                {
                    SetMember(settings, NamespaceKeyMember, _settingsNamespaceKey);
                    SetMember(settings, TypeFullNameMember, _settingsTypeFullName);
                    SetMember(settings, ObjectSelectionsMember, CloneSelections(_settingsObjectSelections));
                    return true;
                };
                Func<DataVisualizerUserState, bool> userStateApplier = userState =>
                {
                    userState.lastSelectedNamespaceKey = _userStateNamespaceKey;
                    userState.lastSelectedTypeFullName = _userStateTypeFullName;
                    userState.lastObjectSelections = CloneSelections(_userStateObjectSelections);
                    return true;
                };

                persistSettings.Invoke(window, new object[] { settingsApplier, userStateApplier });
            }
        }
    }
}
