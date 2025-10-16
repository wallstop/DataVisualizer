namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class LayoutPersistenceService : IDisposable
    {
        private readonly string outerWidthKey;
        private readonly string innerWidthKey;
        private readonly string initialSizeAppliedKey;

        private IVisualElementScheduledItem scheduledSaveTask;
        private Func<float> outerWidthProvider;
        private Func<float> innerWidthProvider;
        private float lastSavedOuterWidth = float.NaN;
        private float lastSavedInnerWidth = float.NaN;

        public LayoutPersistenceService(
            string outerWidthPreferenceKey,
            string innerWidthPreferenceKey,
            string initialSizeAppliedPreferenceKey
        )
        {
            if (string.IsNullOrWhiteSpace(outerWidthPreferenceKey))
            {
                throw new ArgumentException(
                    "Outer width preference key cannot be null or whitespace.",
                    nameof(outerWidthPreferenceKey)
                );
            }

            if (string.IsNullOrWhiteSpace(innerWidthPreferenceKey))
            {
                throw new ArgumentException(
                    "Inner width preference key cannot be null or whitespace.",
                    nameof(innerWidthPreferenceKey)
                );
            }

            if (string.IsNullOrWhiteSpace(initialSizeAppliedPreferenceKey))
            {
                throw new ArgumentException(
                    "Initial size preference key cannot be null or whitespace.",
                    nameof(initialSizeAppliedPreferenceKey)
                );
            }

            outerWidthKey = outerWidthPreferenceKey;
            innerWidthKey = innerWidthPreferenceKey;
            initialSizeAppliedKey = initialSizeAppliedPreferenceKey;
        }

        public float LoadOuterSplitWidth(float defaultWidth)
        {
            return EditorPrefs.GetFloat(outerWidthKey, defaultWidth);
        }

        public float LoadInnerSplitWidth(float defaultWidth)
        {
            return EditorPrefs.GetFloat(innerWidthKey, defaultWidth);
        }

        public bool IsInitialSizeApplied()
        {
            return EditorPrefs.GetBool(initialSizeAppliedKey, false);
        }

        public void MarkInitialSizeApplied()
        {
            EditorPrefs.SetBool(initialSizeAppliedKey, true);
        }

        public void StartTrackingSplitViewWidths(
            VisualElement schedulerOwner,
            Func<float> currentOuterWidthProvider,
            Func<float> currentInnerWidthProvider
        )
        {
            if (schedulerOwner == null)
            {
                throw new ArgumentNullException(nameof(schedulerOwner));
            }

            if (currentOuterWidthProvider == null)
            {
                throw new ArgumentNullException(nameof(currentOuterWidthProvider));
            }

            if (currentInnerWidthProvider == null)
            {
                throw new ArgumentNullException(nameof(currentInnerWidthProvider));
            }

            StopTrackingSplitViewWidths();

            outerWidthProvider = currentOuterWidthProvider;
            innerWidthProvider = currentInnerWidthProvider;

            float initialOuterWidth = outerWidthProvider();
            float initialInnerWidth = innerWidthProvider();

            if (IsValidWidth(initialOuterWidth))
            {
                lastSavedOuterWidth = initialOuterWidth;
            }
            else
            {
                lastSavedOuterWidth = float.NaN;
            }

            if (IsValidWidth(initialInnerWidth))
            {
                lastSavedInnerWidth = initialInnerWidth;
            }
            else
            {
                lastSavedInnerWidth = float.NaN;
            }

            scheduledSaveTask = schedulerOwner.schedule.Execute(SaveSplitViewWidths).Every(1000);
        }

        public void StopTrackingSplitViewWidths()
        {
            if (scheduledSaveTask != null)
            {
                scheduledSaveTask.Pause();
                scheduledSaveTask = null;
            }

            outerWidthProvider = null;
            innerWidthProvider = null;
            lastSavedOuterWidth = float.NaN;
            lastSavedInnerWidth = float.NaN;
        }

        public void Dispose()
        {
            StopTrackingSplitViewWidths();
        }

        private void SaveSplitViewWidths()
        {
            if (outerWidthProvider == null || innerWidthProvider == null)
            {
                return;
            }

            float currentOuterWidth = outerWidthProvider();
            float currentInnerWidth = innerWidthProvider();

            if (
                IsValidWidth(currentOuterWidth)
                && (
                    !IsValidWidth(lastSavedOuterWidth)
                    || !Mathf.Approximately(currentOuterWidth, lastSavedOuterWidth)
                )
            )
            {
                EditorPrefs.SetFloat(outerWidthKey, currentOuterWidth);
                lastSavedOuterWidth = currentOuterWidth;
            }

            if (
                IsValidWidth(currentInnerWidth)
                && (
                    !IsValidWidth(lastSavedInnerWidth)
                    || !Mathf.Approximately(currentInnerWidth, lastSavedInnerWidth)
                )
            )
            {
                EditorPrefs.SetFloat(innerWidthKey, currentInnerWidth);
                lastSavedInnerWidth = currentInnerWidth;
            }
        }

        private static bool IsValidWidth(float width)
        {
            return !float.IsNaN(width) && !float.IsInfinity(width);
        }
    }
}
