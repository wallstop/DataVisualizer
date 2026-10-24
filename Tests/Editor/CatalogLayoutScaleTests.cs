namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Reflection;
    using System.Reflection.Emit;
    using NUnit.Framework;
    using Unity.Profiling;
    using Unity.Profiling.LowLevel.Unsafe;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Styles;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;
    using Object = UnityEngine.Object;

    public sealed class CatalogLayoutScaleTests
    {
        private static ProfilerRecorder CreateFrameAllocationRecorderOrDefault()
        {
            List<ProfilerRecorderHandle> handles = new();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (ProfilerRecorderHandle handle in handles)
            {
                ProfilerRecorderDescription description = ProfilerRecorderHandle.GetDescription(handle);
                if (string.Equals(description.Name, "GC Allocated In Frame", StringComparison.Ordinal))
                {
                    return ProfilerRecorder.StartNew(description.Category, description.Name, 1);
                }
            }
            return default;
        }

        private static Type[][] CreateTypes()
        {
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("CatalogLayoutScale" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
            ModuleBuilder module = assembly.DefineDynamicModule("CatalogLayoutScale");
            Type[][] types = { new Type[2000], new Type[2000] };
            for (int distribution = 0; distribution < types.Length; distribution++)
            {
                for (int i = 0; i < types[distribution].Length; i++)
                {
                    string group = distribution == 0 ? "SingleGroup" : "Group" + (i / 10).ToString("D4");
                    string name = "CatalogLayoutScale." + group + ".MeasuredType" + i.ToString("D4") + "Data";
                    TypeBuilder builder = module.DefineType(name, TypeAttributes.Public, typeof(object));
                    types[distribution][i] = builder.CreateType();
                }
            }
            return types;
        }

        [UnityTest, Explicit("Synthetic type-catalog layout; creates no Unity assets")]
        [Timeout(650000)]
        public IEnumerator ShouldMeasureLargeCatalogConstructionAndScrolling()
        {
            BenchmarkCodeIdentity production = new(
                typeof(NamespaceController).Assembly,
                "Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/NamespaceController.cs"
            );
            BenchmarkCodeIdentity driver = new(typeof(CatalogLayoutScaleTests).Assembly, "Packages/com.wallstop-studios.data-visualizer/Tests/Editor/CatalogLayoutScaleTests.cs");
            production.Verify();
            driver.Verify();
            CatalogReport report = new()
            {
                unityVersion = Application.unityVersion,
                operatingSystem = SystemInfo.operatingSystem,
                processor = SystemInfo.processorType,
                productionSource = production.SourceSha256,
                productionModule = production.LoadedModuleId,
                driverSource = driver.SourceSha256,
                driverModule = driver.LoadedModuleId,
            };
            Stopwatch budget = Stopwatch.StartNew();
            Directory.CreateDirectory("Temp");
            using TestCleanupScope reportCleanup = new();
            reportCleanup.Defer(() =>
            {
                report.elapsedMilliseconds = budget.Elapsed.TotalMilliseconds;
                File.WriteAllText("Temp/catalog-layout-scale.json", JsonUtility.ToJson(report));
            });
            using TestCleanupScope cleanup = new();
            DocsImageCapture.WindowPreferenceSnapshot preferences = DocsImageCapture.WindowPreferenceSnapshot.Capture();
            cleanup.Defer(preferences.Restore);
            int hostsBefore = EditorSurfaceCapture.CountNativeWindowHosts();
            int windowsBefore = Resources.FindObjectsOfTypeAll<EditorWindow>().Length;
            FieldInfo instance = typeof(DataVisualizerWindow).GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = instance.GetValue(null);
            DataVisualizerWindow owner = null;
            PerfWindowStateScope state = null;
            cleanup.Defer(() =>
            {
                if (owner != null)
                {
                    Object.DestroyImmediate(owner);
                }
                state?.Dispose();
                instance.SetValue(null, previous);
            });
            owner = ScriptableObject.CreateInstance<DataVisualizerWindow>();
            const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
            DataVisualizerSettings settings = (DataVisualizerSettings)typeof(DataVisualizerWindow).GetProperty("Settings", members).GetValue(owner);
            DataVisualizerUserState userState = (DataVisualizerUserState)typeof(DataVisualizerWindow).GetProperty("UserState", members).GetValue(owner);
            state = new PerfWindowStateScope(settings, userState, "Temp/CatalogLayoutScale");
            typeof(DataVisualizerWindow).GetField("_settings", members).SetValue(owner, state.Settings);
            typeof(DataVisualizerWindow).GetField("_userState", members).SetValue(owner, state.UserState);
            typeof(DataVisualizerWindow).GetField("_userStateFilePath", members).SetValue(owner, state.FilePath);
            state.UserState.namespaceCollapseStates.Clear();
            LayoutTestWindow host = ScriptableObject.CreateInstance<LayoutTestWindow>();
            cleanup.Defer(() => EditorSurfaceCapture.CloseWindow(host));
            host.position = new Rect(40, 40, 360, 720);
            VisualElement root = host.rootVisualElement;
            root.AddToClassList("dataviz-root");
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/Styles/DataVisualizerStyles.uss"));
            DataVisualizerThemeSelection theme = new();
            cleanup.Defer(() => theme.Apply(null, null));
            theme.Apply(root, DataVisualizerThemeSelection.ResolveDefault());
            ScrollView scroll = new(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            root.Add(scroll);
            host.ShowUtility();
            using ProfilerRecorder frameAllocations = CreateFrameAllocationRecorderOrDefault();
            Type[][] types = CreateTypes();
            long initialHeap = GC.GetTotalMemory(true);
            long allocationControlStart = GC.GetAllocatedBytesForCurrentThread();
            byte[] allocationControl = new byte[4096];
            report.allocationControlObservedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationControlStart;
            report.perThreadAllocationCounterAvailable = 4096 <= report.allocationControlObservedBytes;
            yield return null;
            GC.KeepAlive(allocationControl);
            report.frameAllocationRecorderAvailable = frameAllocations.Valid;

            foreach (int count in new[] { 100, 1000, 2000 })
            {
                for (int distribution = 0; distribution < types.Length; distribution++)
                {
                    Dictionary<string, List<Type>> managed = new(StringComparer.Ordinal);
                    Dictionary<string, int> ranks = new(StringComparer.Ordinal);
                    for (int i = 0; i < count; i++)
                    {
                        Type type = types[distribution][i];
                        string key = NamespaceController.GetNamespaceKey(type);
                        if (!managed.TryGetValue(key, out List<Type> group))
                        {
                            group = new List<Type>();
                            managed.Add(key, group);
                            ranks.Add(key, -ranks.Count);
                        }
                        group.Add(type);
                    }
                    for (int repetition = -5; repetition < 30; repetition++)
                    {
                        Assert.That(budget.Elapsed.TotalMinutes, Is.LessThan(10), $"Ten-minute catalog budget: {count}/{distribution}/{repetition}");
                        scroll.Clear();
                        scroll.scrollOffset = Vector2.zero;
                        VisualElement catalogRoot = null;
                        long allocated = GC.GetAllocatedBytesForCurrentThread();
                        long constructionStarted = Stopwatch.GetTimestamp();
                        NamespaceController controller = new(managed, ranks);
                        controller.Build(owner, ref catalogRoot);
                        double constructionMilliseconds = (Stopwatch.GetTimestamp() - constructionStarted) * 1000.0 / Stopwatch.Frequency;
                        allocated = report.perThreadAllocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() - allocated : -1;
                        scroll.Add(catalogRoot);
                        yield return null;
                        long editorFrameAllocatedBytes = frameAllocations.Valid && 0 < frameAllocations.Count ? frameAllocations.LastValue : -1;
                        EditorSurfaceCapture.SettleLayout(host);
                        int elements = catalogRoot.Query<VisualElement>().ToList().Count;
                        List<VisualElement> rows = catalogRoot.Query<VisualElement>(className: StyleConstants.TypeItemClass).ToList();
                        Assert.That(rows.Count, Is.EqualTo(count));
                        Stopwatch scrolling = Stopwatch.StartNew();
                        scroll.scrollOffset = new Vector2(0, scroll.verticalScroller.highValue);
                        yield return null;
                        EditorSurfaceCapture.SettleLayout(host);
                        double scrollMilliseconds = scrolling.Elapsed.TotalMilliseconds;
                        Assert.That(scroll.scrollOffset.y, Is.Positive, "Every scroll observation must move the viewport");
                        int visible = 0;
                        foreach (VisualElement row in rows)
                        {
                            if (row.worldBound.Overlaps(scroll.contentViewport.worldBound))
                            {
                                visible++;
                            }
                        }
                        Assert.That(visible, Is.Positive);
                        if (0 <= repetition)
                        {
                            report.samples.Add(
                                new CatalogSample
                                {
                                    types = count,
                                    namespaces = managed.Count,
                                    distribution = distribution == 0 ? "one-large-namespace" : "ten-types-per-namespace",
                                    repetition = repetition,
                                    constructionMilliseconds = constructionMilliseconds,
                                    constructionAllocatedBytes = allocated,
                                    editorFrameAllocatedBytesAfterConstruction = editorFrameAllocatedBytes,
                                    observedScrollMilliseconds = scrollMilliseconds,
                                    scrollOffset = scroll.scrollOffset.y,
                                    realizedElements = elements,
                                    realizedTypeRows = rows.Count,
                                    viewportTypeRows = visible,
                                    managedBytes = GC.GetTotalMemory(false),
                                    unityAllocatedBytes = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(),
                                }
                            );
                        }
                    }
                    scroll.Clear();
                    Assert.That(
                        GC.GetTotalMemory(true) - initialHeap,
                        Is.LessThan(512L * 1024 * 1024),
                        "Stop after a condition if additional retained managed heap reaches 512 MiB"
                    );
                }
            }
            cleanup.Dispose();
            Assert.That(EditorSurfaceCapture.CountNativeWindowHosts(), Is.EqualTo(hostsBefore));
            Assert.That(Resources.FindObjectsOfTypeAll<EditorWindow>().Length, Is.EqualTo(windowsBefore));
            production.Verify();
            driver.Verify();
            report.nativeCleanupVerified = true;
            report.complete = true;
            report.failure = string.Empty;
        }

        [Serializable]
        private sealed class CatalogReport
        {
            public bool complete;
            public bool syntheticTypes = true;
            public string fixtureKind = "CLR classes; not Unity asset types";
            public string failure = "Experiment did not complete; see NUnit results.";
            public int generatedTypes = 4000;
            public int warmupsPerCondition = 5;
            public int samplesPerCondition = 30;
            public int timeoutMinutes = 10;
            public bool nativeCleanupVerified;
            public bool perThreadAllocationCounterAvailable;
            public long allocationControlObservedBytes;
            public bool frameAllocationRecorderAvailable;
            public string unityVersion;
            public string operatingSystem;
            public string processor;
            public string productionSource;
            public string productionModule;
            public string driverSource;
            public string driverModule;
            public double elapsedMilliseconds;
            public List<CatalogSample> samples = new();
        }

        [Serializable]
        private sealed class CatalogSample
        {
            public int types;
            public int namespaces;
            public string distribution;
            public int repetition;
            public double constructionMilliseconds;
            public long constructionAllocatedBytes;
            public long editorFrameAllocatedBytesAfterConstruction;
            public double observedScrollMilliseconds;
            public float scrollOffset;
            public int realizedElements;
            public int realizedTypeRows;
            public int viewportTypeRows;
            public long managedBytes;
            public long unityAllocatedBytes;
        }
    }
}
