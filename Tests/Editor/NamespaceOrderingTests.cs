namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using WallstopStudios.DataVisualizer.Editor.Styles;
    using CollisionA = WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.First.Data;
    using CollisionB = WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.Second.Data;

    public sealed class NamespaceOrderingTests
    {
        private static KeyValuePair<string, List<Type>>[] InsertionOrderBaseline(Dictionary<string, List<Type>> managedTypes, Dictionary<string, int> namespaceOrder)
        {
            KeyValuePair<string, List<Type>>[] entries = new KeyValuePair<string, List<Type>>[managedTypes.Count];
            int[] keys = new int[entries.Length];
            int index = 0;
            foreach (KeyValuePair<string, List<Type>> entry in managedTypes)
            {
                entries[index] = entry;
                keys[index] = namespaceOrder.GetValueOrDefault(entry.Key, namespaceOrder.Count);
                index++;
            }
            for (int outer = 1; outer < entries.Length; outer++)
            {
                int rank = keys[outer];
                KeyValuePair<string, List<Type>> entry = entries[outer];
                int inner = outer - 1;
                while (0 <= inner && rank < keys[inner])
                {
                    keys[inner + 1] = keys[inner];
                    entries[inner + 1] = entries[inner];
                    inner--;
                }
                keys[inner + 1] = rank;
                entries[inner + 1] = entry;
            }
            return entries;
        }

        private static TestCaseData Case(string name, string[] managedTypeNames, string[] removedTypeNames, string[] expectedTypeNames) =>
            new TestCaseData(managedTypeNames, removedTypeNames, expectedTypeNames).SetName(name);

        private static IEnumerable<TestCaseData> Cases()
        {
            // Whitespace removal names are ignored, so the managed list is copied verbatim.
            yield return Case(
                "Whitespace_removals_copy_duplicates_verbatim",
                new[] { "Gameplay.EnemyData", "UI.MenuData", "Gameplay.EnemyData" },
                new[] { " ", "", null },
                new[] { "Gameplay.EnemyData", "UI.MenuData", "Gameplay.EnemyData" }
            );

            yield return Case("Null_managed_returns_empty", null, new[] { "Gameplay.EnemyData" }, new string[] { });

            yield return Case(
                "Dedupe_keeps_first_occurrence",
                new[] { "Gameplay.EnemyData", "Gameplay.SpawnData", "UI.MenuData", "Gameplay.EnemyData" },
                new[] { "Gameplay.SpawnData" },
                new[] { "Gameplay.EnemyData", "UI.MenuData" }
            );

            yield return Case(
                "Removal_is_ordinal_case_sensitive",
                new[] { "Gameplay.EnemyData", "gameplay.enemydata" },
                new[] { "GAMEPLAY.ENEMYDATA" },
                new[] { "Gameplay.EnemyData", "gameplay.enemydata" }
            );

            yield return Case(
                "Null_managed_names_are_kept_once",
                new[] { "Gameplay.EnemyData", null, "UI.MenuData" },
                new[] { "UI.MenuData" },
                new[] { "Gameplay.EnemyData", null }
            );

            yield return Case("Removal_drops_every_ordinal_equal_name", new[] { "Gameplay.EnemyData", "UI.MenuData" }, new[] { "UI.MenuData" }, new[] { "Gameplay.EnemyData" });
        }

        [Test, Explicit("Component measurement; creates no Unity assets or catalog visuals")]
        public void ShouldMeasureNamespaceOrderingAtScale()
        {
            BenchmarkCodeIdentity production = new(
                typeof(NamespaceController).Assembly,
                "Packages/com.wallstop-studios.data-visualizer/Editor/DataVisualizer/NamespaceController.cs"
            );
            BenchmarkCodeIdentity driver = new(typeof(NamespaceOrderingTests).Assembly, "Packages/com.wallstop-studios.data-visualizer/Tests/Editor/NamespaceOrderingTests.cs");
            production.Verify();
            driver.Verify();
            StringBuilder json = new();
            json.Append("{\"complete\":false,\"unity\":\"").Append(PerfBaseline.JsonEscape(Application.unityVersion));
            json.Append("\",\"os\":\"").Append(PerfBaseline.JsonEscape(SystemInfo.operatingSystem));
            json.Append("\",\"cpu\":\"").Append(PerfBaseline.JsonEscape(SystemInfo.processorType));
            json.Append("\",\"productionSource\":\"").Append(production.SourceSha256);
            json.Append("\",\"productionModule\":\"").Append(production.LoadedModuleId);
            json.Append("\",\"driverSource\":\"").Append(driver.SourceSha256);
            json.Append("\",\"driverModule\":\"").Append(driver.LoadedModuleId);
            json.Append("\",\"warmups\":5,\"samplesPerCondition\":30,\"samples\":[");
            bool first = true;
            Stopwatch budget = Stopwatch.StartNew();
            bool complete = false;
            Directory.CreateDirectory("Temp");
            using TestCleanupScope reportCleanup = new();
            reportCleanup.Defer(() =>
            {
                string report =
                    json.ToString()
                    + "],\"verifiedAfter\":"
                    + (complete ? "true" : "false")
                    + ",\"elapsedMilliseconds\":"
                    + budget.Elapsed.TotalMilliseconds.ToString("R", CultureInfo.InvariantCulture)
                    + ",\"failure\":\""
                    + (complete ? "" : "Experiment did not complete; see the NUnit result for the active condition and failure.")
                    + "\"}";
                if (complete)
                {
                    report = report.Replace("\"complete\":false", "\"complete\":true");
                }
                File.WriteAllText("Temp/catalog-ordering.json", report);
            });
            foreach (int count in new[] { 100, 1000, 10000 })
            {
                foreach (string ordering in new[] { "forward", "reverse", "ties" })
                {
                    Dictionary<string, List<Type>> managed = new(StringComparer.Ordinal);
                    Dictionary<string, int> ranks = new(StringComparer.Ordinal);
                    for (int i = 0; i < count; i++)
                    {
                        string key = "Namespace" + i;
                        managed.Add(key, new List<Type>());
                        ranks.Add(
                            key,
                            ordering == "reverse" ? count - i
                                : ordering == "ties" ? i % 7
                                : i
                        );
                    }
                    string[] expected = managed.OrderBy(entry => ranks[entry.Key]).Select(entry => entry.Key).ToArray();
                    for (int repetition = -5; repetition < 30; repetition++)
                    {
                        // Alternate the pair order to limit consistent first-run bias.
                        foreach (bool baseline in repetition % 2 == 0 ? new[] { true, false } : new[] { false, true })
                        {
                            Assert.That(
                                budget.Elapsed.TotalMinutes,
                                Is.LessThan(10),
                                $"Ten-minute component budget: {count}/{ordering}/baseline={baseline}/repetition={repetition}"
                            );
                            long allocated = GC.GetAllocatedBytesForCurrentThread();
                            long started = Stopwatch.GetTimestamp();
                            KeyValuePair<string, List<Type>>[] actual = baseline
                                ? InsertionOrderBaseline(managed, ranks)
                                : NamespaceController.GetOrderedNamespaceEntries(managed, ranks);
                            double milliseconds = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                            Assert.That(actual.Select(entry => entry.Key), Is.EqualTo(expected));
                            if (repetition < 0)
                            {
                                continue;
                            }
                            if (!first)
                            {
                                json.Append(',');
                            }
                            first = false;
                            json.Append("{\"count\":").Append(count).Append(",\"order\":\"").Append(ordering);
                            json.Append("\",\"baseline\":").Append(baseline ? "true" : "false");
                            json.Append(",\"repetition\":").Append(repetition);
                            json.Append(",\"milliseconds\":").Append(milliseconds.ToString("R", CultureInfo.InvariantCulture));
                            json.Append(",\"allocatedBytes\":").Append(allocated).Append('}');
                        }
                    }
                }
            }
            production.Verify();
            driver.Verify();
            complete = true;
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(1000)]
        public void ShouldPreserveStableNamespaceOrderAndTypeLists(int count)
        {
            Dictionary<string, List<Type>> managed = new(StringComparer.Ordinal);
            Dictionary<string, int> order = new(StringComparer.Ordinal);
            System.Random random = new(174);
            for (int i = 0; i < count; i++)
            {
                string key = "Namespace" + i;
                managed.Add(key, new List<Type> { typeof(NamespaceOrderingTests) });
                if (i % 4 != 0)
                {
                    order.Add(
                        key,
                        i % 7 == 0 ? int.MinValue
                            : i % 7 == 1 ? int.MaxValue
                            : random.Next(-5, 5)
                    );
                }
            }
            string[] expected = managed.OrderBy(entry => order.GetValueOrDefault(entry.Key, order.Count)).Select(entry => entry.Key).ToArray();
            KeyValuePair<string, List<Type>>[] actual = NamespaceController.GetOrderedNamespaceEntries(managed, order);
            Assert.That(actual.Select(entry => entry.Key), Is.EqualTo(expected), "Equal ranks and missing ranks keep input order");
            Assert.That(managed.Keys, Is.EqualTo(Enumerable.Range(0, count).Select(i => "Namespace" + i)), "Sorting must not mutate the catalog");
            foreach (KeyValuePair<string, List<Type>> entry in actual)
            {
                Assert.That(entry.Value, Is.SameAs(managed[entry.Key]), "Type lists retain their shared mutation owner");
            }
        }

        [Test]
        public void ShouldOrderReverseRankedNamespaces()
        {
            Dictionary<string, List<Type>> managed = new(StringComparer.Ordinal);
            Dictionary<string, int> order = new(StringComparer.Ordinal);
            for (int i = 0; i < 2000; i++)
            {
                string key = "Namespace" + i;
                managed.Add(key, new List<Type>());
                order.Add(key, 2000 - i);
            }
            Assert.That(NamespaceController.GetOrderedNamespaceEntries(managed, order).Select(entry => entry.Key), Is.EqualTo(managed.Keys.Reverse()));
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        public void ShouldSkipFilteredTypesAndNonTypeRowsWhenNavigating(bool forward, bool hideCurrent)
        {
            VisualElement parent = new();
            VisualElement first = new() { userData = typeof(CollisionA.OrderCollisionData) };
            VisualElement hidden = new() { userData = typeof(NamespaceOrderingTests) };
            VisualElement last = new() { userData = typeof(CollisionB.OrderCollisionData) };
            hidden.style.display = DisplayStyle.None;
            parent.Add(first);
            parent.Add(hidden);
            parent.Add(new VisualElement());
            parent.Add(last);
            if (hideCurrent)
            {
                first.style.display = DisplayStyle.None;
            }
            Assert.That(NamespaceController.GetAdjacentVisibleType(first, forward), Is.EqualTo(typeof(CollisionB.OrderCollisionData)));
            Assert.That(
                NamespaceController.GetAdjacentVisibleType(last, forward),
                Is.EqualTo(hideCurrent ? typeof(CollisionB.OrderCollisionData) : typeof(CollisionA.OrderCollisionData))
            );
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ShouldKeepSelectionWhenNoCatalogTypeIsVisible(bool forward)
        {
            VisualElement group = new();
            VisualElement parent = new();
            VisualElement current = new() { userData = typeof(NamespaceOrderingTests) };
            group.Add(parent);
            parent.Add(current);
            current.AddToClassList(StyleConstants.SelectedClass);
            group.style.display = DisplayStyle.None;
            Assert.That(NamespaceController.GetAdjacentVisibleType(current, forward) == null);
            Assert.That(current.ClassListContains(StyleConstants.SelectedClass), Is.True);
            group.style.display = DisplayStyle.Flex;
            current.style.display = DisplayStyle.None;
            Assert.That(NamespaceController.GetAdjacentVisibleType(current, forward) == null);
            current.style.display = DisplayStyle.Flex;
            current.style.visibility = Visibility.Hidden;
            Assert.That(NamespaceController.GetAdjacentVisibleType(current, forward) == null);
            Assert.That(NamespaceController.GetAdjacentVisibleType(null, forward) == null);
            Assert.That(NamespaceController.GetAdjacentVisibleType(new VisualElement(), forward) == null);
        }

        [Test]
        public void ShouldOrderTypesByFullNameWhenShortTypeNamesCollide()
        {
            Type firstType = typeof(CollisionA.OrderCollisionData);
            Type secondType = typeof(CollisionB.OrderCollisionData);
            List<Type> types = new() { firstType, secondType };
            List<string> persistedFullNameOrder = new() { secondType.FullName, firstType.FullName };

            types.Sort((lhs, rhs) => NamespaceTypeOrder.CompareTypesByFullNameOrder(lhs, rhs, persistedFullNameOrder));

            CollectionAssert.AreEqual(persistedFullNameOrder, types.Select(type => type.FullName).ToList());
            Assert.AreEqual(firstType.Name, secondType.Name);
        }

        [Test]
        public void ShouldFallBackToFullNameOrderingWhenNoCustomOrderExists()
        {
            Type firstType = typeof(CollisionA.OrderCollisionData);
            Type secondType = typeof(CollisionB.OrderCollisionData);
            List<Type> types = new() { secondType, firstType };

            types.Sort((lhs, rhs) => NamespaceTypeOrder.CompareTypesByFullNameOrder(lhs, rhs, Array.Empty<string>()));

            CollectionAssert.AreEqual(
                new[] { firstType.FullName, secondType.FullName }.OrderBy(typeFullName => typeFullName, StringComparer.Ordinal),
                types.Select(type => type.FullName).ToList()
            );
        }

        [Test]
        public void ShouldOrderTypesByFullNameWhenSeedingPersistedOrder()
        {
            Type firstType = typeof(CollisionA.OrderCollisionData);
            Type secondType = typeof(CollisionB.OrderCollisionData);
            List<Type> types = new() { secondType, firstType };

            types.Sort(NamespaceTypeOrder.CompareTypesByFullName);

            CollectionAssert.AreEqual(
                new[] { firstType.FullName, secondType.FullName }.OrderBy(typeFullName => typeFullName, StringComparer.Ordinal),
                types.Select(type => type.FullName).ToList()
            );
        }

        [Test]
        public void ShouldUseFullNameTieBreakerWhenDisplayNamesCollide()
        {
            Type firstType = typeof(CollisionA.OrderCollisionData);
            Type secondType = typeof(CollisionB.OrderCollisionData);
            List<Type> types = new() { secondType, firstType };

            types.Sort(NamespaceTypeOrder.CompareTypesByNameThenFullName);

            CollectionAssert.AreEqual(
                new[] { firstType.FullName, secondType.FullName }.OrderBy(typeFullName => typeFullName, StringComparer.Ordinal),
                types.Select(type => type.FullName).ToList()
            );
            Assert.AreEqual(firstType.Name, secondType.Name);
        }

        [Test]
        public void ShouldFindTypeByFullNameWhenShortTypeNamesCollide()
        {
            Type firstType = typeof(CollisionA.OrderCollisionData);
            Type secondType = typeof(CollisionB.OrderCollisionData);
            List<Type> types = new() { firstType, secondType };

            Type resolvedType = NamespaceTypeOrder.FindTypeByFullName(types, secondType.FullName);

            Assert.AreSame(secondType, resolvedType);
            Assert.AreEqual(firstType.Name, secondType.Name);
        }

        [Test]
        public void ShouldPreserveOtherNamespacesWhenRemovingManagedTypeNames()
        {
            string[] managedTypeNames = { "Gameplay.EnemyData", "UI.MenuData", "Gameplay.SpawnData" };
            string[] removedTypeNames = { "Gameplay.EnemyData", "Gameplay.SpawnData" };

            List<string> result = NamespaceController.RemoveManagedTypeNames(managedTypeNames, removedTypeNames);

            CollectionAssert.AreEqual(new[] { "UI.MenuData" }, result);
        }

        [TestCaseSource(nameof(Cases))]
        public void ShouldRemoveManagedTypeNamesWhenRemovalsDiffer(string[] managedTypeNames, string[] removedTypeNames, string[] expectedTypeNames)
        {
            List<string> result = NamespaceController.RemoveManagedTypeNames(managedTypeNames, removedTypeNames);

            CollectionAssert.AreEqual(expectedTypeNames, result);
        }

        [Test]
        public void ShouldReturnIndependentCopyWhenRemovalsAreNull()
        {
            List<string> managedTypeNames = new() { "Gameplay.EnemyData", "UI.MenuData" };

            List<string> result = NamespaceController.RemoveManagedTypeNames(managedTypeNames, null);

            CollectionAssert.AreEqual(managedTypeNames, result);
            Assert.AreNotSame(managedTypeNames, result);
            managedTypeNames.Add("Gameplay.SpawnData");
            CollectionAssert.AreEqual(new[] { "Gameplay.EnemyData", "UI.MenuData" }, result);
        }
    }
}
