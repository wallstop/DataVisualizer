namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
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
            HashSet<Type> distinctEnumTypes = new();
            foreach (Assembly assembly in PackageAssemblies)
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (
                        type.IsEnum
                        && type.Namespace?.StartsWith(
                            "WallstopStudios.DataVisualizer",
                            StringComparison.Ordinal
                        ) == true
                    )
                    {
                        distinctEnumTypes.Add(type);
                    }
                }
            }

            List<Type> enumTypes = new(distinctEnumTypes);
            enumTypes.Sort(
                (lhs, rhs) => StringComparer.Ordinal.Compare(lhs.FullName, rhs.FullName)
            );

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
