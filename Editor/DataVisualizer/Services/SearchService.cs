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
        private bool _enableFuzzyMatching = true;
        private float _fuzzyMatchThreshold = 0.6f;

        public SearchService(IDataAssetService assetService)
        {
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
        }

        public bool EnableFuzzyMatching
        {
            get { return _enableFuzzyMatching; }
            set { _enableFuzzyMatching = value; }
        }

        public float FuzzyMatchThreshold
        {
            get { return _fuzzyMatchThreshold; }
            set { _fuzzyMatchThreshold = Mathf.Clamp01(value); }
        }

        public IReadOnlyList<string> AllManagedGuids
        {
            get { return _managedGuids; }
        }

        public void PopulateSearchCache(IEnumerable<Type> managedTypes, bool forceRebuild)
        {
            if (_assetService == null)
            {
                return;
            }

            List<Type> typeList =
                managedTypes != null
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

            results.Sort(
                (lhs, rhs) =>
                {
                    int scoreCompare = rhs.MatchInfo.highestScore.CompareTo(
                        lhs.MatchInfo.highestScore
                    );
                    if (scoreCompare != 0)
                    {
                        return scoreCompare;
                    }

                    string leftName = lhs.Metadata?.DisplayName ?? string.Empty;
                    string rightName = rhs.Metadata?.DisplayName ?? string.Empty;
                    return string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
                }
            );

            return results;
        }

        private SearchResultMatchInfo CheckMatch(DataAssetMetadata metadata, string[] searchTerms)
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
            float highestScore = 0f;

            for (int termIndex = 0; termIndex < searchTerms.Length; termIndex++)
            {
                string term = searchTerms[termIndex];
                if (string.IsNullOrWhiteSpace(term))
                {
                    continue;
                }

                bool termMatched = false;
                List<MatchDetail> termDetails = new List<MatchDetail>();

                termMatched |= TryAddContainsMatch(
                    objectName,
                    term,
                    MatchSource.ObjectName,
                    termDetails,
                    ref highestScore
                );

                termMatched |= TryAddContainsMatch(
                    typeName,
                    term,
                    MatchSource.TypeName,
                    termDetails,
                    ref highestScore
                );

                termMatched |= TryAddContainsMatch(
                    guid,
                    term,
                    MatchSource.Guid,
                    termDetails,
                    ref highestScore
                );

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
                            reflectedMatch.score = 1f;
                            termDetails.Add(reflectedMatch);
                            termMatched = true;
                            highestScore = Mathf.Max(highestScore, reflectedMatch.score);
                        }
                    }
                }

                if (!termMatched && _enableFuzzyMatching)
                {
                    termMatched |= TryAddFuzzyMatch(
                        objectName,
                        term,
                        MatchSource.ObjectName,
                        termDetails,
                        ref highestScore
                    );

                    termMatched |= TryAddFuzzyMatch(
                        typeName,
                        term,
                        MatchSource.TypeName,
                        termDetails,
                        ref highestScore
                    );
                }

                if (termMatched)
                {
                    matchInfo.isMatch = true;
                    matchInfo.matchedFields.AddRange(termDetails);
                }
            }

            matchInfo.highestScore = highestScore;
            return matchInfo;
        }

        private bool TryAddContainsMatch(
            string source,
            string term,
            string fieldName,
            List<MatchDetail> details,
            ref float highestScore
        )
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrWhiteSpace(term))
            {
                return false;
            }

            if (!source.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            MatchDetail detail = new MatchDetail(term)
            {
                fieldName = fieldName,
                matchedValue = source,
                score = 1f,
            };
            details.Add(detail);
            highestScore = Mathf.Max(highestScore, detail.score);
            return true;
        }

        private bool TryAddFuzzyMatch(
            string source,
            string term,
            string fieldName,
            List<MatchDetail> details,
            ref float highestScore
        )
        {
            if (!_enableFuzzyMatching)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(term))
            {
                return false;
            }

            float score = CalculateFuzzyScore(source, term);
            if (score < _fuzzyMatchThreshold)
            {
                return false;
            }

            MatchDetail detail = new MatchDetail(term)
            {
                fieldName = fieldName,
                matchedValue = source,
                score = score,
            };
            details.Add(detail);
            highestScore = Mathf.Max(highestScore, score);
            return true;
        }

        private static float CalculateFuzzyScore(string source, string term)
        {
            if (string.IsNullOrEmpty(source) && string.IsNullOrEmpty(term))
            {
                return 1f;
            }

            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(term))
            {
                return 0f;
            }

            string lowerSource = source.ToLowerInvariant();
            string lowerTerm = term.ToLowerInvariant();

            int distance = ComputeLevenshteinDistance(lowerSource, lowerTerm);
            int maxLength = Mathf.Max(lowerSource.Length, lowerTerm.Length);
            if (maxLength == 0)
            {
                return 1f;
            }

            return 1f - distance / (float)maxLength;
        }

        private static int ComputeLevenshteinDistance(string source, string target)
        {
            int sourceLength = source.Length;
            int targetLength = target.Length;

            if (sourceLength == 0)
            {
                return targetLength;
            }

            if (targetLength == 0)
            {
                return sourceLength;
            }

            int[] previous = new int[targetLength + 1];
            int[] current = new int[targetLength + 1];

            for (int index = 0; index <= targetLength; index++)
            {
                previous[index] = index;
            }

            for (int sourceIndex = 1; sourceIndex <= sourceLength; sourceIndex++)
            {
                current[0] = sourceIndex;
                for (int targetIndex = 1; targetIndex <= targetLength; targetIndex++)
                {
                    int cost = source[sourceIndex - 1] == target[targetIndex - 1] ? 0 : 1;

                    current[targetIndex] = Math.Min(
                        Math.Min(current[targetIndex - 1] + 1, previous[targetIndex] + 1),
                        previous[targetIndex - 1] + cost
                    );
                }

                int[] temp = previous;
                previous = current;
                current = temp;
            }

            return previous[targetLength];
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
                                score = 1f,
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
