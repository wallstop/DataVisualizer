namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using NUnit.Framework;
    using UnityEngine.TestTools;
    using WallstopStudios.DataVisualizer.Editor.Search;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    /*
        Opt-in synthetic feasibility evidence for #323. No Unity objects or assets
        are created. The production search snapshot and per-window identity tracker
        are measured separately from the unfinished shared metadata index (#309).
        NUnit owns iteration and disposal; every timed unit runs on the editor thread.
    */
    public sealed class MetadataScaleBenchmarkTests
    {
        private const int Warmups = 5;
        private const int Samples = 30;
        private const double BudgetMilliseconds = 4;
        private const long HeapLimitBytes = 2L * 1024 * 1024 * 1024;
        private const string Output = "Packages/com.wallstop-studios.data-visualizer/.artifacts/metadata-scale.json";

        private static string Number(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string Numbers(List<double> values)
        {
            StringBuilder text = new("[");
            for (int index = 0; index < values.Count; index++)
            {
                if (0 < index)
                {
                    text.Append(',');
                }
                text.Append(Number(values[index]));
            }
            return text.Append(']').ToString();
        }

        private static IEnumerator MeasureSize(int size, List<string> scenarios)
        {
            Stopwatch collection = Stopwatch.StartNew();
            long before = GC.GetTotalMemory(true);
            double longestCollection = collection.Elapsed.TotalMilliseconds;
            collection.Restart();
            using Dataset dataset = new(size);
            double allocation = collection.Elapsed.TotalMilliseconds;
            Stopwatch construction = Stopwatch.StartNew();
            Stopwatch slice = Stopwatch.StartNew();
            double longestBuild = 0;
            for (int index = 0; index < size; index++)
            {
                string guid = (index + 1).ToString("x32", CultureInfo.InvariantCulture);
                string name = index == size - 1 ? "needle" : "record " + index.ToString(CultureInfo.InvariantCulture);
                dataset.Documents[index] = new AssetSearchDocument(guid, name, typeof(MetadataScaleBenchmarkTests), null);
                if (BudgetMilliseconds <= slice.Elapsed.TotalMilliseconds)
                {
                    longestBuild = Math.Max(longestBuild, slice.Elapsed.TotalMilliseconds);
                    Assert.That(GC.GetTotalMemory(false) - before, Is.LessThan(HeapLimitBytes), "Stop before a 2 GiB heap increase");
                    yield return null;
                    slice.Restart();
                }
            }
            longestBuild = Math.Max(longestBuild, slice.Elapsed.TotalMilliseconds);
            double buildWall = construction.Elapsed.TotalMilliseconds;
            collection.Restart();
            long documentHeap = GC.GetTotalMemory(true) - before;
            longestCollection = Math.Max(longestCollection, collection.Elapsed.TotalMilliseconds);
            slice.Restart();
            Stopwatch tracking = Stopwatch.StartNew();
            int tracked = 0;
            for (int index = 0; index < size; index++)
            {
                if (dataset.Tracker.Track("Assets/Synthetic/" + index.ToString(CultureInfo.InvariantCulture) + ".asset", dataset.Documents[index].Guid))
                {
                    tracked++;
                }
                if (BudgetMilliseconds <= slice.Elapsed.TotalMilliseconds)
                {
                    longestBuild = Math.Max(longestBuild, slice.Elapsed.TotalMilliseconds);
                    Assert.That(GC.GetTotalMemory(false) - before, Is.LessThan(HeapLimitBytes));
                    yield return null;
                    slice.Restart();
                }
            }
            longestBuild = Math.Max(longestBuild, slice.Elapsed.TotalMilliseconds);
            double trackWall = tracking.Elapsed.TotalMilliseconds;
            Assert.That(tracked, Is.EqualTo(size));
            collection.Restart();
            long combinedHeap = GC.GetTotalMemory(true) - before;
            longestCollection = Math.Max(longestCollection, collection.Elapsed.TotalMilliseconds);
            Assert.That(dataset.Tracker.EnqueueGuid(dataset.Documents[size - 1].Guid));
            Assert.That(dataset.Tracker.PendingCount, Is.EqualTo(1));
            Stopwatch refresh = Stopwatch.StartNew();
            int changed = dataset.Tracker.ProcessSlice();
            double oneChange = refresh.Elapsed.TotalMilliseconds;
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(dataset.Published, Is.EqualTo(1));
            foreach (int count in new[] { size / 10, size })
            {
                IReadOnlyList<AssetSearchDocument> available = new ArraySegment<AssetSearchDocument>(dataset.Documents, 0, count);
                foreach (string term in new[] { "absent-marker", "needle", "record" })
                {
                    List<double> totals = new();
                    List<double> preparations = new();
                    List<double> slices = new();
                    int expectedMatches =
                        term == "record" ? 25
                        : term == "needle" && count == size ? 1
                        : 0;
                    int expectedScanned = term == "record" ? 25 : count;
                    for (int repetition = -Warmups; repetition < Samples; repetition++)
                    {
                        Stopwatch total = Stopwatch.StartNew();
                        AssetSearchQuery query = new(available, new[] { term }, 25);
                        double preparation = total.Elapsed.TotalMilliseconds;
                        while (!query.IsComplete)
                        {
                            query.Advance(BudgetMilliseconds);
                            if (!query.IsComplete)
                            {
                                yield return null;
                            }
                        }
                        double elapsed = total.Elapsed.TotalMilliseconds;
                        Assert.That(query.RecordCount, Is.EqualTo(count));
                        Assert.That(query.ScannedCount, Is.EqualTo(expectedScanned));
                        Assert.That(query.Results.Count, Is.EqualTo(expectedMatches));
                        if (0 <= repetition)
                        {
                            totals.Add(elapsed);
                            preparations.Add(preparation);
                            slices.Add(query.LongestSliceMilliseconds);
                        }
                        yield return null;
                    }
                    Stopwatch cancellation = Stopwatch.StartNew();
                    AssetSearchQuery abandoned = new(available, new[] { "absent-marker" }, 25);
                    double cancelPreparation = cancellation.Elapsed.TotalMilliseconds;
                    abandoned.Advance(BudgetMilliseconds);
                    double firstUnit = cancellation.Elapsed.TotalMilliseconds;
                    int scannedAtCancel = abandoned.ScannedCount;
                    yield return null;
                    Assert.That(abandoned.ScannedCount, Is.EqualTo(scannedAtCancel), "Discarding a query must stop further scanning");
                    scenarios.Add(
                        "{\"records\":"
                            + size
                            + ",\"availableRecords\":"
                            + count
                            + ",\"term\":\""
                            + term
                            + "\",\"documentHeapDeltaBytes\":"
                            + documentHeap
                            + ",\"documentAndTrackerHeapDeltaBytes\":"
                            + combinedHeap
                            + ",\"buildWallMs\":"
                            + Number(buildWall)
                            + ",\"trackWallMs\":"
                            + Number(trackWall)
                            + ",\"initialAllocationMs\":"
                            + Number(allocation)
                            + ",\"longestForcedCollectionMs\":"
                            + Number(longestCollection)
                            + ",\"longestConstructionUnitMs\":"
                            + Number(longestBuild)
                            + ",\"oneTrackedChangeMs\":"
                            + Number(oneChange)
                            + ",\"expectedMatches\":"
                            + expectedMatches
                            + ",\"expectedScanned\":"
                            + expectedScanned
                            + ",\"abandonedScanned\":"
                            + scannedAtCancel
                            + ",\"abandonedPreparationMs\":"
                            + Number(cancelPreparation)
                            + ",\"abandonedPreparationAndFirstSliceMs\":"
                            + Number(firstUnit)
                            + ",\"totalWallMs\":"
                            + Numbers(totals)
                            + ",\"snapshotPreparationMs\":"
                            + Numbers(preparations)
                            + ",\"longestQuerySliceMs\":"
                            + Numbers(slices)
                            + "}"
                    );
                }
            }
            GC.KeepAlive(dataset);
        }

        [UnityTest]
        [Explicit("Synthetic 10k/100k/1m metadata measurement; does not establish real-asset support")]
        [Timeout(600000)]
        public IEnumerator ShouldMeasureSyntheticMetadataScale()
        {
            List<string> scenarios = new();
            string environment = PerfBaseline.BuildEnvironmentJson();
            bool complete = false;
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Output));
                File.WriteAllText(
                    Output,
                    "{\n\"schema\":1,\n\"fixtureKind\":\"synthetic ASCII metadata, no Unity objects or assets\",\n"
                        + "\"productionSurfaces\":[\"AssetSearchDocument\",\"AssetSearchQuery\",\"AssetChangeRefreshService\"],\n"
                        + "\"sharedMetadataIndexMeasured\":false,\n\"realAssets\":0,\n\"diskCacheBytes\":-1,\n"
                        + "\"warmups\":5,\n\"samples\":30,\n\"sliceBudgetMilliseconds\":4,\n"
                        + "\"complete\":"
                        + (complete ? "true" : "false")
                        + ",\n\"environment\":"
                        + environment
                        + ",\n\"scenarios\":[\n"
                        + string.Join(",\n", scenarios)
                        + "\n]}\n"
                );
            });
            foreach (int size in new[] { 10000, 100000, 1000000 })
            {
                yield return MeasureSize(size, scenarios);
            }
            complete = true;
        }

        private sealed class Dataset : IDisposable
        {
            public AssetSearchDocument[] Documents { get; private set; }
            public AssetChangeRefreshService Tracker { get; }
            public int Published { get; private set; }

            public Dataset(int size)
            {
                Documents = new AssetSearchDocument[size];
                Tracker = new AssetChangeRefreshService(_ => true, (_, _, _) => Published++, (path, guid) => (guid, path, typeof(MetadataScaleBenchmarkTests)));
            }

            public void Dispose()
            {
                Tracker.Dispose();
                Documents = null;
            }
        }
    }
}
