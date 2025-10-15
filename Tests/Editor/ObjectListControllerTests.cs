#pragma warning disable CS0618 // Type or member is obsolete
namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using System;
    using System.Collections.Generic;
    using Controllers;
    using Data;
    using NUnit.Framework;
    using Services;
    using State;
    using UnityEngine;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class ObjectListControllerTests
    {
        private sealed class DummyScriptableObject : ScriptableObject { }

        private sealed class StubUserStateRepository : IUserStateRepository
        {
            public DataVisualizerSettings Settings { get; set; }

            public DataVisualizerUserState UserState { get; set; }

            public DataVisualizerSettings LoadSettings()
            {
                return Settings;
            }

            public DataVisualizerUserState LoadUserState()
            {
                return UserState;
            }

            public void SaveSettings(DataVisualizerSettings settings) { }

            public void SaveUserState(DataVisualizerUserState userState) { }

        }

        [Test]
        public void HandleNextPageRequestedAdvancesPageWhenPossible()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            DataVisualizerSettings createdSettings = null;
            List<ScriptableObject> created = new List<ScriptableObject>();
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;
                created = PrepareDataVisualizerWithPaginationContext(
                    dataVisualizer,
                    out Type selectedType
                );
                createdSettings = dataVisualizer._settings;

                dataVisualizer.SetCurrentPage(selectedType, 0);

                dataVisualizer._objectListController.HandleNextPageRequested();

                Assert.AreEqual(1, dataVisualizer.GetCurrentPage(selectedType));
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
                if (createdSettings != null)
                {
                    Object.DestroyImmediate(createdSettings);
                }
                foreach (ScriptableObject createdObject in created)
                {
                    if (createdObject != null)
                    {
                        Object.DestroyImmediate(createdObject);
                    }
                }
            }
        }

        [Test]
        public void HandleCurrentPageChangedClampsRequestedValue()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            DataVisualizerSettings createdSettings = null;
            List<ScriptableObject> created = new List<ScriptableObject>();
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;
                created = PrepareDataVisualizerWithPaginationContext(
                    dataVisualizer,
                    out Type selectedType
                );
                createdSettings = dataVisualizer._settings;

                dataVisualizer.SetCurrentPage(selectedType, 0);

                dataVisualizer._objectListController.HandleCurrentPageChanged(5);

                Assert.AreEqual(2, dataVisualizer.GetCurrentPage(selectedType));
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
                if (createdSettings != null)
                {
                    Object.DestroyImmediate(createdSettings);
                }
                foreach (ScriptableObject createdObject in created)
                {
                    if (createdObject != null)
                    {
                        Object.DestroyImmediate(createdObject);
                    }
                }
            }
        }

        private static List<ScriptableObject> PrepareDataVisualizerWithPaginationContext(
            DataVisualizer dataVisualizer,
            out Type selectedType
        )
        {
            DataVisualizerSettings settings =
                ScriptableObject.CreateInstance<DataVisualizerSettings>();
            settings.persistStateInSettingsAsset = true;
            DataVisualizerUserState userState = new DataVisualizerUserState();

            StubUserStateRepository repository = new StubUserStateRepository
            {
                Settings = settings,
                UserState = userState,
            };

            dataVisualizer._settings = settings;
            dataVisualizer._userState = userState;
            dataVisualizer._userStateRepository = repository;

            selectedType = typeof(DummyScriptableObject);
            dataVisualizer._scriptableObjectTypes.Clear();
            dataVisualizer._scriptableObjectTypes["TestNamespace"] = new List<Type>
            {
                selectedType,
            };
            dataVisualizer._namespaceOrder.Clear();
            dataVisualizer._namespaceOrder["TestNamespace"] = 0;
            dataVisualizer._namespaceController._selectedType = selectedType;

            dataVisualizer._filteredObjects.Clear();
            dataVisualizer._selectedObjects.Clear();
            List<ScriptableObject> created = new List<ScriptableObject>();
            for (int index = 0; index < 250; index++)
            {
                DummyScriptableObject obj =
                    ScriptableObject.CreateInstance<DummyScriptableObject>();
                dataVisualizer._filteredObjects.Add(obj);
                dataVisualizer._selectedObjects.Add(obj);
                created.Add(obj);
            }

            dataVisualizer._objectListView = null;
            return created;
        }
    }
}
#pragma warning restore CS0618 // Type or member is obsolete
