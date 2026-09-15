namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;

    public static class AssetGuidDiscovery
    {
        /*
            Replaces the default busy check when set (deterministic tests and tooling seams; the
            editor test assembly cannot access editor-assembly internals). Null keeps the default:
            EditorApplication.isCompiling || EditorApplication.isUpdating.
        */
        public static Func<bool> AssetDatabaseBusyOverride { get; set; }

        public static bool IsAssetDatabaseBusy()
        {
            Func<bool> overridePredicate = AssetDatabaseBusyOverride;
            if (overridePredicate != null)
            {
                return overridePredicate();
            }

            return EditorApplication.isCompiling || EditorApplication.isUpdating;
        }

        /*
            Merges the candidate GUID sets for one managed type: the caller's discovery results
            (FindAssets by short type name), the type index's known GUIDs, the caller's referenced
            order GUIDs, and the saved selection GUID.

            While the AssetDatabase is busy (deferNormalization), type identity cannot be verified
            without forcing Unity's type resolution, which logs a user-facing missing-script
            warning per unresolved asset right after a script recompile (recorded on issue #36).
            Deferral keeps referenced and saved order GUIDs as raw unverified candidates and
            returns the raw saved GUID instead of null, so callers keep the saved selection and
            re-normalize after the database settles instead of discarding a possibly still-valid
            selection inside the transient window.
        */
        public static string[] MergeCandidates(
            Type type,
            string[] discoveredGuids,
            IReadOnlyList<string> referencedGuids,
            string savedObjectGuid,
            out string normalizedSavedObjectGuid,
            AssetGuidTypeIndex typeIndex = null,
            bool deferNormalization = false
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
                if (
                    0 < AddResolvedGuids(type, referencedGuids, candidateLookup, deferNormalization)
                )
                {
                    candidateAdded = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(savedObjectGuid))
            {
                if (deferNormalization)
                {
                    /*
                        The saved GUID cannot be verified while the database is busy, so hand it
                        back unchanged (unverified) instead of null: null means "discard", which
                        would clear a selection that may still be valid once the database settles.
                    */
                    normalizedSavedGuid = savedObjectGuid;
                    candidateLookup ??= new HashSet<string>(
                        candidates,
                        StringComparer.OrdinalIgnoreCase
                    );
                    if (candidateLookup.Add(normalizedSavedGuid))
                    {
                        candidateAdded = true;
                    }
                }
                else if (TryNormalizeGuidForType(type, savedObjectGuid, out string normalizedGuid))
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
            ISet<string> destination,
            bool deferNormalization = false
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

                bool added;
                if (deferNormalization)
                {
                    /*
                        Unverified while the database is busy: keep the raw GUID as a candidate
                        instead of dropping it (see MergeCandidates).
                    */
                    added = destination.Add(referencedGuid);
                }
                else
                {
                    added =
                        TryNormalizeGuidForType(type, referencedGuid, out string normalizedGuid)
                        && destination.Add(normalizedGuid);
                }

                if (added)
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

        /*
            Forces Unity's type resolution for the GUID's main asset. Unity logs its built-in
            "The referenced script on this Behaviour (Game Object '<null>') is missing!" warning
            for every asset whose script GUID is transiently unresolvable (the window right after
            a script recompile), so callers must pass deferNormalization while
            IsAssetDatabaseBusy() instead of invoking this path during that window (issue #36).
        */
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
