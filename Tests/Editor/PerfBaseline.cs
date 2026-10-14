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
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;
    using Debug = UnityEngine.Debug;
    using Object = UnityEngine.Object;
    using PlayModeDataObject = WallstopStudios.DataVisualizer.Tests.Runtime.PlayModeDataObject;

    /*
        Standing performance-baseline driver for the T01 harness slice of #114. It
        stages deterministic PlayModeDataObject fixture datasets (100 / 1,000 /
        10,000 assets), then measures, with monotonic Stopwatch timers inside the
        editor so MCP transport never pollutes a sample:

        - fixture-import wall (the single AssetDatabase.Refresh that imports the
          staged files);
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
        the run closed — with fixtures and the update hook removed — when a
        phase never completes, so a hung index can never hang the host editor.

        Results land in one JSON report (schema 1) plus a compact console
        summary. The report records the host environment (Unity, OS, CPU,
        memory, DPI, displays, theme, package version, pre-existing managed
        assets) so a number is meaningless without its context, and it records
        the repetition counts so the first-tranche limits are explicit: cold is
        one sample, warm is two discarded warm-ups plus five samples, and the
        full T01 acceptance (30+ repetitions, cross-host spread, Play-entry and
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

        private const string FixtureFolder = "Assets/PerfBaselineFixtures";
        private const string OutputArgument = "-perfBaselineOutputDir";
        private const string OutputEnvironmentVariable = "DATAVISUALIZER_PERF_BASELINE_DIR";
        private const string DefaultOutputDirectory = "Temp/PerfBaseline";
        private const string PackagePath = "Packages/com.wallstop-studios.data-visualizer/package.json";

        private const int WarmupRepetitions = 2;
        private const int WarmRepetitions = 5;
        private const int SearchRepetitions = 5;
        private const int SelectionRepetitions = 5;
        private const int SearchCacheTimeoutSeconds = 600;
        private const int OverallTimeoutMinutes = 45;
        private const string SearchQueryFirst = "Player";
        private const string SearchQuerySecond = "World";

        private const string InstanceFieldName = "Instance";
        private const string SearchPopoverFieldName = "_searchPopover";
        private const string SearchTriggerFieldName = "_searchField";
        private const string SearchFieldElementName = "global-search-field";
        private const string SearchCacheFieldName = "_allManagedObjectsCache";
        private const string SearchCachePopulatedFieldName = "_isSearchCachePopulated";
        private const string SearchResultsFieldName = "_currentSearchResultItems";

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
        internal static void RunBaseline(string outputDirectory)
        {
            if (_active != null)
            {
                throw new InvalidOperationException("A performance-baseline run is already active in this editor.");
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException("The output directory must not be empty.", nameof(outputDirectory));
            }

            RunState state = new(outputDirectory);
            _active = state;
            EditorApplication.update += Tick;
            Debug.Log($"[PerfBaseline] run started; report will be written to {outputDirectory}.");
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

        internal static string FixtureName(int index)
        {
            string title = FixtureTitles[index % FixtureTitles.Length];
            int cycle = index / FixtureTitles.Length;
            return cycle == 0 ? title : $"{title} {cycle:D6}";
        }

        private static void Tick()
        {
            RunState state = _active;
            if (state == null)
            {
                EditorApplication.update -= Tick;
                return;
            }

            if (state.Watchdog.Elapsed > TimeSpan.FromMinutes(OverallTimeoutMinutes))
            {
                FinishWithError(state, $"the run exceeded the {OverallTimeoutMinutes}-minute watchdog in phase {state.Phase}.");
                return;
            }

            try
            {
                Advance(state);
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
                case Phase.WaitingColdReady:
                case Phase.WaitingWarmReady:
                    WaitSearchCacheReady(state);
                    return;
                case Phase.InteractionSamples:
                    RunInteractionSample(state);
                    return;
                case Phase.WaitingSearchResults:
                    WaitSearchResults(state);
                    return;
                case Phase.BetweenWarmRepetitions:
                    AdvanceWarmCycle(state);
                    return;
                case Phase.WarmOpen:
                    OpenWindow(state, cold: false);
                    return;
                case Phase.Finished:
                    return; // Finished has nothing to advance
            }
        }

        private static void BeginNextSize(RunState state)
        {
            state.SizeIndex++;
            if (state.SizeIndex >= FixtureSizes.Length)
            {
                Finish(state);
                return;
            }

            int size = FixtureSizes[state.SizeIndex];
            SizeResult result = new() { Size = size };
            state.Current = result;
            state.Results.Add(result);

            DeleteFixtureFolder();
            StageFixtures(state, size);
            OpenWindow(state, cold: true);
        }

        private static void StageFixtures(RunState state, int size)
        {
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(FixtureFolder));
            for (int index = 0; index < size; index++)
            {
                PlayModeDataObject asset = ScriptableObject.CreateInstance<PlayModeDataObject>();
                asset.name = FixtureName(index);
                AssetDatabase.CreateAsset(asset, $"{FixtureFolder}/{FixtureName(index)}.asset");
            }

            state.PhaseClock.Restart();
            AssetDatabase.Refresh();
            state.PhaseClock.Stop();
            state.Current.ImportMilliseconds = state.PhaseClock.Elapsed.TotalMilliseconds;
            state.Phase = Phase.WaitingColdReady;
        }

        private static void OpenWindow(RunState state, bool cold)
        {
            FieldInfo instanceField = typeof(DataVisualizerWindow).GetField(InstanceFieldName, StaticMembers);
            if (instanceField == null)
            {
                throw new InvalidOperationException("The DataVisualizer window exposes no Instance field; the baseline driver cannot preserve the host's window instance.");
            }

            state.PreviousInstance = instanceField.GetValue(null);
            DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            state.Window = window;
            window.ShowPopup();
            window.position = new Rect(40f, 40f, 1180f, 780f);
            state.PhaseClock.Restart();
            window.CreateGUI();
            InvokeInitialContentLoad(window);
            GetNamespaceController(window).SelectType(window, typeof(PlayModeDataObject));
            state.Phase = cold ? Phase.WaitingColdReady : Phase.WaitingWarmReady;
        }

        private static void WaitSearchCacheReady(RunState state)
        {
            if (state.PhaseClock.Elapsed > TimeSpan.FromSeconds(SearchCacheTimeoutSeconds))
            {
                throw new InvalidOperationException($"the search cache did not become ready within {SearchCacheTimeoutSeconds}s at size {state.Current.Size}.");
            }

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

            state.PhaseClock.Stop();
            double readyMilliseconds = state.PhaseClock.Elapsed.TotalMilliseconds;
            if (state.Phase == Phase.WaitingColdReady)
            {
                state.Current.ColdOpenMilliseconds = readyMilliseconds;
                state.SearchRepetition = 0;
                state.SelectionRepetition = 0;
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

        private static void RunInteractionSample(RunState state)
        {
            if (state.SearchRepetition < SearchRepetitions)
            {
                MeasureSearch(state, state.SearchRepetition++);
                return;
            }

            if (state.SelectionRepetition < SelectionRepetitions)
            {
                MeasureSelection(state, state.SelectionRepetition++);
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
            string query = repetition % 2 == 0 ? SearchQueryFirst : SearchQuerySecond;
            TextField searchField = FindSearchField(state.Window);
            state.PendingQuery = query;
            /*
                The production search keeps a no-op guard on the last query string, so a
                sample that repeats the previous query would measure nothing. Clearing
                the field first guarantees every measured query really runs; the guard's
                own empty-search pass happens outside the timed region.
            */
            searchField.value = string.Empty;
            /*
                The production search may build its results synchronously (small
                caches) or through a debounce (larger ones — the size-1,000 run
                measured a 0.6 ms callback that had not produced a row yet). The
                clock keeps running across ticks until the results list carries a
                row, so the sample is the until-first-results latency the T12
                acceptance target describes, debounce included.
            */
            state.PhaseClock.Restart();
            searchField.value = query;
            state.Phase = Phase.WaitingSearchResults;
        }

        private static void WaitSearchResults(RunState state)
        {
            if (state.PhaseClock.Elapsed > TimeSpan.FromSeconds(5))
            {
                throw new InvalidOperationException($"the search '{state.PendingQuery}' produced no results within 5s at size {state.Current.Size}.");
            }

            if (typeof(DataVisualizerWindow).GetField(SearchResultsFieldName, Members)?.GetValue(state.Window) is not List<VisualElement> results || results.Count == 0)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                return; // the next editor tick re-checks the results list
            }

            state.PhaseClock.Stop();
            state.Current.SearchSamples.Add(state.PhaseClock.Elapsed.TotalMilliseconds);
            state.Current.SearchMatchCounts.Add(results.Count);
            state.Phase = Phase.InteractionSamples;
        }

        private static void MeasureSelection(RunState state, int repetition)
        {
            int assetIndex = (repetition * 997 + state.Current.Size / 2) % state.Current.Size;
            string assetPath = $"{FixtureFolder}/{FixtureName(assetIndex)}.asset";
            PlayModeDataObject asset = AssetDatabase.LoadAssetAtPath<PlayModeDataObject>(assetPath);
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
            state.Current.SelectionSamples.Add(state.PhaseClock.Elapsed.TotalMilliseconds);
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

        private static void CloseWindow(RunState state)
        {
            DataVisualizerWindow window = state.Window;
            state.Window = null;
            if (window == null)
            {
                return;
            }

            InvokeCleanup(window);
            window.rootVisualElement.Clear();
            Object.DestroyImmediate(window);
            AssetGuidTypeIndex.Shared.Cancel();
            typeof(DataVisualizerWindow).GetField(InstanceFieldName, StaticMembers)?.SetValue(null, state.PreviousInstance);
            state.PreviousInstance = null;
        }

        private static void Finish(RunState state)
        {
            string json = BuildJson(state.Results);
            WriteReport(state.OutputDirectory, json);
            string summary = BuildSummary(state.Results);
            TearDown(state);
            Debug.Log($"[PerfBaseline] complete. {summary}");
        }

        private static void FinishWithError(RunState state, string message)
        {
            Debug.LogError($"[PerfBaseline] FAILED: {message}");
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
                DeleteFixtureFolder();
            }
            catch (Exception error)
            {
                Debug.LogError($"[PerfBaseline] fixture cleanup failed: {error.Message}");
            }

            _active = null;
            EditorApplication.update -= Tick;
        }

        private static void DeleteFixtureFolder()
        {
            if (AssetDatabase.IsValidFolder(FixtureFolder))
            {
                AssetDatabase.DeleteAsset(FixtureFolder);
                AssetDatabase.Refresh();
            }
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

        private static string BuildJson(List<SizeResult> results)
        {
            StringBuilder json = new(4096);
            json.Append("{\n  \"schema\": \"").Append(SchemaVersion).Append("\",\n");
            json.Append("  \"generatedAtUtc\": \"").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append("\",\n");
            json.Append("  \"environment\": ").Append(BuildEnvironmentJson()).Append(",\n");
            json.Append("  \"repetitions\": {\"cold\": 1, \"warmWarmups\": ")
                .Append(WarmupRepetitions)
                .Append(", \"warmSamples\": ")
                .Append(WarmRepetitions)
                .Append(", \"searchSamples\": ")
                .Append(SearchRepetitions)
                .Append(", \"selectionSamples\": ")
                .Append(SelectionRepetitions)
                .Append("},\n");
            json.Append(
                "  \"gaps\": [\"Play-entry and compile timings are not yet measured\", \"cold is a single sample per size\", \"cross-host spread is not yet collected\"],\n"
            );
            json.Append("  \"scenarios\": [\n");
            AppendScenario(json, "fixture-import", results, static result => SingleSample(result.ImportMilliseconds));
            json.Append(",\n");
            AppendScenario(json, "window-open-to-index-ready", results, static result => SingleSample(result.ColdOpenMilliseconds));
            json.Append(",\n");
            AppendScenario(json, "window-open-to-index-ready-warm", results, static result => result.WarmOpenSamples);
            json.Append(",\n");
            AppendScenario(json, "search-first-results", results, static result => result.SearchSamples, static result => result.SearchMatchCounts);
            json.Append(",\n");
            AppendScenario(json, "selection", results, static result => result.SelectionSamples);
            json.Append("\n  ]\n}\n");
            return json.ToString();
        }

        private static List<double> SingleSample(double milliseconds)
        {
            return new List<double> { milliseconds };
        }

        private static string BuildErrorJson(RunState state, string message)
        {
            StringBuilder json = new(2048);
            json.Append("{\n  \"schema\": \"").Append(SchemaVersion).Append("\",\n");
            json.Append("  \"generatedAtUtc\": \"").Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append("\",\n");
            json.Append("  \"error\": \"").Append(JsonEscape(message)).Append("\",\n");
            json.Append("  \"phase\": \"").Append(state.Phase).Append("\",\n");
            json.Append("  \"environment\": ").Append(BuildEnvironmentJson()).Append(",\n");
            json.Append("  \"partialResults\": ");
            if (state.Results.Count == 0)
            {
                json.Append("[]");
            }
            else
            {
                json.Append("\n  ");
                json.Append(BuildJson(state.Results).TrimEnd('\n'));
            }

            json.Append("\n}\n");
            return json.ToString();
        }

        private static string BuildEnvironmentJson()
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

        private static int CountScriptableObjectAssets()
        {
            try
            {
                return AssetDatabase.FindAssets("t:ScriptableObject").Length;
            }
            catch (Exception)
            {
                return -1;
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
            json.Append("{\"size\": ").Append(result.Size).Append(", \"samples\": [");
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
            internal double ImportMilliseconds;
            internal double ColdOpenMilliseconds;
            internal readonly List<double> WarmOpenSamples = new();
            internal readonly List<double> SearchSamples = new();
            internal readonly List<int> SearchMatchCounts = new();
            internal readonly List<double> SelectionSamples = new();
        }

        private sealed class RunState
        {
            internal readonly string OutputDirectory;
            internal readonly Stopwatch Watchdog = Stopwatch.StartNew();
            internal readonly List<SizeResult> Results = new();
            internal Phase Phase = Phase.Idle;
            internal int SizeIndex = -1;
            internal SizeResult Current;
            internal DataVisualizerWindow Window;
            internal object PreviousInstance;
            internal readonly Stopwatch PhaseClock = new();
            internal int WarmRepetition;
            internal int SearchRepetition;
            internal int SelectionRepetition;
            internal string PendingQuery;

            internal RunState(string outputDirectory)
            {
                OutputDirectory = outputDirectory;
            }
        }

        private enum Phase
        {
            Idle,
            WaitingColdReady,
            InteractionSamples,
            WaitingSearchResults,
            WarmOpen,
            WaitingWarmReady,
            BetweenWarmRepetitions,
            Finished,
        }
    }
}
