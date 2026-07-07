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
        [Test]
        public void Should_OrderTypesByFullName_When_ShortTypeNamesCollide()
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
        public void Should_FallBackToFullNameOrdering_When_NoCustomOrderExists()
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
        public void Should_OrderTypesByFullName_When_SeedingPersistedOrder()
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
        public void Should_UseFullNameTieBreaker_When_DisplayNamesCollide()
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
        public void Should_FindTypeByFullName_When_ShortTypeNamesCollide()
        {
            Type firstType = typeof(CollisionA.OrderCollisionData);
            Type secondType = typeof(CollisionB.OrderCollisionData);
            List<Type> types = new() { firstType, secondType };

            Type resolvedType = NamespaceTypeOrder.FindTypeByFullName(types, secondType.FullName);

            Assert.AreSame(secondType, resolvedType);
            Assert.AreEqual(firstType.Name, secondType.Name);
        }

        [Test]
        public void Should_PreserveOtherNamespaces_When_RemovingManagedTypeNames()
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
    }
}

namespace WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.First.Data
{
    using UnityEngine;

    public sealed class OrderCollisionData : ScriptableObject { }
}

namespace WallstopStudios.DataVisualizer.Tests.Editor.TypeIdentityCollision.Second.Data
{
    using UnityEngine;

    public sealed class OrderCollisionData : ScriptableObject { }
}
