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
            out string normalizedSavedObjectGuid
        )
        {
            string[] candidates = discoveredGuids ?? Array.Empty<string>();
            HashSet<string> candidateLookup = null;
            bool candidateAdded = false;

            if (referencedGuids != null && referencedGuids.Count > 0)
            {
                candidateLookup = new HashSet<string>(candidates, StringComparer.OrdinalIgnoreCase);
                int initialCount = candidateLookup.Count;
                AddResolvedGuids(type, referencedGuids, candidateLookup);
                candidateAdded = candidateLookup.Count > initialCount;
            }

            normalizedSavedObjectGuid = NormalizeGuidForType(type, savedObjectGuid);
            if (normalizedSavedObjectGuid != null)
            {
                candidateLookup ??= new HashSet<string>(
                    candidates,
                    StringComparer.OrdinalIgnoreCase
                );
                candidateAdded |= candidateLookup.Add(normalizedSavedObjectGuid);
            }

            if (!candidateAdded)
            {
                return candidates;
            }

            string[] mergedCandidates = new string[candidateLookup.Count];
            candidateLookup.CopyTo(mergedCandidates);
            return mergedCandidates;
        }

        public static void AddResolvedGuids(
            Type type,
            IEnumerable<string> referencedGuids,
            ISet<string> destination
        )
        {
            if (type == null || referencedGuids == null || destination == null)
            {
                return;
            }

            foreach (string referencedGuid in referencedGuids)
            {
                if (
                    string.IsNullOrWhiteSpace(referencedGuid)
                    || destination.Contains(referencedGuid)
                )
                {
                    continue;
                }

                string normalizedGuid = NormalizeGuidForType(type, referencedGuid);
                if (normalizedGuid != null)
                {
                    destination.Add(normalizedGuid);
                }
            }
        }

        public static string NormalizeGuidForType(Type type, string assetGuid)
        {
            if (
                type == null
                || string.IsNullOrWhiteSpace(assetGuid)
                || !TryResolveAssetGuidForType(assetGuid, type, out string assetPath)
            )
            {
                return null;
            }

            string canonicalGuid = AssetDatabase.AssetPathToGUID(assetPath);
            return string.IsNullOrWhiteSpace(canonicalGuid) ? assetGuid : canonicalGuid;
        }

        public static bool TryResolveAssetGuidForType(
            string assetGuid,
            Type type,
            out string assetPath
        )
        {
            assetPath = null;
            if (string.IsNullOrWhiteSpace(assetGuid) || type == null)
            {
                return false;
            }

            assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
            if (
                string.IsNullOrWhiteSpace(assetPath)
                || AssetDatabase.GetMainAssetTypeAtPath(assetPath) != type
            )
            {
                assetPath = null;
                return false;
            }

            return true;
        }
    }
#endif
}
