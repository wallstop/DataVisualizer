namespace WallstopStudios.DataVisualizer.Editor.Search
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Text;
    using UnityEngine;

    // Contains copied text only; querying a document never reads a Unity object.
    public sealed class AssetSearchDocument
    {
        public Type ExactType { get; }
        public string Guid { get; }
        public string Name { get; }
        public string TypeName { get; }
        public string TypeFullName { get; }
        public string NormalizedAsciiText { get; }
        public string NormalizedOrdinalText { get; }

        private readonly List<(string Name, string Value)> _fields = new();

        public AssetSearchDocument(string guid, string name, Type type, object fieldSource)
        {
            ExactType = type ?? throw new ArgumentNullException(nameof(type));
            Guid = guid ?? string.Empty;
            Name = name ?? string.Empty;
            TypeName = type.Name;
            TypeFullName = type.FullName;
            CaptureFields(fieldSource, 0, new HashSet<object>());
            StringBuilder text = new(Name);
            text.Append('\0').Append(TypeName).Append('\0').Append(Guid);
            foreach (var field in _fields)
            {
                text.Append('\0').Append(field.Value);
            }
            string capturedText = text.ToString();
            NormalizedAsciiText = NormalizeAsciiOrNull(capturedText);
            if (NormalizedAsciiText == null)
            {
                NormalizedOrdinalText = NormalizeOrdinalOrNull(capturedText);
            }
        }

        public static int CompareIdentity(AssetSearchDocument left, AssetSearchDocument right)
        {
            int name = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
            if (name != 0)
            {
                return name;
            }
            int type = string.Compare(left.TypeFullName, right.TypeFullName, StringComparison.Ordinal);
            return type != 0 ? type : string.Compare(left.Guid, right.Guid, StringComparison.Ordinal);
        }

        public static string NormalizeAsciiOrNull(string text)
        {
            if (text == null)
            {
                return null;
            }
            foreach (char character in text)
            {
                if (127 < character)
                {
                    return null;
                }
            }
            return text.ToLowerInvariant();
        }

        /*
            OrdinalIgnoreCase uses invariant uppercase casing. Leave malformed UTF-16 on
            the original path so partial-surrogate matching stays runtime-owned. This is
            only a candidate filter; original text still decides every match.
        */
        public static string NormalizeOrdinalOrNull(string text)
        {
            if (text == null)
            {
                return null;
            }
            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];
                if (char.IsHighSurrogate(character))
                {
                    if (text.Length <= index + 1 || !char.IsLowSurrogate(text[index + 1]))
                    {
                        return null;
                    }
                    index++;
                }
                else if (char.IsLowSurrogate(character))
                {
                    return null;
                }
            }
            string normalized = text.ToUpperInvariant();
            return normalized.Length == text.Length ? normalized : null;
        }

        public SearchResultMatchInfo Match(string[] terms)
        {
            return MatchOrNull(terms) ?? new SearchResultMatchInfo();
        }

        public SearchResultMatchInfo MatchOrNull(string[] terms)
        {
            SearchResultMatchInfo result = null;
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
                    result ??= new SearchResultMatchInfo();
                    result.AddMatchedField(new MatchDetail(term) { fieldName = MatchSource.ObjectName, matchedValue = Name });
                    primaryMatch = true;
                }
                if (TypeName.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    result ??= new SearchResultMatchInfo();
                    result.AddMatchedField(new MatchDetail(term) { fieldName = MatchSource.TypeName, matchedValue = TypeName });
                    primaryMatch = true;
                }
                if (!string.IsNullOrWhiteSpace(Guid) && string.Equals(Guid, term, StringComparison.OrdinalIgnoreCase))
                {
                    result ??= new SearchResultMatchInfo();
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
                        result ??= new SearchResultMatchInfo();
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
