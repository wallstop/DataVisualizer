namespace WallstopStudios.DataVisualizer.Editor.Search
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEngine;

    // Contains copied text only; querying a document never reads a Unity object.
    public sealed class AssetSearchDocument
    {
        public string Guid { get; }
        public string Name { get; }
        public string TypeName { get; }
        public string TypeFullName { get; }

        private readonly List<(string Name, string Value)> _fields = new();

        public AssetSearchDocument(string guid, string name, Type type, object fieldSource)
        {
            Guid = guid ?? string.Empty;
            Name = name ?? string.Empty;
            TypeName = type.Name;
            TypeFullName = type.FullName;
            CaptureFields(fieldSource, 0, new HashSet<object>());
        }

        public SearchResultMatchInfo Match(string[] terms)
        {
            SearchResultMatchInfo result = new();
            if (terms == null)
            {
                return result;
            }

            foreach (string term in terms)
            {
                if (string.IsNullOrWhiteSpace(term))
                {
                    continue;
                }
                bool primaryMatch = false;
                if (Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    result.AddMatchedField(new MatchDetail(term) { fieldName = MatchSource.ObjectName, matchedValue = Name });
                    primaryMatch = true;
                }
                if (TypeName.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    result.AddMatchedField(new MatchDetail(term) { fieldName = MatchSource.TypeName, matchedValue = TypeName });
                    primaryMatch = true;
                }
                if (!string.IsNullOrWhiteSpace(Guid) && string.Equals(Guid, term, StringComparison.OrdinalIgnoreCase))
                {
                    result.AddMatchedField(new MatchDetail(term) { fieldName = MatchSource.Guid, matchedValue = Guid });
                    primaryMatch = true;
                }
                if (primaryMatch)
                {
                    result.isMatch = true;
                    continue;
                }
                foreach (var field in _fields)
                {
                    if (field.Value.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        result.AddMatchedField(new MatchDetail(term) { fieldName = field.Name, matchedValue = field.Value });
                        result.isMatch = true;
                        break;
                    }
                }
            }
            return result;
        }

        private void CaptureFields(object source, int depth, HashSet<object> visited)
        {
            if (source == null || 2 < depth)
            {
                return;
            }
            Type type = source.GetType();
            if (
                type.IsPrimitive
                || type == typeof(Vector2)
                || type == typeof(Vector3)
                || type == typeof(Vector4)
                || type == typeof(Quaternion)
                || type == typeof(Color)
                || type == typeof(Rect)
                || type == typeof(Bounds)
                || (!type.IsValueType && !visited.Add(source))
            )
            {
                return;
            }
            try
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    object value = field.GetValue(source);
                    if (value == null)
                    {
                        continue;
                    }
                    if (field.FieldType == typeof(string))
                    {
                        string text = (string)value;
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            _fields.Add((field.Name, text));
                        }
                    }
                    else if (
                        (field.FieldType.IsClass || field.FieldType is { IsValueType: true, IsPrimitive: false }) && !typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)
                    )
                    {
                        CaptureFields(value, depth + 1, visited);
                    }
                }
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }
    }
}
