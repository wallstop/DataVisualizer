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

        public static bool IsLoadableType(Type type)
        {
            if (type == null)
            {
                /*
                    MonoScript.GetClass() can return null for unsupported scripts. Treat those as not loadable instead of throwing.
                */
                return false;
            }

            return type != typeof(ScriptableObject)
                && typeof(ScriptableObject).IsAssignableFrom(type)
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

        /// <summary>Find eligible types in an asset or script folder without creating instances.</summary>
        public static List<Type> FindTypesInFolder(string folderPath, bool scripts)
        {
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                throw new ArgumentException("Use an existing Unity asset folder.", nameof(folderPath));
            }

            string filter = scripts ? "t:MonoScript" : $"t:{nameof(ScriptableObject)}";
            string[] guids = AssetDatabase.FindAssets(filter, new[] { folderPath });
            HashSet<Type> seen = new();
            List<Type> types = new();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Type type;
                if (scripts)
                {
                    MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    type = script == null ? null : script.GetClass();
                }
                else
                {
                    type = AssetDatabase.GetMainAssetTypeAtPath(path);
                }
                if (IsLoadableType(type) && seen.Add(type))
                {
                    types.Add(type);
                }
            }
            types.Sort(Data.NamespaceTypeOrder.CompareTypesByFullName);
            return types;
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
