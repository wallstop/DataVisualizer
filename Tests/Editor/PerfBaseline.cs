namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;
    using Debug = UnityEngine.Debug;
    using PlayModeDataObject = WallstopStudios.DataVisualizer.Tests.Runtime.PlayModeDataObject;

    /*
        Standing performance-baseline driver for the T01 harness slice of #114. It
        stages deterministic PlayModeDataObject fixture datasets (100 / 1,000 /
        10,000 assets), then measures, with monotonic Stopwatch timers inside the
        editor so MCP transport never pollutes a sample:

        - fixture creation calls, final refresh, total staging wall time, and
          longest creation call / staging tick (creation yields between ticks);
        - window-open-to-index-ready, cold (the first open after staging) and
          warm (repeated opens of the same already-imported dataset), where
          "ready" is the production _isSearchCachePopulated flag the async
          batch loader flips when the last batch lands;
        - first-results search latency against the populated cache, driven
          through the production search field so the change callback runs;
        - selection latency through the production SelectObject path.

        The run is a tick-driven state machine: every wait yields back to
        EditorApplication.update so the window's scheduled batches advance on
        real editor ticks, exactly as they would for a user. A watchdog fails
        the run closed with owned fixtures and the update hook removed when a
        phase never completes. Individual Unity calls cannot be interrupted.

        Results land in one JSON report (schema 1) plus a compact console
        summary. The report records the host environment (Unity, OS, CPU,
        memory, DPI, displays, theme, package version, pre-existing managed
        assets) so a number is meaningless without its context, and it records
        the repetition counts: cold is five windows in one warm process, warm is
        five discarded warm-ups plus thirty samples, and the
        remaining T01 acceptance (cross-host spread, Play-entry and
        compile timings) stays open on the roadmap.

        The window's Instance field, Cleanup, LoadInitialContent, SelectObject,
        the namespace controller, the search popover elements, and the
        PositionAndDisplayPopover step are private with no public equivalent,
        and Unity's compilation does not honor InternalsVisibleTo for the
        editor-to-tests assembly pair; they are reached reflectively, exactly
        like the merged DocsImageCapture and EditorSurfaceCaptureTests, and a
        Unity upgrade that removes one fails closed with an exception.
    */
    public static class PerfBaseline
    {
        internal const string SchemaVersion = "1";

        private const string OutputArgument = "-perfBaselineOutputDir";
        private const string OutputEnvironmentVariable = "DATAVISUALIZER_PERF_BASELINE_DIR";
        private const string DefaultOutputDirectory = "Temp/PerfBaseline";
        private const string PackagePath = "Packages/com.wallstop-studios.data-visualizer/package.json";

        private const int ColdRepetitions = 5;
        private const int WarmupRepetitions = 5;
        private const int WarmRepetitions = 30;
        private const int SearchRepetitions = 30;
        private const int SelectionRepetitions = 30;
        private const int InteractionRepetitions = 30;
        private const int SearchCacheTimeoutSeconds = 600;
        private const double DefaultTimeoutMinutes = 90;
        private const string SearchQueryFirst = "Player";
        private const string SearchQuerySecond = "World";

        private const string InstanceFieldName = "Instance";
        private const string SearchPopoverFieldName = "_searchPopover";
        private const string SearchTriggerFieldName = "_searchField";
        private const string SearchFieldElementName = "global-search-field";
        private const string SearchCachePopulatedFieldName = "_isSearchCachePopulated";
        private const int SearchKindCount = 4;

        private static readonly int[] FixtureSizes = { 100, 1000, 10000 };

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

        private static readonly BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static RunState _active;

        /*
            Batch-mode entry point for
            `unity -batchmode -quit -executeMethod
            WallstopStudios.DataVisualizer.Tests.Editor.PerfBaseline.RunFromCommandLine`.
            The output directory comes from `-perfBaselineOutputDir <path>` or the
            DATAVISUALIZER_PERF_BASELINE_DIR environment variable; failures throw so
            the batch exit code reports them.
        */
        public static void RunFromCommandLine()
        {
            string outputDirectory = ResolveOutputDirectory(Environment.GetCommandLineArgs(), Environment.GetEnvironmentVariable(OutputEnvironmentVariable));
            RunBaseline(outputDirectory);
        }

        /*
            Starts the measurement run. The run advances on editor ticks, so a host
            driving this through the Unity MCP bridge starts it, polls for the
            report file, and reads the JSON when the summary announces completion;
            every measured duration lives inside this editor process.
        */
        public static void RunBaseline(
            string outputDirectory,
            int[] fixtureSizes = null,
            double timeoutMinutes = DefaultTimeoutMinutes,
            Type[] fixtureTypes = null,
            double stagingBudgetMilliseconds = 4,
            int maximumStagingItems = 10
        )
        {
            if (_active != null)
            {
                throw new InvalidOperationException("A performance-baseline run is already active in this editor.");
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Run the performance baseline in Edit Mode.");
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException("The output directory must not be empty.", nameof(outputDirectory));
            }

            if (double.IsNaN(timeoutMinutes) || double.IsInfinity(timeoutMinutes) || timeoutMinutes <= 0 || 1440 < timeoutMinutes)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMinutes), "Choose a finite run budget greater than zero and no longer than 24 hours.");
            }

            if (double.IsNaN(stagingBudgetMilliseconds) || double.IsInfinity(stagingBudgetMilliseconds) || stagingBudgetMilliseconds <= 0 || maximumStagingItems < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(stagingBudgetMilliseconds), "Use a finite positive staging budget and a positive item limit.");
            }

            int[] sizes = fixtureSizes == null ? (int[])FixtureSizes.Clone() : (int[])fixtureSizes.Clone();
            if (sizes.Length == 0)
            {
                throw new ArgumentException("At least one fixture size is required.", nameof(fixtureSizes));
            }

            foreach (int size in sizes)
            {
                if (size < 2 || 100000 < size)
                {
                    throw new ArgumentOutOfRangeException(nameof(fixtureSizes), "Fixture sizes must be between 2 and 100,000 so both search terms have fixtures.");
                }
            }

            // Validate type distributions before subscribing or creating an asset folder.
            using PerfFixtureStaging validation = new(sizes[0], fixtureTypes: fixtureTypes);
            Type[] types = new Type[validation.FixtureTypes.Count];
            for (int index = 0; index < types.Length; index++)
            {
                types[index] = validation.FixtureTypes[index];
            }
            foreach (int size in sizes)
            {
                if (size < types.Length)
                {
                    throw new ArgumentException("Each size needs at least one asset per fixture type.", nameof(fixtureSizes));
                }
            }
            RunState state = new(outputDirectory, sizes, timeoutMinutes, types, stagingBudgetMilliseconds, maximumStagingItems);
            _active = state;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            EditorApplication.quitting += Cancel;
            EditorApplication.playModeStateChanged += CancelForPlayMode;
            Debug.Log($"[PerfBaseline] run started; report will be written to {outputDirectory}.");
        }

        public static string Status()
        {
            RunState state = _active;
            return state == null ? "Idle" : $"{state.Phase}: {state.Staging?.CreatedCount ?? 0}/{state.Current?.Size ?? 0} fixtures";
        }

        public static void Cancel()
        {
            RunState state = _active;
            if (state != null)
            {
                FinishWithError(state, "Cancelled before completion.", cancelled: true);
            }
        }

        internal static string ResolveOutputDirectory(string[] arguments, string environmentValue)
        {
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], OutputArgument, StringComparison.Ordinal))
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

        internal static double Median(List<double> samples)
        {
            if (samples == null || samples.Count == 0)
            {
                return 0;
            }

            List<double> sorted = new(samples);
            sorted.Sort();
            int middle = sorted.Count / 2;
            if (sorted.Count % 2 == 1)
            {
                return sorted[middle];
            }

            return (sorted[middle - 1] + sorted[middle]) / 2.0;
        }

        internal static string JsonEscape(string value)
        {
            StringBuilder escaped = new(value.Length);
            foreach (char character in value)
            {
                switch (character)
                {
                    case '\\':
                        escaped.Append("\\\\");
                        break;
                    case '"':
                        escaped.Append("\\\"");
                        break;
                    case '\n':
                        escaped.Append("\\n");
                        break;
                    case '\r':
                        escaped.Append("\\r");
                        break;
                    case '\t':
                        escaped.Append("\\t");
                        break;
                    default:
                        if (character < ' ')
                        {
                            escaped.Append("\\u");
                            escaped.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            escaped.Append(character);
                        }

                        break;
                }
            }

            return escaped.ToString();
        }

        internal static string FixtureId(int index)
        {
            return "perf-fixture-" + index.ToString("D6", CultureInfo.InvariantCulture);
        }

        internal static string FixtureSearchText(int index)
        {
            return "perf検索é" + index.ToString("D6", CultureInfo.InvariantCulture);
        }

        internal static string FixtureName(int index)
        {
            string title = FixtureTitles[index % FixtureTitles.Length];
            int cycle = index / FixtureTitles.Length;
            return cycle == 0 ? title : $"{title} {cycle:D6}";
        }

        internal static string BuildEnvironmentJson()
        {
            return new StringBuilder(1024)
                .Append("{\"unityVersion\": \"")
                .Append(JsonEscape(Application.unityVersion))
                .Append("\"")
                .Append(", \"platform\": \"")
                .Append(JsonEscape(Application.platform.ToString()))
                .Append("\"")
                .Append(", \"operatingSystem\": \"")
                .Append(JsonEscape(SystemInfo.operatingSystem))
                .Append("\"")
                .Append(", \"processorType\": \"")
                .Append(JsonEscape(SystemInfo.processorType))
                .Append("\"")
                .Append(", \"processorCount\": ")
                .Append(SystemInfo.processorCount)
                .Append(", \"systemMemoryMegabytes\": ")
                .Append(SystemInfo.systemMemorySize)
                .Append(", \"graphicsDevice\": \"")
                .Append(JsonEscape(SystemInfo.graphicsDeviceName))
                .Append("\"")
                .Append(", \"dpi\": ")
                .Append(Screen.dpi.ToString("0.#", CultureInfo.InvariantCulture))
                .Append(", \"editorPixelsPerPoint\": ")
                .Append(EditorGUIUtility.pixelsPerPoint.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(", \"darkTheme\": ")
                .Append(EditorGUIUtility.isProSkin ? "true" : "false")
                .Append(", \"displayCount\": ")
                .Append(Display.displays != null ? Display.displays.Length : 0)
                .Append(", \"packageVersion\": \"")
                .Append(JsonEscape(ReadPackageVersion()))
                .Append("\"")
                .Append(", \"preExistingScriptableObjectAssets\": ")
                .Append(CountScriptableObjectAssets())
                .Append("}")
                .ToString();
        }

        internal static int CountScriptableObjectAssets()
        {
            try
            {
                int count = 0;
                foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (_active?.Staging == null || !path.StartsWith(_active.Staging.Folder + "/", StringComparison.Ordinal))
                    {
                        count++;
                    }
                }

                return count;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static void CancelForPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                Cancel();
            }
        }

        private static void Tick()
        {
            RunState state = _active;
            if (state == null)
            {
                EditorApplication.update -= Tick;
                return;
            }

            try
            {
                if (state.Phase == Phase.Finished)
                {
                    Finish(state);
                    return;
                }
                if (state.Watchdog.Elapsed > TimeSpan.FromMinutes(state.TimeoutMinutes))
                {
                    FinishWithError(state, $"the run exceeded the {state.TimeoutMinutes}-minute watchdog in phase {state.Phase}.");
                    return;
                }

                SizeResult owner = state.Current;
                double now = state.Watchdog.Elapsed.TotalMilliseconds;
                if (state.Current != null && 0 < state.LastTickMilliseconds)
                {
                    state.Current.LongestObservedEditorUpdateGapMilliseconds = Math.Max(state.Current.LongestObservedEditorUpdateGapMilliseconds, now - state.LastTickMilliseconds);
                }
                state.LastTickMilliseconds = now;
                Stopwatch tick = Stopwatch.StartNew();
                Advance(state);
                SizeResult measured = owner ?? state.Current;
                if (measured != null)
                {
                    measured.LongestDriverTickMilliseconds = Math.Max(measured.LongestDriverTickMilliseconds, tick.Elapsed.TotalMilliseconds);
                }
            }
            catch (Exception error)
            {
                FinishWithError(state, $"phase {state.Phase} threw: {error.GetType().Name}: {error.Message}");
            }
        }

        private static void Advance(RunState state)
        {
            switch (state.Phase)
            {
                case Phase.Idle:
                    BeginNextSize(state);
                    return;
                case Phase.Staging:
                    StageFixtures(state);
                    return;
                case Phase.RefreshingFixtures:
                    RefreshFixtures(state);
                    return;
                case Phase.WaitingColdReady:
                case Phase.WaitingWarmReady:
                    WaitSearchCacheReady(state);
                    return;
                case Phase.InteractionSamples:
                    RunInteractionSample(state);
                    return;
                case Phase.WaitingLabelFilter:
                case Phase.WaitingObjectScroll:
                case Phase.WaitingTypeSwitch:
                    WaitInteraction(state);
                    return;
                case Phase.WaitingSearchResults:
                    WaitSearchResults(state);
                    return;
                case Phase.BetweenWarmRepetitions:
                    AdvanceWarmCycle(state);
                    return;
                case Phase.ColdOpen:
                    OpenWindow(state, cold: true);
                    return;
                case Phase.WarmOpen:
                    OpenWindow(state, cold: false);
                    return;
                case Phase.Finished:
                    return;
            }
        }

        private static void BeginNextSize(RunState state)
        {
            state.SizeIndex++;
            if (state.Sizes.Length <= state.SizeIndex)
            {
                state.Staging?.Dispose();
                // Publish on the next tick, after this dataset's teardown time is recorded.
                state.LastTickMilliseconds = 0;
                state.Phase = Phase.Finished;
                return;
            }

            int size = state.Sizes[state.SizeIndex];
            SizeResult result = new() { Size = size, FixtureTypes = state.FixtureTypes };
            state.Current = result;
            state.LastTickMilliseconds = 0;
            state.Results.Add(result);

            state.Staging?.Dispose();
            state.Staging = new PerfFixtureStaging(size, fixtureTypes: state.FixtureTypes);
            state.Phase = Phase.Staging;
        }

        private static void StageFixtures(RunState state)
        {
            state.Staging.Advance(state.StagingBudgetMilliseconds, state.MaximumStagingItems);
            if (state.Staging.CreatedCount == state.Current.Size)
            {
                state.Phase = Phase.RefreshingFixtures;
            }

            EditorApplication.QueuePlayerLoopUpdate();
        }

        private static void RefreshFixtures(RunState state)
        {
            state.Staging.Refresh();
            state.Current.CreationMilliseconds = state.Staging.CreationMilliseconds;
            state.Current.BatchImportMilliseconds = state.Staging.BatchImportMilliseconds;
            state.Current.InitializationMilliseconds = state.Staging.InitializationMilliseconds;
            state.Current.LongestBatchImportMilliseconds = state.Staging.LongestBatchImportMilliseconds;
            state.Current.ImportMilliseconds = state.Staging.RefreshMilliseconds;
            state.Current.StagingMilliseconds = state.Staging.TotalMilliseconds;
            state.Current.LongestCreateMilliseconds = state.Staging.LongestCreateMilliseconds;
            state.Current.LongestSliceMilliseconds = state.Staging.LongestSliceMilliseconds;
            state.Current.FixtureComplete = true;
            OpenWindow(state, cold: true);
        }

        private static void OpenWindow(RunState state, bool cold)
        {
            state.OpenClock.Restart();
            PerfReadinessSample sample = new()
            {
                size = state.Current.Size,
                mode =
                    cold ? "cold-window-warm-process"
                    : state.WarmRepetition < WarmupRepetitions ? "warmup"
                    : "warm",
            };
            state.Readiness = sample;
            state.Current.ReadinessSamples.Add(sample);
            if (state.Preferences == null)
            {
                state.PreviousSelection = Selection.objects;
                state.Preferences = DocsImageCapture.WindowPreferenceSnapshot.Capture();
            }
            state.Preferences.ApplyWidePanes();
            FieldInfo instanceField = typeof(DataVisualizerWindow).GetField(InstanceFieldName, StaticMembers);
            if (instanceField == null)
            {
                throw new InvalidOperationException("The DataVisualizer window exposes no Instance field; the baseline driver cannot preserve the host's window instance.");
            }

            state.PreviousInstance = instanceField.GetValue(null);
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            state.Window = window;
            DataVisualizerSettings settings = typeof(DataVisualizerWindow).GetProperty("Settings", Members)?.GetValue(window) as DataVisualizerSettings;
            DataVisualizerUserState userState = typeof(DataVisualizerWindow).GetProperty("UserState", Members)?.GetValue(window) as DataVisualizerUserState;
            state.WindowState = new PerfWindowStateScope(settings, userState, state.OutputDirectory);
            foreach (Type type in state.FixtureTypes)
            {
                if (!state.WindowState.UserState.managedTypeNames.Contains(type.FullName))
                {
                    state.WindowState.UserState.managedTypeNames.Add(type.FullName);
                }
                List<string> order = state.WindowState.UserState.GetOrCreateTypeOrderList(NamespaceController.GetNamespaceKey(type));
                if (!order.Contains(type.FullName))
                {
                    order.Add(type.FullName);
                }
            }
            state.WindowState.UserState.labelFilterConfigs.Clear();
            state.WindowState.UserState.namespaceCollapseStates.Clear();
            // Set the owned path first so even a later setup failure cannot save into the host file.
            SetWindowField(window, "_userStateFilePath", state.WindowState.FilePath);
            SetWindowField(window, "_userState", state.WindowState.UserState);
            SetWindowField(window, "_settings", state.WindowState.Settings);
            window.rootVisualElement.generateVisualContent += _ =>
            {
                if (sample.firstRootPaintMilliseconds < 0 && ReferenceEquals(state.Window, window))
                {
                    sample.firstRootPaintMilliseconds = state.OpenClock.Elapsed.TotalMilliseconds;
                }
                PerfInteractionSample interaction = state.PendingInteraction;
                if (interaction != null && !interaction.complete && interaction.firstRootPaintMilliseconds < 0 && ReferenceEquals(state.Window, window))
                {
                    interaction.firstRootPaintMilliseconds = state.PhaseClock.Elapsed.TotalMilliseconds;
                }
            };
            window.ShowPopup();
            // Show may enable the window again; restore owned state before deferred initialization.
            SetWindowField(window, "_userStateFilePath", state.WindowState.FilePath);
            SetWindowField(window, "_userState", state.WindowState.UserState);
            SetWindowField(window, "_settings", state.WindowState.Settings);
            window.position = new Rect(40f, 40f, 1180f, 780f);
            state.PhaseClock.Restart();
            window.CreateGUI();
            InvokeInitialContentLoad(window);
            NamespaceController controller = GetNamespaceController(window);
            controller.SelectType(window, state.FixtureTypes[0]);
            if (controller.SelectedType != state.FixtureTypes[0])
            {
                throw new InvalidOperationException("The benchmark did not select its configured fixture type.");
            }
            state.Phase = cold ? Phase.WaitingColdReady : Phase.WaitingWarmReady;
        }

        private static void WaitSearchCacheReady(RunState state)
        {
            if (state.PhaseClock.Elapsed > TimeSpan.FromSeconds(SearchCacheTimeoutSeconds))
            {
                throw new InvalidOperationException($"the search cache did not become ready within {SearchCacheTimeoutSeconds}s at size {state.Current.Size}.");
            }

            ObserveReadiness(state);
            if (!IsSearchCachePopulated(state.Window))
            {
                /*
                    The batch loader advances on editor updates; a backgrounded host
                    ticks at a throttled rate that would stretch one 1,000-asset load
                    into minutes. QueuePlayerLoopUpdate keeps the cadence the window
                    gets when a user is actually looking at it, which is the behavior
                    being measured.
                */
                EditorApplication.QueuePlayerLoopUpdate();
                return; // the next editor tick continues the async batch load
            }

            PerfReadinessSample sample = state.Readiness;
            if (sample.legacyIndexReadyMilliseconds < 0)
            {
                sample.legacyIndexReadyMilliseconds = state.PhaseClock.Elapsed.TotalMilliseconds;
            }
            if (
                !AssetGuidTypeIndex.Shared.IsComplete
                || 0 < AssetGuidTypeIndex.Shared.PendingPathCount
                || (bool)typeof(DataVisualizerWindow).GetField("_needsRefresh", Members).GetValue(state.Window)
                || (bool)typeof(DataVisualizerWindow).GetField("_isLoadingObjectsAsync", Members).GetValue(state.Window)
                || sample.firstRootPaintMilliseconds < 0
                || sample.firstUsableRowsMilliseconds < 0
                || sample.inspectorReadyMilliseconds < 0
            )
            {
                EditorApplication.QueuePlayerLoopUpdate();
                return;
            }
            sample.openToIndexMilliseconds = state.OpenClock.Elapsed.TotalMilliseconds;
            sample.complete = true;
            state.PhaseClock.Stop();
            double readyMilliseconds = sample.legacyIndexReadyMilliseconds;
            if (state.Phase == Phase.WaitingColdReady)
            {
                state.Current.ColdOpenSamples.Add(readyMilliseconds);
                state.Current.ColdOpenMilliseconds = state.Current.ColdOpenSamples[0];
                if (state.Current.ColdOpenSamples.Count < ColdRepetitions)
                {
                    CloseWindow(state);
                    state.Phase = Phase.ColdOpen;
                    return;
                }
                state.SearchKind = 0;
                state.SearchRepetition = 0;
                state.SelectionRepetition = 0;
                state.FilterRepetition = 0;
                state.ScrollRepetition = 0;
                state.TypeSwitchRepetition = 0;
                state.Phase = Phase.InteractionSamples;
            }
            else
            {
                if (state.WarmRepetition >= WarmupRepetitions)
                {
                    state.Current.WarmOpenSamples.Add(readyMilliseconds);
                }

                state.Phase = Phase.BetweenWarmRepetitions;
            }
        }

        private static void ObserveReadiness(RunState state)
        {
            Stopwatch observer = Stopwatch.StartNew();
            PerfReadinessSample sample = state.Readiness;
            DataVisualizerWindow window = state.Window;
            int realized = 0;
            window
                .rootVisualElement.Query<VisualElement>(className: "object-item")
                .ForEach(element =>
                {
                    if (0 < element.worldBound.width && 0 < element.worldBound.height && element.parent?.userData is ScriptableObject asset && asset != null)
                    {
                        realized++;
                    }
                });
            sample.realizedRows = Math.Max(sample.realizedRows, realized);
            double elapsed = state.OpenClock.Elapsed.TotalMilliseconds;
            if (0 < realized && sample.firstUsableRowsMilliseconds < 0)
            {
                sample.firstUsableRowsMilliseconds = elapsed;
            }
            if (
                sample.inspectorReadyMilliseconds < 0
                && typeof(DataVisualizerWindow).GetField("_currentInspectorScriptableObject", Members)?.GetValue(window) is SerializedObject inspector
                && inspector.targetObject != null
                && window.rootVisualElement.Q("inspector-content") is VisualElement content
                && 0 < content.childCount
                && 0 < content.worldBound.width
                && 0 < content.worldBound.height
            )
            {
                sample.inspectorReadyMilliseconds = elapsed;
            }
            sample.loadedRows = (typeof(DataVisualizerWindow).GetField("_selectedObjects", Members)?.GetValue(window) as System.Collections.ICollection)?.Count ?? 0;
            sample.retainedSearchObjects = window.RetainedSearchObjectCount;
            sample.managedBytes = Math.Max(sample.managedBytes, GC.GetTotalMemory(false));
            sample.unityAllocatedBytes = Math.Max(sample.unityAllocatedBytes, UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong());
            sample.unityReservedBytes = Math.Max(sample.unityReservedBytes, UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong());
            sample.longestIndexSliceMilliseconds = window.LongestSearchIndexSliceMilliseconds;
            sample.indexSliceOverruns = window.SearchIndexSliceOverrunCount;
            sample.observations++;
            sample.observerMilliseconds += observer.Elapsed.TotalMilliseconds;
        }

        private static void RunInteractionSample(RunState state)
        {
            if (state.SearchKind < SearchKindCount)
            {
                if (state.SearchRepetition == WarmupRepetitions + SearchRepetitions)
                {
                    state.SearchKind++;
                    state.SearchRepetition = 0;
                }
                if (state.SearchKind < SearchKindCount)
                {
                    MeasureSearch(state, state.SearchRepetition++);
                    return;
                }
            }

            if (state.SelectionRepetition < WarmupRepetitions + SelectionRepetitions)
            {
                MeasureSelection(state, state.SelectionRepetition++);
                return;
            }

            if (state.FilterRepetition < WarmupRepetitions + InteractionRepetitions)
            {
                StartLabelFilter(state, state.FilterRepetition++);
                return;
            }
            if (state.ScrollRepetition < WarmupRepetitions + InteractionRepetitions)
            {
                if (state.ScrollRepetition == 0)
                {
                    ApplyMeasuredLabelFilter(state.Window, null);
                }
                StartObjectScroll(state, state.ScrollRepetition++);
                return;
            }
            if (1 < state.FixtureTypes.Length && state.TypeSwitchRepetition < WarmupRepetitions + InteractionRepetitions)
            {
                StartTypeSwitch(state, state.TypeSwitchRepetition++);
                return;
            }

            /*
                The interaction samples are done; close the cold-opened window before
                the warm-open cycle so exactly one driver window is ever alive. A
                leaked window keeps its scheduled batch loader running and compounds
                every later AssetDatabase storm.
            */
            CloseWindow(state);
            state.WarmRepetition = 0;
            state.Phase = Phase.WarmOpen;
        }

        private static void MeasureSearch(RunState state, int repetition)
        {
            string query;
            string kind;
            int expectedMatches;
            switch (state.SearchKind)
            {
                case 0:
                    kind = "common-name";
                    query = repetition % 2 == 0 ? SearchQueryFirst : SearchQuerySecond;
                    expectedMatches = -1;
                    break;
                case 1:
                    kind = "rare-guid";
                    query = AssetDatabase.AssetPathToGUID($"{state.Staging.Folder}/{FixtureName(state.Current.Size - 1)}.asset");
                    expectedMatches = 1;
                    break;
                case 2:
                    kind = "rare-field";
                    int fieldIndex = -1;
                    for (int index = 0; index < state.FixtureTypes.Length; index++)
                    {
                        Type type = state.FixtureTypes[index];
                        if (typeof(BaseDataObject).IsAssignableFrom(type) || type == typeof(OtherEditorOnlyCreationData))
                        {
                            fieldIndex = index;
                            break;
                        }
                    }
                    if (fieldIndex < 0)
                    {
                        state.SearchRepetition = WarmupRepetitions + SearchRepetitions;
                        return;
                    }
                    query = FixtureSearchText(fieldIndex);
                    expectedMatches = 1;
                    break;
                default:
                    kind = "no-match";
                    query = state.MissingLabel;
                    expectedMatches = 0;
                    break;
            }
            if (string.IsNullOrEmpty(query))
            {
                throw new InvalidOperationException("The search condition has no query text: " + kind);
            }
            TextField searchField = FindSearchField(state.Window);
            // Clear outside the clock so every repetition starts a new query generation.
            searchField.value = string.Empty;
            PerfSearchSample sample = new()
            {
                size = state.Current.Size,
                kind = kind,
                mode = repetition < WarmupRepetitions ? "warmup" : "warm",
                query = query,
                expectedMatches = expectedMatches,
            };
            state.Current.SearchObservations.Add(sample);
            state.PendingSearch = sample;
            state.QuerySliceOverrunsBefore = state.Window.SearchQuerySliceOverrunCount;
            state.PhaseClock.Restart();
            searchField.value = query;
            sample.actionMilliseconds = state.PhaseClock.Elapsed.TotalMilliseconds;
            state.Phase = Phase.WaitingSearchResults;
        }

        private static void WaitSearchResults(RunState state)
        {
            PerfSearchSample sample = state.PendingSearch;
            DataVisualizerWindow window = state.Window;
            if (state.PhaseClock.Elapsed > TimeSpan.FromSeconds(30))
            {
                throw new InvalidOperationException($"the search '{sample.kind}' did not complete within 30s at size {state.Current.Size}.");
            }
            if (!string.Equals(window.ActiveSearchText, sample.query, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The active query no longer matches the measured input.");
            }
            sample.generation = window.ActiveSearchGeneration;
            sample.records = window.ActiveSearchRecordCount;
            sample.scanned = window.ActiveSearchScannedCount;
            sample.matches = window.ActiveSearchMatchCount;
            sample.visibleMatches = window.ActiveSearchPreviewRowCount;
            sample.reachedLimit = window.ActiveSearchReachedLimit;
            sample.indexComplete = window.IsSearchIndexComplete;
            sample.sliceOverruns = window.SearchQuerySliceOverrunCount - state.QuerySliceOverrunsBefore;
            sample.longestWindowQuerySliceMilliseconds = window.LongestSearchQuerySliceMilliseconds;
            if (0 < sample.visibleMatches && sample.firstResultMilliseconds < 0)
            {
                sample.firstResultMilliseconds = state.PhaseClock.Elapsed.TotalMilliseconds;
            }
            if (!window.IsActiveSearchComplete || !sample.indexComplete)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                return;
            }
            sample.completedMilliseconds = state.PhaseClock.Elapsed.TotalMilliseconds;
            if ((0 <= sample.expectedMatches && sample.matches != sample.expectedMatches) || (sample.expectedMatches < 0 && sample.matches == 0))
            {
                throw new InvalidOperationException($"The {sample.kind} search returned {sample.matches} unexpected matches.");
            }
            if (sample.visibleMatches != sample.matches)
            {
                throw new InvalidOperationException($"The {sample.kind} publication could not resolve every visible match.");
            }
            sample.complete = true;
            if (sample.kind == "common-name" && sample.mode == "warm")
            {
                state.Current.SearchSamples.Add(sample.firstResultMilliseconds);
                state.Current.SearchMatchCounts.Add(sample.matches);
            }
            state.PendingSearch = null;
            state.Phase = Phase.InteractionSamples;
        }

        private static void MeasureSelection(RunState state, int repetition)
        {
            int typeCount = state.FixtureTypes.Length;
            int assetsOfFirstType = (state.Current.Size + typeCount - 1) / typeCount;
            int assetIndex = ((repetition * 997 + assetsOfFirstType / 2) % assetsOfFirstType) * typeCount;
            string assetPath = $"{state.Staging.Folder}/{FixtureName(assetIndex)}.asset";
            ScriptableObject asset = AssetDatabase.LoadMainAssetAtPath(assetPath) as ScriptableObject;
            if (asset == null)
            {
                throw new InvalidOperationException($"The fixture asset '{assetPath}' did not load; selection cannot be measured.");
            }

            MethodInfo selectObject = typeof(DataVisualizerWindow).GetMethod("SelectObject", Members, binder: null, types: new[] { typeof(ScriptableObject) }, modifiers: null);
            if (selectObject == null)
            {
                throw new InvalidOperationException("The DataVisualizer window exposes no SelectObject member; selection cannot be measured.");
            }

            state.PhaseClock.Restart();
            selectObject.Invoke(state.Window, new object[] { asset });
            state.PhaseClock.Stop();
            if (WarmupRepetitions <= repetition)
            {
                state.Current.SelectionSamples.Add(state.PhaseClock.Elapsed.TotalMilliseconds);
            }
        }

        private static PerfInteractionSample StartInteraction(RunState state, string kind, int repetition, Phase phase)
        {
            PerfInteractionSample sample = new()
            {
                size = state.Current.Size,
                kind = kind,
                mode = repetition < WarmupRepetitions ? "warmup" : "warm",
                selectedType = GetNamespaceController(state.Window).SelectedType.AssemblyQualifiedName,
            };
            state.PendingInteraction = sample;
            state.Current.InteractionSamples.Add(sample);
            state.Phase = phase;
            state.PhaseClock.Restart();
            return sample;
        }

        private static void ApplyMeasuredLabelFilter(DataVisualizerWindow window, string missingLabel)
        {
            TypeLabelFilterConfig filter = (TypeLabelFilterConfig)typeof(DataVisualizerWindow).GetProperty("CurrentTypeLabelFilterConfig", Members).GetValue(window);
            filter.andLabels.Clear();
            filter.orLabels.Clear();
            filter.combinationType = LabelCombinationType.And;
            if (missingLabel != null)
            {
                filter.andLabels.Add(missingLabel);
            }
            typeof(DataVisualizerWindow).GetMethod("ApplyLabelFilter", Members).Invoke(window, new object[] { true });
        }

        private static void StartLabelFilter(RunState state, int repetition)
        {
            bool excludeAll = repetition % 2 == 0;
            int rows = (typeof(DataVisualizerWindow).GetField("_selectedObjects", Members).GetValue(state.Window) as System.Collections.ICollection).Count;
            PerfInteractionSample sample = StartInteraction(state, "label-filter", repetition, Phase.WaitingLabelFilter);
            sample.expectedRows = excludeAll ? 0 : rows;
            ApplyMeasuredLabelFilter(state.Window, excludeAll ? state.MissingLabel : null);
            sample.actionMilliseconds = state.PhaseClock.Elapsed.TotalMilliseconds;
            state.Window.rootVisualElement.MarkDirtyRepaint();
        }

        private static void StartObjectScroll(RunState state, int repetition)
        {
            ListView list = state.Window.rootVisualElement.Q<ListView>("object-listview");
            int target = repetition % 2 == 0 ? list.itemsSource.Count - 1 : 0;
            if (target < 0)
            {
                throw new InvalidOperationException("The scroll sample requires loaded object rows.");
            }
            state.PendingScrollTarget = (ScriptableObject)list.itemsSource[target];
            state.ScrollOffsetBefore = list.Q<ScrollView>().scrollOffset;
            PerfInteractionSample sample = StartInteraction(state, "object-scroll", repetition, Phase.WaitingObjectScroll);
            sample.expectedRows = list.itemsSource.Count;
            list.ScrollToItem(target);
            sample.actionMilliseconds = state.PhaseClock.Elapsed.TotalMilliseconds;
            state.Window.rootVisualElement.MarkDirtyRepaint();
        }

        private static void StartTypeSwitch(RunState state, int repetition)
        {
            NamespaceController controller = GetNamespaceController(state.Window);
            int next = (Array.IndexOf(state.FixtureTypes, controller.SelectedType) + 1) % state.FixtureTypes.Length;
            state.PendingType = state.FixtureTypes[next];
            PerfInteractionSample sample = StartInteraction(state, "type-switch", repetition, Phase.WaitingTypeSwitch);
            sample.selectedType = state.PendingType.AssemblyQualifiedName;
            controller.SelectType(state.Window, state.PendingType);
            sample.actionMilliseconds = state.PhaseClock.Elapsed.TotalMilliseconds;
            state.Window.rootVisualElement.MarkDirtyRepaint();
        }

        private static void WaitInteraction(RunState state)
        {
            if (30 < state.PhaseClock.Elapsed.TotalSeconds)
            {
                throw new InvalidOperationException("Interaction did not reach observed readiness: " + state.PendingInteraction.kind);
            }
            Stopwatch observer = Stopwatch.StartNew();
            PerfInteractionSample sample = state.PendingInteraction;
            ListView list = state.Window.rootVisualElement.Q<ListView>("object-listview");
            Rect viewport = list.Q<ScrollView>().contentViewport.worldBound;
            int realized = 0;
            int visible = 0;
            bool targetVisible = false;
            list.Query<VisualElement>(className: "object-item")
                .ForEach(box =>
                {
                    VisualElement row = box.parent;
                    if (row.userData is not ScriptableObject asset || asset == null || row.worldBound.width <= 0 || row.worldBound.height <= 0)
                    {
                        return;
                    }
                    realized++;
                    if (row.worldBound.Overlaps(viewport))
                    {
                        visible++;
                        targetVisible = targetVisible || ReferenceEquals(asset, state.PendingScrollTarget);
                    }
                });
            sample.realizedRows = Math.Max(sample.realizedRows, realized);
            sample.loadedRows = (typeof(DataVisualizerWindow).GetField("_selectedObjects", Members).GetValue(state.Window) as System.Collections.ICollection).Count;
            sample.retainedSearchObjects = state.Window.RetainedSearchObjectCount;
            bool ready = 0 < viewport.width && 0 < viewport.height;
            if (state.Phase == Phase.WaitingLabelFilter)
            {
                if (sample.expectedRows == 0)
                {
                    Label empty = state.Window.rootVisualElement.Q<Label>("empty-object-list-label");
                    ready =
                        list.itemsSource.Count == 0
                        && list.resolvedStyle.display == DisplayStyle.None
                        && empty.resolvedStyle.display != DisplayStyle.None
                        && 0 < empty.worldBound.width
                        && 0 < empty.worldBound.height;
                }
                else
                {
                    ready = ready && list.itemsSource.Count == sample.expectedRows && 0 < visible && list.resolvedStyle.display != DisplayStyle.None;
                }
            }
            else if (state.Phase == Phase.WaitingObjectScroll)
            {
                ready = ready && targetVisible;
                sample.viewportMoved = list.Q<ScrollView>().scrollOffset != state.ScrollOffsetBefore;
            }
            else
            {
                SerializedObject inspector = typeof(DataVisualizerWindow).GetField("_currentInspectorScriptableObject", Members).GetValue(state.Window) as SerializedObject;
                VisualElement content = state.Window.rootVisualElement.Q("inspector-content");
                ready =
                    ready
                    && GetNamespaceController(state.Window).SelectedType == state.PendingType
                    && !(bool)typeof(DataVisualizerWindow).GetField("_isLoadingObjectsAsync", Members).GetValue(state.Window)
                    && 0 < visible
                    && inspector != null
                    && inspector.targetObject != null
                    && inspector.targetObject.GetType() == state.PendingType
                    && content != null
                    && 0 < content.childCount
                    && 0 < content.worldBound.width
                    && 0 < content.worldBound.height;
            }
            sample.observerMilliseconds += observer.Elapsed.TotalMilliseconds;
            if (!ready || sample.firstRootPaintMilliseconds < 0)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                return;
            }
            sample.observedReadyMilliseconds = state.PhaseClock.Elapsed.TotalMilliseconds;
            sample.complete = true;
            if (sample.mode == "warm")
            {
                List<double> samples =
                    state.Phase == Phase.WaitingLabelFilter ? state.Current.FilterSamples
                    : state.Phase == Phase.WaitingObjectScroll ? state.Current.ScrollSamples
                    : state.Current.TypeSwitchSamples;
                samples.Add(sample.observedReadyMilliseconds);
            }
            state.PendingInteraction = null;
            state.PendingScrollTarget = null;
            state.Phase = Phase.InteractionSamples;
        }

        private static void AdvanceWarmCycle(RunState state)
        {
            CloseWindow(state);
            state.WarmRepetition++;
            if (state.WarmRepetition >= WarmupRepetitions + WarmRepetitions)
            {
                BeginNextSize(state);
                return;
            }

            state.Phase = Phase.WarmOpen;
        }

        private static void SetWindowField(DataVisualizerWindow window, string name, object value)
        {
            FieldInfo field = typeof(DataVisualizerWindow).GetField(name, Members);
            if (field == null)
            {
                throw new InvalidOperationException("The baseline cannot isolate window state: " + name);
            }
            field.SetValue(window, value);
        }

        private static void CloseWindow(RunState state)
        {
            DataVisualizerWindow window = state.Window;
            PerfWindowStateScope isolatedState = state.WindowState;
            state.Window = null;
            state.WindowState = null;
            state.PendingInteraction = null;
            state.PendingScrollTarget = null;
            // A destroyed wrapper still owns restoration; a run that never opened a window does not.
            if (ReferenceEquals(window, null) && isolatedState == null)
            {
                return;
            }
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() =>
            {
                typeof(DataVisualizerWindow).GetField(InstanceFieldName, StaticMembers)?.SetValue(null, state.PreviousInstance);
                state.PreviousInstance = null;
            });
            cleanup.Defer(AssetGuidTypeIndex.Shared.Cancel);
            cleanup.Defer(() => isolatedState?.Dispose());
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(window));
            if (window != null)
            {
                InvokeCleanup(window);
            }
        }

        private static void Finish(RunState state)
        {
            string json = BuildJson(state.Results, state.TimeoutMinutes, state.StagingBudgetMilliseconds, state.MaximumStagingItems);
            WriteReport(state.OutputDirectory, json);
            string summary = BuildSummary(state.Results);
            TearDown(state);
            Debug.Log($"[PerfBaseline] complete. {summary}");
        }

        private static void FinishWithError(RunState state, string message, bool cancelled = false)
        {
            if (cancelled)
            {
                Debug.Log($"[PerfBaseline] {message}");
            }
            else
            {
                Debug.LogError($"[PerfBaseline] FAILED: {message}");
            }
            try
            {
                WriteReport(state.OutputDirectory, BuildErrorJson(state, message));
            }
            catch (Exception writeError)
            {
                Debug.LogError($"[PerfBaseline] the error report could not be written: {writeError.Message}");
            }

            TearDown(state);
        }

        private static void TearDown(RunState state)
        {
            state.Phase = Phase.Finished;
            /*
                Every teardown step is best-effort on the error path: a failing
                window cleanup or fixture delete must never leave the update hook
                installed or the fixture folder behind, because the state machine
                runs inside EditorApplication.update where an exception would leave
                the next tick re-entering a dead run.
            */
            try
            {
                CloseWindow(state);
            }
            catch (Exception error)
            {
                Debug.LogError($"[PerfBaseline] window teardown failed: {error.Message}");
            }

            try
            {
                state.Staging?.Dispose();
            }
            catch (Exception error)
            {
                Debug.LogError($"[PerfBaseline] fixture cleanup failed: {error.Message}");
            }

            if (state.Preferences != null)
            {
                using (TestCleanupScope restoration = new())
                {
                    restoration.Defer(state.Preferences.Restore);
                    restoration.Defer(() => Selection.objects = state.PreviousSelection);
                }
            }
            _active = null;
            EditorApplication.update -= Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
            EditorApplication.quitting -= Cancel;
            EditorApplication.playModeStateChanged -= CancelForPlayMode;
        }

        private static bool IsSearchCachePopulated(DataVisualizerWindow window)
        {
            FieldInfo populatedField = typeof(DataVisualizerWindow).GetField(SearchCachePopulatedFieldName, Members);
            return populatedField != null && populatedField.GetValue(window) is bool populated && populated;
        }

        private static TextField FindSearchField(DataVisualizerWindow window)
        {
            /*
                The global search field lives inside the search popover, so the
                popover is revealed through the window's own production positioning
                step before the field is read; reveal cost lands outside the timed
                region, which starts at the typed value change.
            */
            VisualElement popover = ReadWindowElement(window, SearchPopoverFieldName);
            VisualElement trigger = ReadWindowElement(window, SearchTriggerFieldName);
            MethodInfo positionAndDisplay = typeof(DataVisualizerWindow).GetMethod(
                "PositionAndDisplayPopover",
                Members,
                binder: null,
                types: new[] { typeof(VisualElement), typeof(VisualElement), typeof(bool), typeof(bool) },
                modifiers: null
            );
            if (positionAndDisplay == null)
            {
                throw new InvalidOperationException("The DataVisualizer window exposes no PositionAndDisplayPopover member; search cannot be measured.");
            }

            positionAndDisplay.Invoke(window, new object[] { popover, trigger, false, false });
            if (window.rootVisualElement.Q(SearchFieldElementName) is TextField field)
            {
                return field;
            }

            throw new InvalidOperationException($"The DataVisualizer window exposes no '{SearchFieldElementName}' element; search cannot be measured.");
        }

        private static VisualElement ReadWindowElement(DataVisualizerWindow window, string fieldName)
        {
            FieldInfo field = typeof(DataVisualizerWindow).GetField(fieldName, Members);
            if (field?.GetValue(window) is not VisualElement element)
            {
                throw new InvalidOperationException($"The DataVisualizer window exposes no '{fieldName}' visual element; the baseline driver cannot continue.");
            }

            return element;
        }

        private static void InvokeInitialContentLoad(DataVisualizerWindow window)
        {
            MethodInfo loadInitialContent = typeof(DataVisualizerWindow).GetMethod("LoadInitialContent", Members, binder: null, types: Type.EmptyTypes, modifiers: null);
            if (loadInitialContent == null)
            {
                throw new InvalidOperationException("The DataVisualizer window exposes no LoadInitialContent member; the baseline driver cannot build the namespace tree.");
            }

            loadInitialContent.Invoke(window, null);
        }

        private static NamespaceController GetNamespaceController(DataVisualizerWindow window)
        {
            if (typeof(DataVisualizerWindow).GetField("_namespaceController", Members)?.GetValue(window) is not NamespaceController controller)
            {
                throw new InvalidOperationException("The DataVisualizer window exposes no namespace controller; the baseline driver cannot select the fixture type.");
            }

            return controller;
        }

        private static void InvokeCleanup(DataVisualizerWindow window)
        {
            MethodInfo cleanup = typeof(DataVisualizerWindow).GetMethod("Cleanup", Members, binder: null, types: Type.EmptyTypes, modifiers: null);
            if (cleanup == null)
            {
                throw new InvalidOperationException("The DataVisualizer window exposes no Cleanup member; the baseline driver cannot tear the window down.");
            }

            cleanup.Invoke(window, null);
        }

        private static string BuildSummary(List<SizeResult> results)
        {
            StringBuilder summary = new();
            foreach (SizeResult result in results)
            {
                summary.Append("size ");
                summary.Append(result.Size);
                summary.Append(": import ");
                summary.Append(FormatMs(result.ImportMilliseconds));
                summary.Append(", cold open ");
                summary.Append(FormatMs(result.ColdOpenMilliseconds));
                summary.Append(", warm open median ");
                summary.Append(FormatMs(Median(result.WarmOpenSamples)));
                summary.Append(", search median ");
                summary.Append(FormatMs(Median(result.SearchSamples)));
                summary.Append(", selection median ");
                summary.Append(FormatMs(Median(result.SelectionSamples)));
                summary.Append("; ");
            }

            return summary.ToString().TrimEnd(' ', ';');
        }

        private static void WriteReport(string outputDirectory, string json)
        {
            Directory.CreateDirectory(outputDirectory);
            string path = Path.Combine(outputDirectory, "perf-baseline.json");
            File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static string FormatMs(double milliseconds)
        {
            return milliseconds.ToString("0.###", CultureInfo.InvariantCulture) + " ms";
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string BuildJson(List<SizeResult> results, double timeoutMinutes, double stagingBudgetMilliseconds, int maximumStagingItems)
        {
            StringBuilder json = new(4096);
            json.Append("{\n  \"schema\": \"").Append(SchemaVersion).Append("\",\n");
            json.Append("  \"generatedAtUtc\": \"").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append("\",\n");
            json.Append("  \"environment\": ").Append(BuildEnvironmentJson()).Append(",\n");
            json.Append("  \"timeoutMinutes\": ").Append(FormatNumber(timeoutMinutes)).Append(",\n");
            json.Append("  \"stagingBudgetMilliseconds\": ").Append(FormatNumber(stagingBudgetMilliseconds)).Append(",\n");
            json.Append("  \"maximumStagingItems\": ").Append(maximumStagingItems).Append(",\n");
            json.Append("  \"repetitions\": {\"cold\": ")
                .Append(ColdRepetitions)
                .Append(", \"warmWarmups\": ")
                .Append(WarmupRepetitions)
                .Append(", \"warmSamples\": ")
                .Append(WarmRepetitions)
                .Append(", \"searchWarmups\": ")
                .Append(WarmupRepetitions)
                .Append(", \"selectionWarmups\": ")
                .Append(WarmupRepetitions)
                .Append(", \"searchSamples\": ")
                .Append(SearchRepetitions)
                .Append(", \"selectionSamples\": ")
                .Append(SelectionRepetitions)
                .Append(", \"interactionWarmups\": ")
                .Append(WarmupRepetitions)
                .Append(", \"interactionSamples\": ")
                .Append(InteractionRepetitions)
                .Append("},\n");
            json.Append(
                "  \"gaps\": [\"Play-entry and compile timings are not yet measured\", \"cold windows reuse the editor process; fresh-process controls are not measured\", \"cross-host spread is not yet collected\"],\n"
            );
            json.Append(
                "  \"readinessSemantics\": \"Root paint callback and first observed laid-out rows/inspector, not GPU completion. Full index readiness waits for shared type classification, selected rows and queued refreshes; the legacy clock retains its first window-cache-ready observation. Negative timings are unavailable. Memory values are separate peaks, not a sum. Editor update gaps include scheduling delays. Cold windows share a warm editor process.\",\n"
            );
            json.Append("  \"readinessSamples\": [");
            bool firstReadiness = true;
            foreach (SizeResult result in results)
            {
                foreach (PerfReadinessSample sample in result.ReadinessSamples)
                {
                    if (!firstReadiness)
                    {
                        json.Append(",");
                    }
                    firstReadiness = false;
                    json.Append(JsonUtility.ToJson(sample));
                }
            }
            json.Append("],\n");
            json.Append(
                "  \"interactionSemantics\": \"Five warmups then 30 warm observations per interaction; root paint is not GPU completion; readiness is observed layout, with observer delay. Small scrolls can be no-ops. Type switching requires at least two fixture types.\",\n"
            );
            json.Append(
                "  \"searchSemantics\": \"Five warmups and 30 warm samples per supported condition on a complete index. First result and current snapshot completion include field input and editor observation delay, not GPU paint. No-match first result is -1. Common names may reach the result cap. Rare fields require BaseDataObject or OtherEditorOnlyCreationData fixtures. Partial and fresh-process searches remain unmeasured.\",\n"
            );
            json.Append("  \"searchObservations\": [");
            bool firstSearch = true;
            foreach (SizeResult result in results)
            {
                foreach (PerfSearchSample sample in result.SearchObservations)
                {
                    if (!firstSearch)
                    {
                        json.Append(",");
                    }
                    firstSearch = false;
                    json.Append(JsonUtility.ToJson(sample));
                }
            }
            json.Append("],\n");
            json.Append("  \"interactionSamples\": [\n");
            bool firstInteraction = true;
            foreach (SizeResult result in results)
            {
                foreach (PerfInteractionSample sample in result.InteractionSamples)
                {
                    if (!firstInteraction)
                    {
                        json.Append(",\n");
                    }
                    firstInteraction = false;
                    json.Append(JsonUtility.ToJson(sample));
                }
            }
            json.Append("],\n");
            json.Append("  \"scenarios\": [\n");
            AppendScenario(
                json,
                "longest-observed-editor-update-gap",
                results,
                static result => SingleSample(result.LongestObservedEditorUpdateGapMilliseconds, result.FixtureComplete)
            );
            json.Append(",\n");
            AppendScenario(json, "longest-driver-tick", results, static result => SingleSample(result.LongestDriverTickMilliseconds, result.FixtureComplete));
            json.Append(",\n");
            AppendScenario(json, "fixture-create-calls", results, static result => SingleSample(result.CreationMilliseconds, result.FixtureComplete));
            json.Append(",\n");
            AppendScenario(json, "fixture-initialization", results, static result => SingleSample(result.InitializationMilliseconds, result.FixtureComplete));
            json.Append(",\n");
            AppendScenario(json, "fixture-import-batches", results, static result => SingleSample(result.BatchImportMilliseconds, result.FixtureComplete));
            json.Append(",\n");
            AppendScenario(json, "fixture-longest-import-batch", results, static result => SingleSample(result.LongestBatchImportMilliseconds, result.FixtureComplete));
            json.Append(",\n");
            AppendScenario(json, "fixture-staging-total", results, static result => SingleSample(result.StagingMilliseconds, result.FixtureComplete));
            json.Append(",\n");
            AppendScenario(json, "fixture-longest-create-call", results, static result => SingleSample(result.LongestCreateMilliseconds, result.FixtureComplete));
            json.Append(",\n");
            AppendScenario(json, "fixture-longest-staging-tick", results, static result => SingleSample(result.LongestSliceMilliseconds, result.FixtureComplete));
            json.Append(",\n");
            AppendScenario(json, "fixture-import", results, static result => SingleSample(result.ImportMilliseconds, result.FixtureComplete));
            json.Append(",\n");
            AppendScenario(json, "window-open-to-index-ready", results, static result => result.ColdOpenSamples);
            json.Append(",\n");
            AppendScenario(json, "window-open-to-index-ready-warm", results, static result => result.WarmOpenSamples);
            json.Append(",\n");
            AppendScenario(json, "search-first-results", results, static result => result.SearchSamples, static result => result.SearchMatchCounts);
            json.Append(",\n");
            AppendScenario(json, "selection", results, static result => result.SelectionSamples);
            json.Append(",\n");
            AppendScenario(json, "label-filter-observed-ready", results, static result => result.FilterSamples);
            json.Append(",\n");
            AppendScenario(json, "object-scroll-observed-ready", results, static result => result.ScrollSamples);
            json.Append(",\n");
            AppendScenario(json, "type-switch-observed-ready", results, static result => result.TypeSwitchSamples);
            json.Append("\n  ]\n}\n");
            return json.ToString();
        }

        private static List<double> SingleSample(double milliseconds, bool complete = true)
        {
            return complete ? new List<double> { milliseconds } : new List<double>();
        }

        private static string BuildErrorJson(RunState state, string message)
        {
            StringBuilder json = new(2048);
            json.Append("{\n  \"schema\": \"").Append(SchemaVersion).Append("\",\n");
            json.Append("  \"generatedAtUtc\": \"").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append("\",\n");
            json.Append("  \"error\": \"").Append(JsonEscape(message)).Append("\",\n");
            json.Append("  \"phase\": \"").Append(state.Phase).Append("\",\n");
            json.Append("  \"environment\": ").Append(BuildEnvironmentJson()).Append(",\n");
            json.Append("  \"timeoutMinutes\": ").Append(FormatNumber(state.TimeoutMinutes)).Append(",\n");
            json.Append("  \"fixturesCreated\": ").Append(state.Staging?.CreatedCount ?? 0).Append(",\n");
            json.Append("  \"fixturesRequested\": ").Append(state.Current?.Size ?? 0).Append(",\n");
            json.Append("  \"stagingElapsedMs\": ").Append(FormatNumber(state.Staging?.TotalMilliseconds ?? 0)).Append(",\n");
            json.Append("  \"creationCallMs\": ").Append(FormatNumber(state.Staging?.CreationMilliseconds ?? 0)).Append(",\n");
            json.Append("  \"batchImportMs\": ").Append(FormatNumber(state.Staging?.BatchImportMilliseconds ?? 0)).Append(",\n");
            json.Append("  \"fixtureInitializationMs\": ").Append(FormatNumber(state.Staging?.InitializationMilliseconds ?? 0)).Append(",\n");
            json.Append("  \"longestBatchImportMs\": ").Append(FormatNumber(state.Staging?.LongestBatchImportMilliseconds ?? 0)).Append(",\n");
            json.Append("  \"longestCreateCallMs\": ").Append(FormatNumber(state.Staging?.LongestCreateMilliseconds ?? 0)).Append(",\n");
            json.Append("  \"partialResults\": ");
            if (state.Results.Count == 0)
            {
                json.Append("[]");
            }
            else
            {
                json.Append("\n  ");
                json.Append(BuildJson(state.Results, state.TimeoutMinutes, state.StagingBudgetMilliseconds, state.MaximumStagingItems).TrimEnd('\n'));
            }

            json.Append("\n}\n");
            return json.ToString();
        }

        private static string ReadPackageVersion()
        {
            const string marker = "\"version\":";
            try
            {
                string content = File.ReadAllText(PackagePath);
                int markerIndex = content.IndexOf(marker, StringComparison.Ordinal);
                if (markerIndex < 0)
                {
                    return "unknown";
                }

                int valueStart = content.IndexOf('"', markerIndex + marker.Length) + 1;
                int valueEnd = content.IndexOf('"', valueStart);
                return valueEnd > valueStart ? content.Substring(valueStart, valueEnd - valueStart) : "unknown";
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        private static void AppendScenario(
            StringBuilder json,
            string name,
            List<SizeResult> results,
            Func<SizeResult, List<double>> samplesOf,
            Func<SizeResult, List<int>> matchesOf = null
        )
        {
            json.Append("    {\"name\": \"").Append(name).Append("\", \"unit\": \"ms\", \"sizes\": [");
            bool firstSize = true;
            foreach (SizeResult result in results)
            {
                if (!firstSize)
                {
                    json.Append(", ");
                }

                firstSize = false;
                AppendSizeSamples(json, result, samplesOf(result), matchesOf?.Invoke(result));
            }

            json.Append("]}");
        }

        private static void AppendSizeSamples(StringBuilder json, SizeResult result, List<double> samples, List<int> matches)
        {
            json.Append("{\"size\": ").Append(result.Size).Append(", \"fixtureTypeIdentities\": [");
            for (int index = 0; index < result.FixtureTypes.Length; index++)
            {
                if (0 < index)
                {
                    json.Append(",");
                }
                json.Append("\"").Append(JsonEscape(result.FixtureTypes[index].AssemblyQualifiedName)).Append("\"");
            }
            json.Append("], \"samples\": [");
            AppendNumbers(json, samples);
            json.Append("]");
            if (samples.Count > 0)
            {
                json.Append(", \"median\": ").Append(FormatNumber(Median(samples)));
                json.Append(", \"min\": ").Append(FormatNumber(Min(samples)));
                json.Append(", \"max\": ").Append(FormatNumber(Max(samples)));
            }

            if (matches != null)
            {
                json.Append(", \"matchCounts\": [");
                AppendIntegers(json, matches);
                json.Append("]");
            }

            json.Append("}");
        }

        private static void AppendNumbers(StringBuilder json, List<double> samples)
        {
            for (int index = 0; index < samples.Count; index++)
            {
                if (index > 0)
                {
                    json.Append(", ");
                }

                json.Append(FormatNumber(samples[index]));
            }
        }

        private static void AppendIntegers(StringBuilder json, List<int> values)
        {
            for (int index = 0; index < values.Count; index++)
            {
                if (index > 0)
                {
                    json.Append(", ");
                }

                json.Append(values[index]);
            }
        }

        private static double Min(List<double> samples)
        {
            double minimum = double.MaxValue;
            foreach (double sample in samples)
            {
                if (sample < minimum)
                {
                    minimum = sample;
                }
            }

            return minimum;
        }

        private static double Max(List<double> samples)
        {
            double maximum = double.MinValue;
            foreach (double sample in samples)
            {
                if (sample > maximum)
                {
                    maximum = sample;
                }
            }

            return maximum;
        }

        private sealed class SizeResult
        {
            internal int Size;
            internal Type[] FixtureTypes;
            internal bool FixtureComplete;
            internal double CreationMilliseconds;
            internal double BatchImportMilliseconds;
            internal double InitializationMilliseconds;
            internal double LongestBatchImportMilliseconds;
            internal double StagingMilliseconds;
            internal double LongestCreateMilliseconds;
            internal double LongestSliceMilliseconds;
            internal double ImportMilliseconds;
            internal double LongestObservedEditorUpdateGapMilliseconds;
            internal double LongestDriverTickMilliseconds;
            internal readonly List<PerfReadinessSample> ReadinessSamples = new();
            internal double ColdOpenMilliseconds;
            internal readonly List<double> ColdOpenSamples = new();
            internal readonly List<double> WarmOpenSamples = new();
            internal readonly List<PerfSearchSample> SearchObservations = new();
            internal readonly List<double> SearchSamples = new();
            internal readonly List<int> SearchMatchCounts = new();
            internal readonly List<double> SelectionSamples = new();
            internal readonly List<double> FilterSamples = new();
            internal readonly List<double> ScrollSamples = new();
            internal readonly List<double> TypeSwitchSamples = new();
            internal readonly List<PerfInteractionSample> InteractionSamples = new();
        }

        private sealed class RunState
        {
            internal readonly string OutputDirectory;
            internal readonly int[] Sizes;
            internal readonly Type[] FixtureTypes;
            internal readonly double TimeoutMinutes;
            internal readonly double StagingBudgetMilliseconds;
            internal readonly int MaximumStagingItems;
            internal readonly Stopwatch Watchdog = Stopwatch.StartNew();
            internal readonly List<SizeResult> Results = new();
            internal readonly string MissingLabel = "PerfAbsentLabel-" + Guid.NewGuid().ToString("N");
            internal Phase Phase = Phase.Idle;
            internal int SizeIndex = -1;
            internal SizeResult Current;
            internal PerfFixtureStaging Staging;
            internal DataVisualizerWindow Window;
            internal PerfWindowStateScope WindowState;
            internal DocsImageCapture.WindowPreferenceSnapshot Preferences;
            internal UnityEngine.Object[] PreviousSelection;
            internal object PreviousInstance;
            internal readonly Stopwatch PhaseClock = new();
            internal readonly Stopwatch OpenClock = new();
            internal PerfReadinessSample Readiness;
            internal double LastTickMilliseconds;
            internal int WarmRepetition;
            internal int SearchRepetition;
            internal int SelectionRepetition;
            internal int SearchKind;
            internal int QuerySliceOverrunsBefore;
            internal PerfSearchSample PendingSearch;
            internal int FilterRepetition;
            internal int ScrollRepetition;
            internal int TypeSwitchRepetition;
            internal PerfInteractionSample PendingInteraction;
            internal ScriptableObject PendingScrollTarget;
            internal Vector2 ScrollOffsetBefore;
            internal Type PendingType;

            internal RunState(string outputDirectory, int[] sizes, double timeoutMinutes, Type[] fixtureTypes, double stagingBudgetMilliseconds, int maximumStagingItems)
            {
                OutputDirectory = outputDirectory;
                Sizes = sizes;
                FixtureTypes = fixtureTypes;
                TimeoutMinutes = timeoutMinutes;
                StagingBudgetMilliseconds = stagingBudgetMilliseconds;
                MaximumStagingItems = maximumStagingItems;
            }
        }

        private enum Phase
        {
            [Obsolete("Please use a valid value")]
            None = 0,

            // RunState explicitly initializes Idle; phases are never persisted.
            Idle = 8,
            WaitingColdReady = 1,
            InteractionSamples = 2,
            WaitingSearchResults = 3,
            WarmOpen = 4,
            WaitingWarmReady = 5,
            BetweenWarmRepetitions = 6,
            Finished = 7,
            Staging = 9,
            RefreshingFixtures = 10,
            ColdOpen = 11,
            WaitingLabelFilter = 12,
            WaitingObjectScroll = 13,
            WaitingTypeSwitch = 14,
        }
    }
}
