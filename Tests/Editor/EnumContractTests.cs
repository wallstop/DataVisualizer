namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Linq;
    using System.Reflection;
    using NUnit.Framework;

    public sealed class EnumContractTests
    {
        private static readonly Assembly[] PackageAssemblies =
        {
            typeof(global::WallstopStudios.DataVisualizer.BaseDataObject).Assembly,
            typeof(global::WallstopStudios.DataVisualizer.Editor.DataVisualizer).Assembly,
            typeof(EnumContractTests).Assembly,
        };

        [Test]
        public void ShouldReserveObsoleteZeroValueWhenEnumIsDeclaredInPackage()
        {
            Type[] enumTypes = PackageAssemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type =>
                    type.IsEnum
                    && type.Namespace?.StartsWith(
                        "WallstopStudios.DataVisualizer",
                        StringComparison.Ordinal
                    ) == true
                )
                .Distinct()
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();

            Assert.IsNotEmpty(enumTypes);
            foreach (Type enumType in enumTypes)
            {
                object zeroValue = Enum.ToObject(enumType, 0);
                string zeroMemberName = Enum.GetName(enumType, zeroValue);
                Assert.IsNotNull(zeroMemberName, $"{enumType.FullName} must declare a zero value.");

                FieldInfo zeroMember = enumType.GetField(zeroMemberName);
                Assert.IsNotNull(
                    zeroMember.GetCustomAttribute<ObsoleteAttribute>(),
                    $"{enumType.FullName}.{zeroMemberName} must be marked obsolete."
                );
            }
        }
    }
}
