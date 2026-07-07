namespace WallstopStudios.DataVisualizer.Editor.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    [Serializable]
    public sealed class NamespaceTypeOrder
    {
        public string namespaceKey = string.Empty;
        public List<string> typeNames = new();

        public static int CompareTypesByFullNameOrder(
            Type lhs,
            Type rhs,
            IReadOnlyList<string> typeFullNameOrder
        )
        {
            return CompareTypeFullNames(
                lhs?.FullName ?? string.Empty,
                rhs?.FullName ?? string.Empty,
                typeFullNameOrder
            );
        }

        public static int CompareTypesByFullName(Type lhs, Type rhs)
        {
            return string.Compare(
                lhs?.FullName ?? string.Empty,
                rhs?.FullName ?? string.Empty,
                StringComparison.Ordinal
            );
        }

        public static int CompareTypesByNameThenFullName(Type lhs, Type rhs)
        {
            int nameComparison = string.Compare(
                lhs?.Name ?? string.Empty,
                rhs?.Name ?? string.Empty,
                StringComparison.Ordinal
            );
            return nameComparison != 0 ? nameComparison : CompareTypesByFullName(lhs, rhs);
        }

        public static int CompareTypeFullNames(
            string lhsFullName,
            string rhsFullName,
            IReadOnlyList<string> typeFullNameOrder
        )
        {
            int indexA = IndexOf(typeFullNameOrder, lhsFullName);
            int indexB = IndexOf(typeFullNameOrder, rhsFullName);

            switch (indexA)
            {
                case >= 0 when indexB >= 0:
                    return indexA.CompareTo(indexB);
                case >= 0:
                    return -1;
            }

            return 0 <= indexB
                ? 1
                : string.Compare(lhsFullName, rhsFullName, StringComparison.Ordinal);
        }

        public static Type FindTypeByFullName(IEnumerable<Type> types, string typeFullName)
        {
            if (types == null || string.IsNullOrWhiteSpace(typeFullName))
            {
                return null;
            }

            return types.FirstOrDefault(type =>
                string.Equals(type?.FullName, typeFullName, StringComparison.Ordinal)
            );
        }

        public NamespaceTypeOrder Clone()
        {
            return new NamespaceTypeOrder
            {
                namespaceKey = namespaceKey ?? string.Empty,
                typeNames = typeNames?.ToList() ?? new List<string>(),
            };
        }

        private static int IndexOf(IReadOnlyList<string> values, string value)
        {
            if (values == null || string.IsNullOrWhiteSpace(value))
            {
                return -1;
            }

            for (int i = 0; i < values.Count; ++i)
            {
                if (string.Equals(values[i], value, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
