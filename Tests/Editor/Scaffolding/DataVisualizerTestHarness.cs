namespace WallstopStudios.DataVisualizer.Editor.Tests.Scaffolding
{
    using System;
    using System.Collections.Generic;
    using Controllers;
    using Data;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    internal sealed class DataVisualizerTestHarness : IDisposable
    {
        private readonly bool _settingsAssetExistedBefore;
        private readonly string _settingsAssetPath = DataVisualizer.SettingsDefaultPath;

        public DataVisualizerTestHarness()
        {
            Window = ScriptableObject.CreateInstance<DataVisualizer>();
            Window.hideFlags = HideFlags.HideAndDontSave;
            _settingsAssetExistedBefore =
                AssetDatabase.LoadAssetAtPath<DataVisualizerSettings>(_settingsAssetPath) != null;
        }

        public DataVisualizer Window { get; }

        public void ConfigureNamespace(string namespaceKey, params Type[] types)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                throw new ArgumentException(
                    "Namespace key cannot be null or whitespace.",
                    nameof(namespaceKey)
                );
            }

            if (types == null)
            {
                throw new ArgumentNullException(nameof(types));
            }

            Window._scriptableObjectTypes[namespaceKey] = new List<Type>(types);
            Window._namespaceOrder[namespaceKey] = Window._namespaceOrder.Count;
        }

        public void InitializeWindow()
        {
            Window.OnEnable();
            Window.CreateGUI();
        }

        public void Dispose()
        {
            try
            {
                Window?.OnDisable();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Assert.Fail("Window.OnDisable threw an exception: " + exception.Message);
            }

            if (Window != null)
            {
                Object.DestroyImmediate(Window);
            }

            if (!_settingsAssetExistedBefore)
            {
                if (AssetDatabase.DeleteAsset(_settingsAssetPath))
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
            }
        }
    }
}
