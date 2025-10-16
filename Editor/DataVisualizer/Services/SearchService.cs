namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Data;
    using Helper.Pooling;
    using Search;
    using UnityEngine;

    internal sealed class SearchService
    {
        internal sealed class SearchMatch
        {
            public SearchMatch(DataAssetMetadata metadata, SearchResultMatchInfo matchInfo)
            {
                Metadata = metadata;
                MatchInfo = matchInfo;
            }

            public DataAssetMetadata Metadata { get; }

            public SearchResultMatchInfo MatchInfo { get; }
        }

        private readonly IDataAssetService _assetService;
        private readonly List<string> _managedGuids = new List<string>();

        public SearchService(IDataAssetService assetService)
        {
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
        }

        public IReadOnlyList<string> AllManagedGuids
        {
            get
            {
                return _managedGuids;
            }
        }

        public void PopulateSearchCache(IEnumerable<Type> managedTypes, bool forceRebuild)
        {
            if (_assetService == null)
            {
                return;
            }

            List<Type> typeList = managedTypes != null
                ? managedTypes.Where(type => type != null).Distinct().ToList()
                : new List<Type>();

            _assetService.ConfigureTrackedTypes(typeList);
            if (forceRebuild)
            {
                _assetService.ForceRebuild();
            }

            _managedGuids.Clear();
            using PooledResource<List<DataAssetMetadata>> metadataBuffer =
                Buffers<DataAssetMetadata>.GetList(0, out List<DataAssetMetadata> metadata);
            metadata.AddRange(_assetService.GetAllAssets() ?? Array.Empty<DataAssetMetadata>());
            metadata.Sort(
                (lhs, rhs) =>
                {
                    int nameComparison = string.Compare(
                        lhs.DisplayName,
                        rhs.DisplayName,
                        StringComparison.Ordinal
                    );
                    if (nameComparison != 0)
                    {
                        return nameComparison;
                    }

                    return string.Compare(
                        lhs.TypeFullName,
                        rhs.TypeFullName,
                        StringComparison.Ordinal
                    );
                }
            );

            for (int metadataIndex = 0; metadataIndex < metadata.Count; metadataIndex++)
            {
                DataAssetMetadata entry = metadata[metadataIndex];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Guid))
                {
                    continue;
                }

                _managedGuids.Add(entry.Guid);
            }
        }

        public List<SearchMatch> Search(string[] searchTerms, int maxResults)
        {
            List<SearchMatch> results = new List<SearchMatch>();
            if (_assetService == null || searchTerms == null || searchTerms.Length == 0)
            {
                return results;
            }

            for (int guidIndex = 0; guidIndex < _managedGuids.Count; guidIndex++)
            {
                string guid = _managedGuids[guidIndex];
                if (string.IsNullOrWhiteSpace(guid))
                {
                    continue;
                }

                if (!_assetService.TryGetAssetByGuid(guid, out DataAssetMetadata metadata))
                {
                    continue;
                }

                SearchResultMatchInfo match = CheckMatch(metadata, searchTerms);
                if (!match.isMatch)
                {
                    continue;
                }

                results.Add(new SearchMatch(metadata, match));
                if (0 < maxResults && results.Count >= maxResults)
                {
                    break;
                }
            }

            return results;
        }

        private static SearchResultMatchInfo CheckMatch(
            DataAssetMetadata metadata,
            string[] searchTerms
        )
        {
            SearchResultMatchInfo matchInfo = new SearchResultMatchInfo();
            if (metadata == null || searchTerms == null || searchTerms.Length == 0)
            {
                return matchInfo;
            }

            string objectName = metadata.DisplayName ?? string.Empty;
            string typeName = metadata.AssetType?.Name ?? string.Empty;
            string guid = metadata.Guid ?? string.Empty;
            ScriptableObject loadedAsset = null;

            for (int termIndex = 0; termIndex < searchTerms.Length; termIndex++)
            {
                string term = searchTerms[termIndex];
                if (string.IsNullOrWhiteSpace(term))
                {
                    continue;
                }

                bool termMatched = false;
                List<MatchDetail> termDetails = new List<MatchDetail>();

                if (
                    !string.IsNullOrEmpty(objectName)
                    && objectName.Contains(term, StringComparison.OrdinalIgnoreCase)
                )
                {
                    MatchDetail detail = new MatchDetail(term)
                    {
                        fieldName = MatchSource.ObjectName,
                        matchedValue = objectName,
                    };
                    termDetails.Add(detail);
                    termMatched = true;
                }

                if (
                    !string.IsNullOrEmpty(typeName)
                    && typeName.Contains(term, StringComparison.OrdinalIgnoreCase)
                )
                {
                    MatchDetail detail = new MatchDetail(term)
                    {
                        fieldName = MatchSource.TypeName,
                        matchedValue = typeName,
                    };
                    termDetails.Add(detail);
                    termMatched = true;
                }

                if (
                    !string.IsNullOrWhiteSpace(guid)
                    && guid.Equals(term, StringComparison.OrdinalIgnoreCase)
                )
                {
                    MatchDetail detail = new MatchDetail(term)
                    {
                        fieldName = MatchSource.Guid,
                        matchedValue = guid,
                    };
                    termDetails.Add(detail);
                    termMatched = true;
                }

                if (!termMatched)
                {
                    loadedAsset ??= metadata.LoadAsset();
                    if (loadedAsset != null)
                    {
                        MatchDetail reflectedMatch = SearchStringProperties(
                            loadedAsset,
                            term,
                            0,
                            2,
                            new HashSet<object>()
                        );
                        if (reflectedMatch != null)
                        {
                            reflectedMatch.matchedTerms.Add(term);
                            termDetails.Add(reflectedMatch);
                            termMatched = true;
                        }
                    }
                }

                if (termMatched)
                {
                    matchInfo.isMatch = true;
                    matchInfo.matchedFields.AddRange(termDetails);
                }
            }

            return matchInfo;
        }

        private static MatchDetail SearchStringProperties(
            object obj,
            string searchTerm,
            int currentDepth,
            int maxDepth,
            HashSet<object> visited
        )
        {
            if (obj == null || currentDepth > maxDepth)
            {
                return null;
            }

            Type objType = obj.GetType();

            if (
                objType.IsPrimitive
                || objType == typeof(Vector2)
                || objType == typeof(Vector3)
                || objType == typeof(Vector4)
                || objType == typeof(Quaternion)
                || objType == typeof(Color)
                || objType == typeof(Rect)
                || objType == typeof(Bounds)
            )
            {
                return null;
            }

            if (!objType.IsValueType && !visited.Add(obj))
            {
                return null;
            }

            try
            {
                FieldInfo[] fields = objType.GetFields(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic
                );
                for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
                {
                    FieldInfo field = fields[fieldIndex];
                    object fieldValue = field.GetValue(obj);
                    if (fieldValue == null)
                    {
                        continue;
                    }

                    if (field.FieldType == typeof(string))
                    {
                        string stringValue = fieldValue as string;
                        if (
                            !string.IsNullOrWhiteSpace(stringValue)
                            && stringValue.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            MatchDetail detail = new MatchDetail(searchTerm)
                            {
                                fieldName = field.Name,
                                matchedValue = stringValue,
                            };
                            return detail;
                        }
                    }
                    else if (
                        (
                            field.FieldType.IsClass
                            || field.FieldType is { IsValueType: true, IsPrimitive: false }
                        ) && !typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)
                    )
                    {
                        MatchDetail nestedMatch = SearchStringProperties(
                            fieldValue,
                            searchTerm,
                            currentDepth + 1,
                            maxDepth,
                            visited
                        );
                        if (nestedMatch != null)
                        {
                            return nestedMatch;
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }

            return null;
        }
    }
}
