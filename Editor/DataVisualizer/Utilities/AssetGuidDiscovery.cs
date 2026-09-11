namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;

    public static class AssetGuidDiscovery
    {
        public static string[] MergeCandidates(
            Type type,
            string[] discoveredGuids,
            IReadOnlyList<string> referencedGuids,
            string savedObjectGuid,
            out string normalizedSavedObjectGuid,
            AssetGuidTypeIndex typeIndex = null
        )
        {
            string[] candidates = discoveredGuids ?? Array.Empty<string>();
            HashSet<string> candidateLookup = null;
            bool candidateAdded = false;
            string normalizedSavedGuid = null;

            AssetGuidTypeIndex effectiveTypeIndex = typeIndex ?? AssetGuidTypeIndex.Shared;
            string[] indexedGuids = effectiveTypeIndex.GetKnownGuids(type);
            if (0 < indexedGuids.Length)
            {
                candidateLookup = new HashSet<string>(candidates, StringComparer.OrdinalIgnoreCase);
                foreach (string indexedGuid in indexedGuids)
                {
                    if (candidateLookup.Add(indexedGuid))
                    {
                        candidateAdded = true;
                    }
                }
            }

            if (referencedGuids != null && 0 < referencedGuids.Count)
            {
                candidateLookup ??= new HashSet<string>(
                    candidates,
                    StringComparer.OrdinalIgnoreCase
                );
                if (0 < AddResolvedGuids(type, referencedGuids, candidateLookup))
                {
                    candidateAdded = true;
                }
            }

            if (TryNormalizeGuidForType(type, savedObjectGuid, out string normalizedGuid))
            {
                normalizedSavedGuid = normalizedGuid;
                candidateLookup ??= new HashSet<string>(
                    candidates,
                    StringComparer.OrdinalIgnoreCase
                );
                if (candidateLookup.Add(normalizedSavedGuid))
                {
                    candidateAdded = true;
                }
            }

            if (!candidateAdded)
            {
                normalizedSavedObjectGuid = normalizedSavedGuid;
                return candidates;
            }

            string[] mergedCandidates = new string[candidateLookup.Count];
            candidateLookup.CopyTo(mergedCandidates);
            normalizedSavedObjectGuid = normalizedSavedGuid;
            return mergedCandidates;
        }

        public static int AddResolvedGuids(
            Type type,
            IEnumerable<string> referencedGuids,
            ISet<string> destination
        )
        {
            if (type == null || referencedGuids == null || destination == null)
            {
                return 0;
            }

            int addedCount = 0;
            foreach (string referencedGuid in referencedGuids)
            {
                if (
                    string.IsNullOrWhiteSpace(referencedGuid)
                    || destination.Contains(referencedGuid)
                )
                {
                    continue;
                }

                if (
                    TryNormalizeGuidForType(type, referencedGuid, out string normalizedGuid)
                    && destination.Add(normalizedGuid)
                )
                {
                    addedCount++;
                }
            }

            return addedCount;
        }

        public static bool TryNormalizeGuidForType(
            Type type,
            string assetGuid,
            out string normalizedGuid
        )
        {
            if (
                type == null
                || string.IsNullOrWhiteSpace(assetGuid)
                || !TryResolveAssetGuidForType(assetGuid, type, out string assetPath)
            )
            {
                normalizedGuid = null;
                return false;
            }

            string canonicalGuid = AssetDatabase.AssetPathToGUID(assetPath);
            normalizedGuid = string.IsNullOrWhiteSpace(canonicalGuid) ? assetGuid : canonicalGuid;
            return true;
        }

        public static bool TryResolveAssetGuidForType(
            string assetGuid,
            Type type,
            out string assetPath
        )
        {
            if (string.IsNullOrWhiteSpace(assetGuid) || type == null)
            {
                assetPath = null;
                return false;
            }

            string resolvedAssetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
            if (
                string.IsNullOrWhiteSpace(resolvedAssetPath)
                || AssetDatabase.GetMainAssetTypeAtPath(resolvedAssetPath) != type
            )
            {
                assetPath = null;
                return false;
            }

            assetPath = resolvedAssetPath;
            return true;
        }
    }
#endif
}
