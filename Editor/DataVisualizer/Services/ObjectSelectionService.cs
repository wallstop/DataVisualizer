namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.Collections.Generic;
    using State;
    using UnityEditor;
    using UnityEngine;

    internal sealed class ObjectSelectionService
    {
        private readonly VisualizerSessionState _sessionState;

        public ObjectSelectionService(VisualizerSessionState sessionState)
        {
            _sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
        }

        public void SynchronizeSelection(
            IEnumerable<ScriptableObject> selectedObjects,
            ScriptableObject primarySelection
        )
        {
            if (selectedObjects == null)
            {
                _sessionState.Selection.SetSelectedObjects(Array.Empty<string>());
                SyncPrimary(primarySelection);
                return;
            }

            List<string> guids = new List<string>();
            bool primaryFound = false;

            foreach (ScriptableObject candidate in selectedObjects)
            {
                string guid = ResolveGuid(candidate);
                if (string.IsNullOrWhiteSpace(guid))
                {
                    continue;
                }

                if (ReferenceEquals(candidate, primarySelection))
                {
                    primaryFound = true;
                }

                guids.Add(guid);
            }

            if (!primaryFound)
            {
                string primaryGuid = ResolveGuid(primarySelection);
                if (!string.IsNullOrWhiteSpace(primaryGuid))
                {
                    guids.Insert(0, primaryGuid);
                }
            }

            _sessionState.Selection.SetSelectedObjects(guids);
            SyncPrimary(primarySelection);
        }

        public string ResolveGuid(ScriptableObject dataObject)
        {
            if (dataObject == null)
            {
                return null;
            }

            string assetPath = AssetDatabase.GetAssetPath(dataObject);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(guid))
            {
                return null;
            }

            return guid;
        }

        private void SyncPrimary(ScriptableObject primarySelection)
        {
            string primaryGuid = ResolveGuid(primarySelection);
            _sessionState.Selection.SetPrimarySelectedObject(primaryGuid);
            string typeFullName =
                primarySelection != null ? primarySelection.GetType().FullName : null;
            _sessionState.Selection.SetSelectedType(typeFullName);
        }
    }
}
