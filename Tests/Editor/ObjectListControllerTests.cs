#pragma warning disable CS0618 // Type or member is obsolete
namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Controllers;
    using Events;
    using Data;
    using NUnit.Framework;
    using Services;
    using State;
    using UnityEngine;
    using UnityEngine.UIElements;
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

                int initialPage = dataVisualizer.GetCurrentPage(selectedType);
                dataVisualizer._objectListController.HandleNextPageRequested();

                Assert.AreEqual(initialPage + 1, dataVisualizer.GetCurrentPage(selectedType));
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


        [Test]
        public void HandleNextPageRequestedPublishesEvent()
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
                dataVisualizer.BuildObjectsView();

                List<ObjectPageChangedEvent> events = new List<ObjectPageChangedEvent>();
                using IDisposable subscription =
                    dataVisualizer._eventHub.Subscribe<ObjectPageChangedEvent>(events.Add);

                dataVisualizer._objectListController.HandleNextPageRequested();

                Assert.IsNotEmpty(events);
                ObjectPageChangedEvent lastEvent = events.Last();
                Assert.AreEqual(selectedType, lastEvent.ObjectType);
                Assert.AreEqual(1, lastEvent.PageIndex);
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
        public void HandleSelectionChangedPublishesSelectionEvent()
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

                dataVisualizer.BuildObjectsView();
                ScriptableObject selection = dataVisualizer._selectedObjects.First();

                List<ObjectSelectionChangedEvent> events = new List<ObjectSelectionChangedEvent>();
                using IDisposable subscription =
                    dataVisualizer._eventHub.Subscribe<ObjectSelectionChangedEvent>(events.Add);

                dataVisualizer._objectListController.HandleSelectionChanged(new object[] { selection });

                Assert.IsNotEmpty(events);
                ObjectSelectionChangedEvent lastEvent = events.Last();
                Assert.AreEqual(selection, lastEvent.PrimarySelection);
                CollectionAssert.Contains(lastEvent.Selections, selection);
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
            dataVisualizer._filteredMetadata.Clear();
            dataVisualizer._selectedObjects.Clear();
            List<ScriptableObject> created = new List<ScriptableObject>();
            for (int index = 0; index < 250; index++)
            {
                DummyScriptableObject obj =
                    ScriptableObject.CreateInstance<DummyScriptableObject>();
                obj.name = $"Item_{index}";
                dataVisualizer._filteredObjects.Add(obj);
                dataVisualizer._selectedObjects.Add(obj);
                string fakePath = $"Assets/TempDataVisualizerDragTests/Item_{index}.asset";
                DataAssetMetadata metadata = new DataAssetMetadata(
                    System.Guid.NewGuid().ToString(),
                    fakePath,
                    typeof(DummyScriptableObject),
                    typeof(DummyScriptableObject).FullName,
                    obj.name,
                    System.Array.Empty<string>(),
                    System.DateTime.UtcNow
                );
                dataVisualizer._filteredMetadata.Add(metadata);
                created.Add(obj);
            }

            ListView listView = new ListView
            {
                selectionType = SelectionType.Single,
                makeItem = () => new Label(),
                bindItem = (element, index) =>
                {
                    if (element is Label label && index >= 0 && index < dataVisualizer._displayedObjects.Count)
                    {
                        label.text = dataVisualizer._displayedObjects[index]?.name ?? string.Empty;
                    }
                },
            };
            dataVisualizer._objectListView = listView;
            dataVisualizer.rootVisualElement.Add(listView);

            dataVisualizer._emptyObjectLabel = new Label();
            dataVisualizer.rootVisualElement.Add(dataVisualizer._emptyObjectLabel);
            dataVisualizer._objectPageController = new VisualElement();
            dataVisualizer.rootVisualElement.Add(dataVisualizer._objectPageController);
            dataVisualizer._currentPageField = new IntegerField();
            dataVisualizer.rootVisualElement.Add(dataVisualizer._currentPageField);
            dataVisualizer._maxPageField = new IntegerField();
            dataVisualizer.rootVisualElement.Add(dataVisualizer._maxPageField);
            dataVisualizer._previousPageButton = new Button();
            dataVisualizer.rootVisualElement.Add(dataVisualizer._previousPageButton);
            dataVisualizer._nextPageButton = new Button();
            dataVisualizer.rootVisualElement.Add(dataVisualizer._nextPageButton);

            return created;
        }
    }
}
#pragma warning restore CS0618 // Type or member is obsolete
