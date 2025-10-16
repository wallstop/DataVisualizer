namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    internal sealed class LabelSuggestionProvider
    {
        private readonly IDataAssetService _assetService;
        private readonly Dictionary<Type, List<string>> _cache =
            new Dictionary<Type, List<string>>();

        public LabelSuggestionProvider(IDataAssetService assetService)
        {
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
        }

        public IReadOnlyList<string> GetSuggestions(
            Type type,
            string prefix,
            IReadOnlyCollection<string> excludedLabels
        )
        {
            if (type == null)
            {
                return Array.Empty<string>();
            }

            List<string> pool = GetOrBuildCache(type);
            IEnumerable<string> query = pool;

            if (!string.IsNullOrWhiteSpace(prefix))
            {
                string normalized = prefix.Trim();
                query = query.Where(label =>
                    label.IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0
                );
            }

            if (excludedLabels != null && excludedLabels.Count > 0)
            {
                HashSet<string> excluded = new HashSet<string>(
                    excludedLabels,
                    StringComparer.OrdinalIgnoreCase
                );
                query = query.Where(label => !excluded.Contains(label));
            }

            return query.Take(10).ToList();
        }

        public void Invalidate(Type type)
        {
            if (type == null)
            {
                return;
            }

            _cache.Remove(type);
        }

        public void InvalidateAll()
        {
            _cache.Clear();
        }

        private List<string> GetOrBuildCache(Type type)
        {
            if (_cache.TryGetValue(type, out List<string> existing))
            {
                return existing;
            }

            IReadOnlyCollection<string> labels = _assetService.EnumerateLabels(type);
            List<string> normalized =
                labels
                    ?.Where(label => !string.IsNullOrWhiteSpace(label))
                    .Select(label => label.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>();

            _cache[type] = normalized;
            return normalized;
        }
    }
}
