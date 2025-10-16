namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DataVisualizer;
    using WallstopStudios.DataVisualizer.Editor.Events;

    internal sealed class ProcessorExecutionService : IDisposable
    {
        private readonly ScriptableAssetSaveScheduler _saveScheduler;
        private readonly DataVisualizerEventHub _eventHub;
        private readonly Queue<ExecutionRequest> _pendingExecutions = new();
        private ExecutionRequest _activeRequest;
        private double _activeStartTime;
        private bool _updateHookRegistered;

        private sealed class ExecutionRequest
        {
            public ExecutionRequest(
                IDataProcessor processor,
                Type targetType,
                IReadOnlyList<ScriptableObject> objects
            )
            {
                Processor = processor;
                TargetType = targetType;
                Objects = objects ?? Array.Empty<ScriptableObject>();
            }

            public IDataProcessor Processor { get; }

            public Type TargetType { get; }

            public IReadOnlyList<ScriptableObject> Objects { get; }
        }

        public ProcessorExecutionService(
            ScriptableAssetSaveScheduler saveScheduler,
            DataVisualizerEventHub eventHub
        )
        {
            _saveScheduler = saveScheduler
                ?? throw new ArgumentNullException(nameof(saveScheduler));
            _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
        }

        public void Dispose()
        {
            if (_updateHookRegistered)
            {
                EditorApplication.update -= HandleEditorUpdate;
                _updateHookRegistered = false;
            }

            _pendingExecutions.Clear();
            _activeRequest = null;
        }

        public void EnqueueExecution(
            IDataProcessor processor,
            Type targetType,
            IReadOnlyList<ScriptableObject> objects
        )
        {
            if (processor == null || targetType == null || objects == null)
            {
                return;
            }

            _pendingExecutions.Enqueue(new ExecutionRequest(processor, targetType, objects));
            if (_activeRequest == null)
            {
                BeginNextExecution();
            }
        }

        private void BeginNextExecution()
        {
            if (_pendingExecutions.Count == 0)
            {
                _activeRequest = null;
                return;
            }

            _activeRequest = _pendingExecutions.Dequeue();
            _activeStartTime = EditorApplication.timeSinceStartup;
            _eventHub.Publish(
                new ProcessorExecutionStartedEvent(
                    _activeRequest.Processor,
                    _activeRequest.TargetType,
                    _activeRequest.Objects.Count,
                    _pendingExecutions.Count
                )
            );

            RegisterExecutionUpdate();
        }

        private void RegisterExecutionUpdate()
        {
            if (_updateHookRegistered)
            {
                return;
            }

            EditorApplication.update += HandleEditorUpdate;
            _updateHookRegistered = true;
        }

        private void HandleEditorUpdate()
        {
            if (!_updateHookRegistered)
            {
                return;
            }

            EditorApplication.update -= HandleEditorUpdate;
            _updateHookRegistered = false;

            ExecuteActiveRequest();
        }

        private void ExecuteActiveRequest()
        {
            ExecutionRequest request = _activeRequest;
            if (request == null)
            {
                BeginNextExecution();
                return;
            }

            try
            {
                request.Processor.Process(request.TargetType, request.Objects);
                double durationSeconds = Math.Max(
                    0d,
                    EditorApplication.timeSinceStartup - _activeStartTime
                );
                _saveScheduler.ScheduleAssetDatabaseSave();
                _saveScheduler.Schedule(AssetDatabase.Refresh);

                _eventHub.Publish(
                    new ProcessorExecutionCompletedEvent(
                        request.Processor,
                        request.TargetType,
                        request.Objects.Count,
                        durationSeconds,
                        _pendingExecutions.Count
                    )
                );
            }
            catch (Exception exception)
            {
                _eventHub.Publish(
                    new ProcessorExecutionFailedEvent(
                        request.Processor,
                        request.TargetType,
                        request.Objects.Count,
                        _pendingExecutions.Count,
                        exception
                    )
                );
            }
            finally
            {
                _activeRequest = null;
                BeginNextExecution();
            }
        }
    }
}
