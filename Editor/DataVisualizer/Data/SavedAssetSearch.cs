namespace WallstopStudios.DataVisualizer.Editor.Data
{
    using System;
    using UnityEngine;
    using Utilities;

    [Serializable]
    public sealed class SavedAssetSearch
    {
        public int version = 1;
        public string name = string.Empty;
        public string query = string.Empty;
        public string typeIdentity = string.Empty;
        public TypeLabelFilterConfig labels = new();

        public SavedAssetSearch Clone()
        {
            return new SavedAssetSearch
            {
                version = version,
                name = name,
                query = query,
                typeIdentity = typeIdentity,
                labels = labels?.Clone() ?? new TypeLabelFilterConfig(),
            };
        }

        public EditorAssetQuery StartQuery()
        {
            if (version != 1)
            {
                throw new InvalidOperationException("This saved search uses an unsupported version. Create a new saved search.");
            }
            Type type = string.IsNullOrWhiteSpace(typeIdentity) ? null : Type.GetType(typeIdentity, throwOnError: false);
            if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters)
            {
                throw new InvalidOperationException("The saved type is unavailable. Restore its script or save a new search with a valid type. Scope was not expanded.");
            }
            return new EditorAssetQuery(new[] { type }, query, labels);
        }
    }
}
