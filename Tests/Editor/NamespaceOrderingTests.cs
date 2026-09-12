namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor;
    using WallstopStudios.DataVisualizer.Editor.Data;
    using CollisionA = WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.First.Data;
    using CollisionB = WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.Second.Data;

    public sealed class NamespaceOrderingTests
    {
        private static TestCaseData Case(
            string name,
            string[] managedTypeNames,
            string[] removedTypeNames,
            string[] expectedTypeNames
        ) => new TestCaseData(managedTypeNames, removedTypeNames, expectedTypeNames).SetName(name);

        private static IEnumerable<TestCaseData> Cases()
        {
            // Whitespace removal names are ignored, so the managed list is copied verbatim.
            yield return Case(
                "Whitespace_removals_copy_duplicates_verbatim",
                new[] { "Gameplay.EnemyData", "UI.MenuData", "Gameplay.EnemyData" },
                new[] { " ", "", null },
                new[] { "Gameplay.EnemyData", "UI.MenuData", "Gameplay.EnemyData" }
            );

            yield return Case(
                "Null_managed_returns_empty",
                null,
                new[] { "Gameplay.EnemyData" },
                new string[] { }
            );

            yield return Case(
                "Dedupe_keeps_first_occurrence",
                new[]
                {
                    "Gameplay.EnemyData",
                    "Gameplay.SpawnData",
                    "UI.MenuData",
                    "Gameplay.EnemyData",
                },
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

            yield return Case(
                "Removal_drops_every_ordinal_equal_name",
                new[] { "Gameplay.EnemyData", "UI.MenuData" },
                new[] { "UI.MenuData" },
                new[] { "Gameplay.EnemyData" }
            );
        }

        [Test]
        public void ShouldOrderTypesByFullNameWhenShortTypeNamesCollide()
        {
            Type firstType = typeof(CollisionA.OrderCollisionData);
            Type secondType = typeof(CollisionB.OrderCollisionData);
            List<Type> types = new() { firstType, secondType };
            List<string> persistedFullNameOrder = new() { secondType.FullName, firstType.FullName };

            types.Sort(
                (lhs, rhs) =>
                    NamespaceTypeOrder.CompareTypesByFullNameOrder(lhs, rhs, persistedFullNameOrder)
            );

            CollectionAssert.AreEqual(
                persistedFullNameOrder,
                types.Select(type => type.FullName).ToList()
            );
            Assert.AreEqual(firstType.Name, secondType.Name);
        }

        [Test]
        public void ShouldFallBackToFullNameOrderingWhenNoCustomOrderExists()
        {
            Type firstType = typeof(CollisionA.OrderCollisionData);
            Type secondType = typeof(CollisionB.OrderCollisionData);
            List<Type> types = new() { secondType, firstType };

            types.Sort(
                (lhs, rhs) =>
                    NamespaceTypeOrder.CompareTypesByFullNameOrder(lhs, rhs, Array.Empty<string>())
            );

            CollectionAssert.AreEqual(
                new[] { firstType.FullName, secondType.FullName }.OrderBy(
                    typeFullName => typeFullName,
                    StringComparer.Ordinal
                ),
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
                new[] { firstType.FullName, secondType.FullName }.OrderBy(
                    typeFullName => typeFullName,
                    StringComparer.Ordinal
                ),
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
                new[] { firstType.FullName, secondType.FullName }.OrderBy(
                    typeFullName => typeFullName,
                    StringComparer.Ordinal
                ),
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
            string[] managedTypeNames =
            {
                "Gameplay.EnemyData",
                "UI.MenuData",
                "Gameplay.SpawnData",
            };
            string[] removedTypeNames = { "Gameplay.EnemyData", "Gameplay.SpawnData" };

            List<string> result = NamespaceController.RemoveManagedTypeNames(
                managedTypeNames,
                removedTypeNames
            );

            CollectionAssert.AreEqual(new[] { "UI.MenuData" }, result);
        }

        [TestCaseSource(nameof(Cases))]
        public void ShouldRemoveManagedTypeNamesWhenRemovalsDiffer(
            string[] managedTypeNames,
            string[] removedTypeNames,
            string[] expectedTypeNames
        )
        {
            List<string> result = NamespaceController.RemoveManagedTypeNames(
                managedTypeNames,
                removedTypeNames
            );

            CollectionAssert.AreEqual(expectedTypeNames, result);
        }

        [Test]
        public void ShouldReturnIndependentCopyWhenRemovalsAreNull()
        {
            List<string> managedTypeNames = new() { "Gameplay.EnemyData", "UI.MenuData" };

            List<string> result = NamespaceController.RemoveManagedTypeNames(
                managedTypeNames,
                null
            );

            CollectionAssert.AreEqual(managedTypeNames, result);
            Assert.AreNotSame(managedTypeNames, result);
            managedTypeNames.Add("Gameplay.SpawnData");
            CollectionAssert.AreEqual(new[] { "Gameplay.EnemyData", "UI.MenuData" }, result);
        }
    }
}
