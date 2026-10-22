namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Security.Cryptography;
    using System.Text;
    using NUnit.Framework;
    using UnityEngine.TestTools;
    using WallstopStudios.DataVisualizer.Editor.Search;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    /*
        Opt-in synthetic feasibility evidence for #323. No Unity objects or assets
        are created. The production shared metadata store, window document view and identity tracker
        are measured without AssetDatabase classification or real-asset support claims.
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

        private static string SourceHash()
        {
            using SHA256 hash = SHA256.Create();
            return BitConverter
                .ToString(hash.ComputeHash(File.ReadAllBytes("Packages/com.wallstop-studios.data-visualizer/Tests/Editor/MetadataScaleBenchmarkTests.cs")))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void MeasureConcurrentQueryHeap(AssetObjectCache cache, out long copyingHeap, out long sharedHeap)
        {
            string[] terms = { "absent-marker" };
            AssetSearchQuery[] copying = new AssetSearchQuery[32];
            AssetSearchQuery[] shared = new AssetSearchQuery[32];
            using TestCleanupScope cleanup = new();
            cleanup.Defer(() =>
            {
                Array.Clear(copying, 0, copying.Length);
                Array.Clear(shared, 0, shared.Length);
            });
            long before = GC.GetTotalMemory(true);
            for (int index = 0; index < copying.Length; index++)
            {
                copying[index] = new AssetSearchQuery(cache.Documents, terms, 25, sortByIdentity: !cache.IsSorted);
            }
            long afterCopying = GC.GetTotalMemory(true);
            for (int index = 0; index < shared.Length; index++)
            {
                shared[index] = cache.CreateQuery(terms, 25);
            }
            long afterShared = GC.GetTotalMemory(true);
            GC.KeepAlive(copying);
            GC.KeepAlive(shared);
            copyingHeap = afterCopying - before;
            sharedHeap = afterShared - afterCopying;
        }

        private static IEnumerator MeasureSize(int size, List<string> scenarios, List<string> mutations, Action<string, int> reportProgress, Action checkpoint)
        {
            reportProgress("collecting baseline", size);
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
                AssetSearchDocument document = new(
                    guid,
                    name + " Café 世界",
                    typeof(MetadataScaleBenchmarkTests),
                    new MetadataScaleText
                    {
                        Text = "Description résumé 世界",
                        Child = new MetadataScaleText { Text = "Nested field text" },
                    }
                );
                AssetMetadataRecord record = new(
                    document,
                    typeof(MetadataScaleBenchmarkTests),
                    "Assets/Synthetic/" + guid + ".asset",
                    "Display " + name,
                    new[] { "Shared", "Label " + index % 17 }
                );
                dataset.Index.Metadata.Upsert(record);
                dataset.Documents[index] = record.Document;
                if (BudgetMilliseconds <= slice.Elapsed.TotalMilliseconds)
                {
                    longestBuild = Math.Max(longestBuild, slice.Elapsed.TotalMilliseconds);
                    Assert.That(GC.GetTotalMemory(false) - before, Is.LessThan(HeapLimitBytes), "Stop before a 2 GiB heap increase");
                    reportProgress("building documents", index + 1);
                    yield return null;
                    slice.Restart();
                }
            }
            longestBuild = Math.Max(longestBuild, slice.Elapsed.TotalMilliseconds);
            double buildWall = construction.Elapsed.TotalMilliseconds;
            collection.Restart();
            long documentHeap = GC.GetTotalMemory(true) - before;
            longestCollection = Math.Max(longestCollection, collection.Elapsed.TotalMilliseconds);
            Stopwatch viewClock = Stopwatch.StartNew();
            slice.Restart();
            HashSet<Type> managedTypes = new() { typeof(MetadataScaleBenchmarkTests) };
            foreach (AssetSearchDocument document in dataset.Documents)
            {
                dataset.View.UseMetadata(document.Guid, managedTypes);
                if (dataset.PartialView.Count < size / 10)
                {
                    dataset.PartialView.UseMetadata(document.Guid, managedTypes);
                }
                if (BudgetMilliseconds <= slice.Elapsed.TotalMilliseconds)
                {
                    longestBuild = Math.Max(longestBuild, slice.Elapsed.TotalMilliseconds);
                    Assert.That(GC.GetTotalMemory(false) - before, Is.LessThan(HeapLimitBytes));
                    reportProgress("building metadata view", dataset.View.Count);
                    yield return null;
                    slice.Restart();
                }
            }
            longestBuild = Math.Max(longestBuild, slice.Elapsed.TotalMilliseconds);
            double viewWall = viewClock.Elapsed.TotalMilliseconds;
            reportProgress("ordering metadata view", size);
            Stopwatch finalSort = Stopwatch.StartNew();
            double finalSortActiveMilliseconds = 0;
            double longestFinalSortSliceMilliseconds = 0;
            int finalSortSlices = 0;
            int finalSortOverruns = 0;
            bool sorted;
            do
            {
                Stopwatch sortSlice = Stopwatch.StartNew();
                sorted = dataset.View.AdvanceSort(BudgetMilliseconds);
                double elapsed = sortSlice.Elapsed.TotalMilliseconds;
                finalSortActiveMilliseconds += elapsed;
                longestFinalSortSliceMilliseconds = Math.Max(longestFinalSortSliceMilliseconds, elapsed);
                finalSortSlices++;
                if (BudgetMilliseconds < elapsed)
                {
                    finalSortOverruns++;
                }
                if (!sorted)
                {
                    yield return null;
                }
            } while (!sorted);
            double finalSortMilliseconds = finalSort.Elapsed.TotalMilliseconds;
            longestBuild = Math.Max(longestBuild, longestFinalSortSliceMilliseconds);
            Assert.That(dataset.View.Count, Is.EqualTo(size));
            Assert.That(dataset.View.RetainedObjectCount, Is.Zero);
            Assert.That(dataset.View.ObjectLoadCount, Is.Zero);
            collection.Restart();
            long viewHeap = GC.GetTotalMemory(true) - before;
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
                    reportProgress("tracking identities", index + 1);
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
            Stopwatch invalidation = Stopwatch.StartNew();
            int removed = 0;
            int restored = 0;
            for (int index = 0; index < Math.Min(size, 1000); index++)
            {
                string guid = dataset.Documents[index].Guid;
                Assert.That(dataset.Index.Metadata.TryGet(guid, out AssetMetadataRecord record));
                if (dataset.Index.Metadata.RemovePath(record.Path))
                {
                    removed++;
                }
                if (dataset.Index.Metadata.Upsert(record))
                {
                    restored++;
                }
            }
            double invalidateRestore = invalidation.Elapsed.TotalMilliseconds;
            Assert.That(removed, Is.EqualTo(1000));
            Assert.That(restored, Is.EqualTo(1000));
            Assert.That(dataset.Index.Metadata.Count, Is.EqualTo(size));
            foreach (int count in new[] { size / 10, size })
            {
                AssetObjectCache queryView = count == size ? dataset.View : dataset.PartialView;
                Assert.That(queryView.Count, Is.EqualTo(count));
                reportProgress("measuring concurrent snapshot ownership", count);
                MeasureConcurrentQueryHeap(queryView, out long copyingQueryHeap, out long sharedQueryHeap);
                foreach (string term in new[] { "absent-marker", "needle", "record" })
                {
                    List<double> totals = new();
                    List<double> preparations = new();
                    List<double> activeWork = new();
                    List<double> slices = new();
                    int expectedMatches =
                        term == "record" ? 25
                        : term == "needle" && count == size ? 1
                        : 0;
                    int expectedScanned = term == "record" ? (count == size ? 26 : 25) : count;
                    for (int repetition = -Warmups; repetition < Samples; repetition++)
                    {
                        reportProgress("query " + size + "/" + count + "/" + term, repetition + Warmups);
                        Stopwatch total = Stopwatch.StartNew();
                        AssetSearchQuery query = queryView.CreateQuery(new[] { term }, 25);
                        double preparation = total.Elapsed.TotalMilliseconds;
                        double active = preparation;
                        Stopwatch unit = new();
                        while (!query.IsComplete)
                        {
                            unit.Restart();
                            query.Advance(BudgetMilliseconds);
                            active += unit.Elapsed.TotalMilliseconds;
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
                            activeWork.Add(active);
                            slices.Add(query.LongestSliceMilliseconds);
                        }
                        yield return null;
                    }
                    Stopwatch cancellation = Stopwatch.StartNew();
                    AssetSearchQuery abandoned = queryView.CreateQuery(new[] { "absent-marker" }, 25);
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
                            + ",\"snapshotOwnership\":\""
                            + (count == size ? "shared sorted cache" : "shared unsorted cache")
                            + "\",\"concurrentQueries\":32,\"copyingQueryHeapDeltaBytes\":"
                            + copyingQueryHeap
                            + ",\"sharedQueryHeapDeltaBytes\":"
                            + sharedQueryHeap
                            + ",\"term\":\""
                            + term
                            + "\",\"sharedStoreHeapDeltaBytes\":"
                            + documentHeap
                            + ",\"sharedStoreAndViewHeapDeltaBytes\":"
                            + viewHeap
                            + ",\"viewBuildWallMs\":"
                            + Number(viewWall)
                            + ",\"finalViewSortMs\":"
                            + Number(finalSortMilliseconds)
                            + ",\"finalViewSortActiveMs\":"
                            + Number(finalSortActiveMilliseconds)
                            + ",\"longestFinalViewSortSliceMs\":"
                            + Number(longestFinalSortSliceMilliseconds)
                            + ",\"finalViewSortSlices\":"
                            + finalSortSlices
                            + ",\"finalViewSortOverruns\":"
                            + finalSortOverruns
                            + ",\"invalidateRestore1000Ms\":"
                            + Number(invalidateRestore)
                            + ",\"retainedAssetReferences\":0,\"objectLoads\":0"
                            + ",\"sharedStoreViewAndTrackerHeapDeltaBytes\":"
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
                            + ",\"activeQueryMs\":"
                            + Numbers(activeWork)
                            + ",\"snapshotPreparationMs\":"
                            + Numbers(preparations)
                            + ",\"longestQuerySliceMs\":"
                            + Numbers(slices)
                            + "}"
                    );
                    checkpoint();
                }
            }
            reportProgress("measuring edits after shared queries", size);
            AssetSearchQuery retainedQuery = dataset.View.CreateQuery(new[] { "record" }, 25);
            long beforeMutation = GC.GetTotalMemory(true);
            double[] mutationTimes = new double[2];
            for (int index = 0; index < mutationTimes.Length; index++)
            {
                AssetSearchDocument original = dataset.Documents[index];
                AssetSearchDocument replacement = new(original.Guid, original.Name, original.ExactType, null);
                dataset.Index.Metadata.Upsert(new AssetMetadataRecord(replacement, replacement.ExactType, "Assets/Synthetic/" + original.Guid + ".asset", original.Name, null));
                Stopwatch mutation = Stopwatch.StartNew();
                Assert.That(dataset.View.UseMetadata(original.Guid, managedTypes));
                mutationTimes[index] = mutation.Elapsed.TotalMilliseconds;
            }
            long mutationHeap = GC.GetTotalMemory(true) - beforeMutation;
            mutations.Add(
                "{\"records\":"
                    + size
                    + ",\"firstChangeMs\":"
                    + Number(mutationTimes[0])
                    + ",\"secondChangeMs\":"
                    + Number(mutationTimes[1])
                    + ",\"retainedHeapDeltaBytes\":"
                    + mutationHeap
                    + "}"
            );
            retainedQuery.Advance();
            Assert.That(retainedQuery.RecordCount, Is.EqualTo(size));
            Assert.That(dataset.View.RetainedObjectCount, Is.Zero);
            GC.KeepAlive(retainedQuery);
            GC.KeepAlive(dataset);
        }

        [UnityTest]
        [Explicit("Synthetic 10k/100k/1m metadata measurement; does not establish real-asset support")]
        [Timeout(1800000)]
        public IEnumerator ShouldMeasureSyntheticMetadataScale()
        {
            List<string> scenarios = new();
            List<string> mutations = new();
            string environment = "{}";
            string sourceHash = SourceHash();
            string startedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            Stopwatch runClock = Stopwatch.StartNew();
            bool complete = false;
            string phase = "starting";
            int progress = 0;
            using TestCleanupScope cleanup = new();
            Action writeReport = () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Output));
                File.WriteAllText(
                    Output,
                    "{\n\"schema\":5,\n\"fixtureKind\":\"synthetic Unicode metadata with labels and nested field text, no Unity objects or assets\",\n"
                        + "\"startedUtc\":\""
                        + startedUtc
                        + "\",\"sourceSha256\":\""
                        + sourceHash
                        + "\",\"elapsedMilliseconds\":"
                        + Number(runClock.Elapsed.TotalMilliseconds)
                        + ",\n"
                        + "\"productionSurfaces\":[\"AssetMetadataStore\",\"AssetMetadataRecord\",\"AssetObjectCache\",\"AssetSearchDocument\",\"AssetSearchQuery\",\"AssetChangeRefreshService\"],\n"
                        + "\"sharedMetadataStoreMeasured\":true,\"assetDatabaseClassificationMeasured\":false,\n\"realAssets\":0,\n\"diskCacheBytes\":-1,\n"
                        + "\"warmups\":"
                        + Warmups
                        + ",\n\"samples\":"
                        + Samples
                        + ",\n\"sliceBudgetMilliseconds\":"
                        + Number(BudgetMilliseconds)
                        + ",\n"
                        + "\"complete\":"
                        + (complete ? "true" : "false")
                        + ",\n\"phase\":\""
                        + PerfBaseline.JsonEscape(phase)
                        + "\",\n\"progress\":"
                        + progress
                        + ",\n\"environment\":"
                        + environment
                        + ",\n\"snapshotMutations\":["
                        + string.Join(",", mutations)
                        + "],\n\"scenarios\":[\n"
                        + string.Join(",\n", scenarios)
                        + "\n]}\n"
                );
            };
            cleanup.Defer(writeReport);
            writeReport();
            environment = PerfBaseline.BuildEnvironmentJson();
            foreach (int size in new[] { 10000, 100000, 1000000 })
            {
                yield return MeasureSize(
                    size,
                    scenarios,
                    mutations,
                    (stage, count) =>
                    {
                        bool changed = !string.Equals(phase, stage, StringComparison.Ordinal);
                        phase = stage;
                        progress = count;
                        if (changed)
                        {
                            writeReport();
                        }
                    },
                    writeReport
                );
            }
            phase = "complete";
            complete = true;
        }

        [Test]
        public void ShouldWriteIncompleteReportWhenDisposedBeforeCreatingRecords()
        {
            using TestCleanupScope cleanup = new();
            byte[] previous = File.Exists(Output) ? File.ReadAllBytes(Output) : null;
            cleanup.Defer(() =>
            {
                if (previous == null)
                {
                    File.Delete(Output);
                }
                else
                {
                    File.WriteAllBytes(Output, previous);
                }
            });
            IEnumerator measurement = ShouldMeasureSyntheticMetadataScale();
            cleanup.Defer(() => ((IDisposable)measurement).Dispose());
            Assert.That(measurement.MoveNext());
            ((IDisposable)measurement).Dispose();
            ReportStatus report = UnityEngine.JsonUtility.FromJson<ReportStatus>(File.ReadAllText(Output));
            Assert.That(!report.complete);
            Assert.That(report.phase, Is.EqualTo("starting"));
            Assert.That(report.warmups, Is.EqualTo(Warmups));
            Assert.That(report.samples, Is.EqualTo(Samples));
        }

        [Serializable]
        private sealed class ReportStatus
        {
            public bool complete = true;
            public string phase = string.Empty;
            public int warmups = -1;
            public int samples = -1;
        }

        private sealed class MetadataScaleText
        {
            public string Text;
            public MetadataScaleText Child;
        }

        private sealed class Dataset : IDisposable
        {
            public AssetSearchDocument[] Documents { get; private set; }
            public AssetGuidTypeIndex Index { get; } = new(false);
            public AssetObjectCache View { get; }
            public AssetObjectCache PartialView { get; }
            public AssetChangeRefreshService Tracker { get; }
            public int Published { get; private set; }

            public Dataset(int size)
            {
                Documents = new AssetSearchDocument[size];
                View = new AssetObjectCache(Index.Metadata);
                PartialView = new AssetObjectCache(Index.Metadata);
                Tracker = new AssetChangeRefreshService(_ => true, (_, _, _) => Published++, (path, guid) => (guid, path, typeof(MetadataScaleBenchmarkTests)));
            }

            public void Dispose()
            {
                Tracker.Dispose();
                View.Clear();
                PartialView.Clear();
                Index.Cancel();
                Documents = null;
            }
        }
    }
}
