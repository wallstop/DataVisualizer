namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    internal sealed class ScriptableAssetSaveScheduler : IDisposable
    {
        private readonly double _flushDelaySeconds;
        private readonly List<Action> _pendingActions = new List<Action>();
        private bool _pendingAssetDatabaseSave;
        private bool _updateRegistered;
        private double _nextFlushTime;
        private double? _lastFlushTime;

        public ScriptableAssetSaveScheduler(double flushDelaySeconds = 0.35d)
        {
            _flushDelaySeconds = flushDelaySeconds <= 0d ? 0.1d : flushDelaySeconds;
        }

        public int PendingActionCount
        {
            get { return _pendingActions.Count; }
        }

        public bool IsAssetDatabaseSavePending
        {
            get { return _pendingAssetDatabaseSave; }
        }

        public double? LastFlushTime
        {
            get { return _lastFlushTime; }
        }

        public double? NextScheduledFlushTime
        {
            get { return _updateRegistered ? _nextFlushTime : null; }
        }

        public void ScheduleAssetDatabaseSave()
        {
            _pendingAssetDatabaseSave = true;
            EnsureUpdateRegistered();
        }

        public void Schedule(Action action)
        {
            if (action == null)
            {
                return;
            }

            _pendingActions.Add(action);
            EnsureUpdateRegistered();
        }

        public void Flush()
        {
            ExecutePendingActions();
            CleanupUpdateRegistration();
        }

        public void Dispose()
        {
            ExecutePendingActions();
            CleanupUpdateRegistration();
            _pendingActions.Clear();
        }

        private void EnsureUpdateRegistered()
        {
            _nextFlushTime = EditorApplication.timeSinceStartup + _flushDelaySeconds;
            if (_updateRegistered)
            {
                return;
            }

            EditorApplication.update += HandleEditorUpdate;
            _updateRegistered = true;
        }

        private void HandleEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < _nextFlushTime)
            {
                return;
            }

            ExecutePendingActions();
            CleanupUpdateRegistration();
        }

        private void ExecutePendingActions()
        {
            bool executedWork = false;

            if (_pendingActions.Count > 0)
            {
                List<Action> actions = new List<Action>(_pendingActions);
                _pendingActions.Clear();
                for (int index = 0; index < actions.Count; index++)
                {
                    Action action = actions[index];
                    if (action == null)
                    {
                        continue;
                    }

                    try
                    {
                        action.Invoke();
                        executedWork = true;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            $"Error executing scheduled persistence action: {exception}"
                        );
                    }
                }
            }

            if (_pendingAssetDatabaseSave)
            {
                try
                {
                    AssetDatabase.SaveAssets();
                }
                catch (Exception exception)
                {
                    Debug.LogError($"Error saving scriptable assets: {exception}");
                }

                _pendingAssetDatabaseSave = false;
                executedWork = true;
            }

            if (executedWork)
            {
                _lastFlushTime = EditorApplication.timeSinceStartup;
            }
        }

        private void CleanupUpdateRegistration()
        {
            if (_updateRegistered)
            {
                EditorApplication.update -= HandleEditorUpdate;
                _updateRegistered = false;
            }

            _nextFlushTime = 0d;
        }
    }
}
