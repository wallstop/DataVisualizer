namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.Serialization;
    using WallstopStudios.DataVisualizer;

    public sealed class RuntimeSignatureContractTests
    {
        /*
         * Pins the runtime compatibility member signatures that host projects
         * depend on when they derive from BaseDataObject, implement the
         * lifecycle interfaces, or use the compatibility attribute and context.
         * Renaming, removing, retyping, un-virtualizing, or re-accessing any
         * pinned member silently breaks user overrides and implementations, so
         * every change here must be a deliberate compatibility decision.
         */

        private const BindingFlags DeclaredMemberBindingFlags =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        private static readonly string[] BaseDataObjectFieldSignatures =
        {
            "CloneRegex : System.Text.RegularExpressions.Regex (protected, static, readonly)",
            "_assetGuid : System.String (protected internal, serialized, formerly \"initialGuid\")",
            "_description : System.String (protected, serialized, formerly \"description\")",
            "_title : System.String (protected internal, serialized, formerly \"title\")",
        };

        private static readonly string[] BaseDataObjectPropertySignatures =
        {
            "System.String Description { public virtual get; public virtual set; }",
            "System.String Id { public virtual get; }",
            "System.String Title { public virtual get; public virtual set; }",
        };

        private static readonly string[] BaseDataObjectMethodSignatures =
        {
            "public System.Int32 CompareTo(WallstopStudios.DataVisualizer.BaseDataObject)",
            "public virtual System.Void AfterClone(UnityEngine.ScriptableObject)",
            "public virtual System.Void AfterCreate()",
            "public virtual System.Void AfterRename(System.String)",
            "public virtual System.Void BeforeClone(UnityEngine.ScriptableObject)",
            "public virtual System.Void BeforeCreate()",
            "public virtual System.Void BeforeRename(System.String)",
            "public virtual UnityEngine.UIElements.VisualElement BuildGUI(WallstopStudios.DataVisualizer.DataVisualizerGUIContext)",
            "protected internal virtual System.Void OnValidate()",
        };

        private static readonly Dictionary<Type, string[]> InterfaceSignatures = new()
        {
            {
                typeof(ICreatable),
                new[] { "public System.Void AfterCreate()", "public System.Void BeforeCreate()" }
            },
            {
                typeof(IDuplicable),
                new[]
                {
                    "public System.Void AfterClone(UnityEngine.ScriptableObject)",
                    "public System.Void BeforeClone(UnityEngine.ScriptableObject)",
                }
            },
            {
                typeof(IRenamable),
                new[]
                {
                    "public System.Void AfterRename(System.String)",
                    "public System.Void BeforeRename(System.String)",
                }
            },
            { typeof(IDisplayable), new[] { "System.String Title { public virtual get; }" } },
            {
                typeof(IGUIProvider),
                new[]
                {
                    "public UnityEngine.UIElements.VisualElement BuildGUI(WallstopStudios.DataVisualizer.DataVisualizerGUIContext)",
                }
            },
            {
                typeof(IDataProcessor),
                new[]
                {
                    "System.Collections.Generic.IEnumerable<System.Type> Accepts { public virtual get; }",
                    "System.String Description { public virtual get; }",
                    "System.String Name { public virtual get; }",
                    "public default System.Int32 WillEffect(System.Type, System.Collections.Generic.IEnumerable<UnityEngine.ScriptableObject>)",
                    "public System.Void Process(System.Type, System.Collections.Generic.IEnumerable<UnityEngine.ScriptableObject>)",
                }
            },
        };

#if ODIN_INSPECTOR
        private static readonly string[] CustomDataVisualizationAttributePropertySignatures =
        {
            "System.String Namespace { public get; public set; }",
            "System.String TypeName { public get; public set; }",
            "System.Boolean UseOdinInspector { public get; public set; }",
        };
#else
        private static readonly string[] CustomDataVisualizationAttributePropertySignatures =
        {
            "System.String Namespace { public get; public set; }",
            "System.String TypeName { public get; public set; }",
        };
#endif

        private static readonly string[] DataVisualizerGUIContextFieldSignatures =
        {
            "serializedObject : UnityEditor.SerializedObject (public, readonly)",
        };

        private static string FormatType(Type type)
        {
            if (type.IsByRef)
            {
                return FormatType(type.GetElementType()) + "&";
            }

            if (type.IsArray)
            {
                return FormatType(type.GetElementType())
                    + "["
                    + new string(',', type.GetArrayRank() - 1)
                    + "]";
            }

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                string genericName = type.GetGenericTypeDefinition().FullName;
                int backtickIndex = genericName.IndexOf('`', StringComparison.Ordinal);
                if (backtickIndex > 0)
                {
                    genericName = genericName.Substring(0, backtickIndex);
                }

                Type[] genericArguments = type.GetGenericArguments();
                List<string> formattedArguments = new(genericArguments.Length);
                foreach (Type genericArgument in genericArguments)
                {
                    formattedArguments.Add(FormatType(genericArgument));
                }

                return genericName + "<" + string.Join(", ", formattedArguments) + ">";
            }

            return type.FullName ?? type.Name;
        }

        private static string FormatAccess(MethodInfo method)
        {
            if (method.IsPublic)
            {
                return "public";
            }

            if (method.IsFamilyOrAssembly)
            {
                return "protected internal";
            }

            if (method.IsFamily)
            {
                return "protected";
            }

            if (method.IsAssembly)
            {
                return "internal";
            }

            return "private";
        }

        private static string FormatFieldAccess(FieldInfo field)
        {
            if (field.IsPublic)
            {
                return "public";
            }

            if (field.IsFamilyOrAssembly)
            {
                return "protected internal";
            }

            if (field.IsFamily)
            {
                return "protected";
            }

            if (field.IsAssembly)
            {
                return "internal";
            }

            return "private";
        }

        private static bool IsEffectivelyVirtual(MethodInfo method)
        {
            return method != null && method.IsVirtual && !method.IsFinal;
        }

        private static string BuildFieldSignature(FieldInfo field)
        {
            List<string> traits = new(4) { FormatFieldAccess(field) };
            if (field.IsStatic)
            {
                traits.Add("static");
            }

            if (field.IsInitOnly)
            {
                traits.Add("readonly");
            }

            if (field.IsDefined(typeof(SerializeField), inherit: false))
            {
                traits.Add("serialized");
            }

            FormerlySerializedAsAttribute alias =
                field.GetCustomAttribute<FormerlySerializedAsAttribute>(inherit: false);
            if (alias != null)
            {
                traits.Add("formerly \"" + alias.oldName + "\"");
            }

            return $"{field.Name} : {FormatType(field.FieldType)} ({string.Join(", ", traits)})";
        }

        private static string BuildClassMethodSignature(MethodInfo method)
        {
            string staticMarker = method.IsStatic ? "static " : string.Empty;
            string virtualMarker = IsEffectivelyVirtual(method) ? "virtual " : string.Empty;
            return $"{FormatAccess(method)} {staticMarker}{virtualMarker}{FormatType(method.ReturnType)} {method.Name}({string.Join(", ", BuildParameterTypeNames(method))})";
        }

        private static string BuildPropertySignature(PropertyInfo property)
        {
            return $"{FormatType(property.PropertyType)} {property.Name} {{ {BuildAccessorText(property)} }}";
        }

        private static string BuildInterfaceMethodSignature(MethodInfo method)
        {
            string defaultMarker = method.IsAbstract ? string.Empty : "default ";
            return $"{FormatAccess(method)} {defaultMarker}{FormatType(method.ReturnType)} {method.Name}({string.Join(", ", BuildParameterTypeNames(method))})";
        }

        private static string BuildAccessorText(PropertyInfo property)
        {
            MethodInfo getter = property.GetGetMethod(true);
            MethodInfo setter = property.GetSetMethod(true);
            List<string> accessorNames = new(2);
            if (getter != null)
            {
                string virtualMarker = IsEffectivelyVirtual(getter) ? " virtual" : string.Empty;
                accessorNames.Add($"{FormatAccess(getter)}{virtualMarker} get;");
            }

            if (setter != null)
            {
                string virtualMarker = IsEffectivelyVirtual(setter) ? " virtual" : string.Empty;
                accessorNames.Add($"{FormatAccess(setter)}{virtualMarker} set;");
            }

            return string.Join(" ", accessorNames);
        }

        private static List<string> BuildParameterTypeNames(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            List<string> parameterTypeNames = new(parameters.Length);
            foreach (ParameterInfo parameter in parameters)
            {
                parameterTypeNames.Add(FormatType(parameter.ParameterType));
            }

            return parameterTypeNames;
        }

        private static List<string> CollectFieldSignatures(Type type)
        {
            List<string> signatures = new();
            foreach (FieldInfo field in type.GetFields(DeclaredMemberBindingFlags))
            {
                if (field.Name.StartsWith("<", StringComparison.Ordinal))
                {
                    continue;
                }

                signatures.Add(BuildFieldSignature(field));
            }

            return signatures;
        }

        private static List<string> CollectPropertySignatures(Type type)
        {
            List<string> signatures = new();
            foreach (PropertyInfo property in type.GetProperties(DeclaredMemberBindingFlags))
            {
                signatures.Add(BuildPropertySignature(property));
            }

            return signatures;
        }

        private static List<string> CollectClassMethodSignatures(Type type)
        {
            List<string> signatures = new();
            foreach (MethodInfo method in type.GetMethods(DeclaredMemberBindingFlags))
            {
                if (method.IsPrivate)
                {
                    continue;
                }

                if (IsPropertyAccessor(method))
                {
                    continue;
                }

                signatures.Add(BuildClassMethodSignature(method));
            }

            return signatures;
        }

        private static List<string> CollectInterfaceSignatures(Type interfaceType)
        {
            List<string> signatures = new();
            foreach (
                PropertyInfo property in interfaceType.GetProperties(DeclaredMemberBindingFlags)
            )
            {
                signatures.Add(BuildPropertySignature(property));
            }

            foreach (MethodInfo method in interfaceType.GetMethods(DeclaredMemberBindingFlags))
            {
                if (IsPropertyAccessor(method))
                {
                    continue;
                }

                signatures.Add(BuildInterfaceMethodSignature(method));
            }

            return signatures;
        }

        private static bool IsPropertyAccessor(MethodInfo method)
        {
            return method.IsSpecialName
                && (
                    method.Name.StartsWith("get_", StringComparison.Ordinal)
                    || method.Name.StartsWith("set_", StringComparison.Ordinal)
                );
        }

        private static void AssertSignaturesMatch(
            List<string> actualSignatures,
            IReadOnlyList<string> expectedSignatures,
            string surfaceName
        )
        {
            HashSet<string> expectedSignaturesSet = new(expectedSignatures, StringComparer.Ordinal);
            HashSet<string> actualSignatureSet = new(actualSignatures, StringComparer.Ordinal);
            List<string> missing = new();
            List<string> extra = new();
            foreach (string expectedSignature in expectedSignatures)
            {
                if (!actualSignatureSet.Contains(expectedSignature))
                {
                    missing.Add(expectedSignature);
                }
            }

            foreach (string actualSignature in actualSignatures)
            {
                if (!expectedSignaturesSet.Contains(actualSignature))
                {
                    extra.Add(actualSignature);
                }
            }

            if (missing.Count == 0 && extra.Count == 0)
            {
                return;
            }

            missing.Sort(StringComparer.Ordinal);
            extra.Sort(StringComparer.Ordinal);
            Assert.Fail(
                $"Runtime {surfaceName} surface drifted. Missing: [{string.Join(", ", missing)}]. Extra: [{string.Join(", ", extra)}]. Update RuntimeSignatureContractTests deliberately when the compatibility surface changes."
            );
        }

        [Test]
        public void ShouldPreserveBaseDataObjectDerivationSurfaceWhenHostCodeDerivesFromIt()
        {
            Type baseDataObjectType = typeof(BaseDataObject);
            AssertSignaturesMatch(
                CollectFieldSignatures(baseDataObjectType),
                BaseDataObjectFieldSignatures,
                "BaseDataObject fields"
            );
            AssertSignaturesMatch(
                CollectPropertySignatures(baseDataObjectType),
                BaseDataObjectPropertySignatures,
                "BaseDataObject properties"
            );
            AssertSignaturesMatch(
                CollectClassMethodSignatures(baseDataObjectType),
                BaseDataObjectMethodSignatures,
                "BaseDataObject methods"
            );
        }

        [Test]
        public void ShouldPreserveLifecycleInterfaceSignaturesWhenHostCodeImplementsThem()
        {
            foreach (KeyValuePair<Type, string[]> interfaceSignatures in InterfaceSignatures)
            {
                Type interfaceType = interfaceSignatures.Key;
                AssertSignaturesMatch(
                    CollectInterfaceSignatures(interfaceType),
                    interfaceSignatures.Value,
                    $"{interfaceType.FullName} members"
                );
            }
        }

        [Test]
        public void ShouldPreserveCompatibilityTypeMemberSignaturesWhenHostCodeUsesThem()
        {
            AssertSignaturesMatch(
                CollectFieldSignatures(typeof(DataVisualizerGUIContext)),
                DataVisualizerGUIContextFieldSignatures,
                "DataVisualizerGUIContext fields"
            );
            AssertSignaturesMatch(
                CollectPropertySignatures(typeof(CustomDataVisualizationAttribute)),
                CustomDataVisualizationAttributePropertySignatures,
                "CustomDataVisualizationAttribute properties"
            );
        }
    }
}
