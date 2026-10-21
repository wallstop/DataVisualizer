namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    /// <summary>A read-only snapshot of browser-eligible loaded types. Use on the Unity editor main thread.</summary>
    public sealed class EditorTypeCatalog
    {
        public IReadOnlyList<Type> Types { get; }

        public EditorTypeCatalog()
        {
            List<Type> types = new();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
            {
                if (IsLoadableType(type))
                {
                    types.Add(type);
                }
            }
            types.Sort((left, right) => StringComparer.Ordinal.Compare(left.AssemblyQualifiedName, right.AssemblyQualifiedName));
            Types = types.AsReadOnly();
        }

        internal static bool IsLoadableType(Type type)
        {
            if (type == null)
            {
                /*
                    MonoScript.GetClass() returns null for scripts without a MonoBehaviour
                    class. Treat those as not loadable instead of throwing.
                */
                return false;
            }

            return type != typeof(ScriptableObject)
                && !type.IsAbstract
                && !type.IsGenericType
                && !type.IsInterface
                && !type.IsNestedPrivate
                && !IsSubclassOf(type, typeof(Editor))
                && !IsSubclassOf(type, typeof(EditorWindow))
                && !IsSubclassOf(type, typeof(ScriptableSingleton<>))
                && type.Namespace?.StartsWith("UnityEditor", StringComparison.Ordinal) != true
                && type.Namespace?.StartsWith("UnityEngine", StringComparison.Ordinal) != true;
        }

        private static bool IsSubclassOf(Type typeToCheck, Type baseClass)
        {
            if (typeToCheck == null)
            {
                return false;
            }

            Type currentType = typeToCheck;

            while (currentType != null && currentType != typeof(object))
            {
                Type typeToCheckAgainst = currentType.IsGenericType ? currentType.GetGenericTypeDefinition() : currentType;
                if (typeToCheckAgainst == baseClass)
                {
                    return true;
                }
                currentType = currentType.BaseType;
            }
            return false;
        }

        public Type[] GetPage(int offset, int count)
        {
            if (offset < 0 || count < 1 || 1000 < count)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Use a nonnegative offset and count from 1 to 1000.");
            }
            Type[] page = new Type[Math.Min(count, Math.Max(0, Types.Count - offset))];
            for (int i = 0; i < page.Length; i++)
            {
                page[i] = Types[offset + i];
            }
            return page;
        }
    }
}
