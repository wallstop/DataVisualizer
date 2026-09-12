namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer;

    public sealed class RuntimeAssemblySurfaceTests
    {
        private static readonly string[] PublicCompatibilityTypeFullNames =
        {
            "WallstopStudios.DataVisualizer.BaseDataObject",
            "WallstopStudios.DataVisualizer.CustomDataVisualizationAttribute",
            "WallstopStudios.DataVisualizer.DataVisualizerGUIContext",
            "WallstopStudios.DataVisualizer.ICreatable",
            "WallstopStudios.DataVisualizer.IDataProcessor",
            "WallstopStudios.DataVisualizer.IDisplayable",
            "WallstopStudios.DataVisualizer.IDuplicable",
            "WallstopStudios.DataVisualizer.IGUIProvider",
            "WallstopStudios.DataVisualizer.IRenamable",
        };

        private static readonly string[] InternalRuntimeTypeFullNames =
        {
            "WallstopStudios.DataVisualizer.ReadOnlyAttribute",
        };

        private static List<string> CollectRuntimeTypeFullNames(Func<Type, bool> predicate)
        {
            List<string> typeFullNames = new();
            foreach (Type type in typeof(BaseDataObject).Assembly.GetTypes())
            {
                if (predicate(type))
                {
                    typeFullNames.Add(type.FullName);
                }
            }

            typeFullNames.Sort(StringComparer.Ordinal);
            return typeFullNames;
        }

        private static void AssertTypeSurface(
            List<string> actualTypeFullNames,
            IReadOnlyList<string> expectedTypeFullNames,
            string surfaceName
        )
        {
            HashSet<string> actual = new(actualTypeFullNames, StringComparer.Ordinal);
            HashSet<string> expected = new(expectedTypeFullNames, StringComparer.Ordinal);
            List<string> missing = new();
            List<string> extra = new();
            foreach (string expectedTypeFullName in expected)
            {
                if (!actual.Contains(expectedTypeFullName))
                {
                    missing.Add(expectedTypeFullName);
                }
            }

            foreach (string actualTypeFullName in actual)
            {
                if (!expected.Contains(actualTypeFullName))
                {
                    extra.Add(actualTypeFullName);
                }
            }

            if (missing.Count == 0 && extra.Count == 0)
            {
                return;
            }

            missing.Sort(StringComparer.Ordinal);
            extra.Sort(StringComparer.Ordinal);
            Assert.Fail(
                $"Runtime assembly {surfaceName} surface drifted. Missing: [{string.Join(", ", missing)}]. Extra: [{string.Join(", ", extra)}]. Update RuntimeAssemblySurfaceTests deliberately when the compatibility surface changes."
            );
        }

        [Test]
        public void ShouldPreserveRuntimeAssemblyIdentityWhenPackageCompiles()
        {
            Assert.AreEqual(
                "WallstopStudios.DataVisualizer",
                typeof(BaseDataObject).Assembly.GetName().Name
            );
        }

        [Test]
        public void ShouldExposeOnlyCompatibilityTypesWhenPublicSurfaceIsEnumerated()
        {
            AssertTypeSurface(
                CollectRuntimeTypeFullNames(type => type.IsVisible),
                PublicCompatibilityTypeFullNames,
                "public"
            );
        }

        [Test]
        public void ShouldShipOnlyDeclaredTypesWhenRuntimeAssemblyIsEnumerated()
        {
            List<string> expectedDeclaredTypeFullNames = new(
                PublicCompatibilityTypeFullNames.Length + InternalRuntimeTypeFullNames.Length
            );
            expectedDeclaredTypeFullNames.AddRange(PublicCompatibilityTypeFullNames);
            expectedDeclaredTypeFullNames.AddRange(InternalRuntimeTypeFullNames);
            expectedDeclaredTypeFullNames.Sort(StringComparer.Ordinal);

            AssertTypeSurface(
                CollectRuntimeTypeFullNames(type => !string.IsNullOrEmpty(type.Namespace)),
                expectedDeclaredTypeFullNames,
                "declared"
            );
        }
    }
}
