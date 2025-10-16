#pragma warning disable CS0618 // Type or member is obsolete
namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Data;
    using NUnit.Framework;
    using Services;
    using State;
    using Styles;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class DataVisualizerSelectionTests
    {
        private sealed class DummyScriptableObject : ScriptableObject { }

        private sealed class AnotherDummyScriptableObject : ScriptableObject { }

        private sealed class ThirdDummyScriptableObject : ScriptableObject { }

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
        public void BuildNamespaceViewWithSelectedTypeRetainsNamespaceSelection()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;

                DataVisualizerSettings settings =
                    ScriptableObject.CreateInstance<DataVisualizerSettings>();
                try
                {
                    settings.persistStateInSettingsAsset = true;
                    DataVisualizerUserState userState = new DataVisualizerUserState();
                    dataVisualizer._userStateRepository = new StubUserStateRepository
                    {
                        Settings = settings,
                        UserState = userState,
                    };

                    Dictionary<string, List<Type>> managedTypes = new Dictionary<
                        string,
                        List<Type>
                    >(StringComparer.Ordinal)
                    {
                        {
                            "TestNamespace",
                            new List<Type>
                            {
                                typeof(DummyScriptableObject),
                                typeof(AnotherDummyScriptableObject),
                            }
                        },
                    };
                    dataVisualizer._scriptableObjectTypes = managedTypes;

                    Dictionary<string, int> namespaceOrder = new Dictionary<string, int>(
                        StringComparer.Ordinal
                    )
                    {
                        { "TestNamespace", 0 },
                    };
                    dataVisualizer._namespaceOrder = namespaceOrder;

                    NamespaceController controller = new NamespaceController(
                        managedTypes,
                        namespaceOrder
                    );
                    dataVisualizer._namespaceController = controller;

                    controller._selectedType = typeof(DummyScriptableObject);

                    VisualElement namespaceListContainer = dataVisualizer._namespaceListContainer;
                    controller.Build(dataVisualizer, ref namespaceListContainer);
                    dataVisualizer._namespaceListContainer = namespaceListContainer;

                    VisualElement namespaceGroupItem = namespaceListContainer.Children().First();
                    Assert.AreEqual("TestNamespace", namespaceGroupItem.userData as string);
                    Assert.IsTrue(
                        namespaceGroupItem.ClassListContains(StyleConstants.SelectedClass)
                    );

                    VisualElement typesContainer = namespaceGroupItem.Q<VisualElement>(
                        name: "types-container-TestNamespace"
                    );
                    Assert.NotNull(typesContainer);

                    VisualElement typeItem = typesContainer
                        .Children()
                        .First(element =>
                            ReferenceEquals(element.userData, typeof(DummyScriptableObject))
                        );
                    Assert.IsTrue(typeItem.ClassListContains(StyleConstants.SelectedClass));

                    Label typeLabel = typeItem.Q<Label>(
                        name: NamespaceController.TypeItemLabelName
                    );
                    Assert.NotNull(typeLabel);
                    Assert.IsFalse(typeLabel.ClassListContains(StyleConstants.ClickableClass));
                }
                finally
                {
                    Object.DestroyImmediate(settings);
                }
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }

        [Test]
        public void BuildNamespaceViewAfterTypeMutationPreservesSelection()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;

                DataVisualizerSettings settings =
                    ScriptableObject.CreateInstance<DataVisualizerSettings>();
                try
                {
                    settings.persistStateInSettingsAsset = true;
                    DataVisualizerUserState userState = new DataVisualizerUserState();
                    dataVisualizer._userStateRepository = new StubUserStateRepository
                    {
                        Settings = settings,
                        UserState = userState,
                    };

                    Dictionary<string, List<Type>> managedTypes = new Dictionary<
                        string,
                        List<Type>
                    >(StringComparer.Ordinal)
                    {
                        {
                            "TestNamespace",
                            new List<Type>
                            {
                                typeof(DummyScriptableObject),
                                typeof(AnotherDummyScriptableObject),
                            }
                        },
                    };
                    dataVisualizer._scriptableObjectTypes = managedTypes;

                    Dictionary<string, int> namespaceOrder = new Dictionary<string, int>(
                        StringComparer.Ordinal
                    )
                    {
                        { "TestNamespace", 0 },
                    };
                    dataVisualizer._namespaceOrder = namespaceOrder;

                    NamespaceController controller = new NamespaceController(
                        managedTypes,
                        namespaceOrder
                    );
                    dataVisualizer._namespaceController = controller;

                    controller._selectedType = typeof(AnotherDummyScriptableObject);

                    VisualElement namespaceListContainer = dataVisualizer._namespaceListContainer;
                    controller.Build(dataVisualizer, ref namespaceListContainer);
                    dataVisualizer._namespaceListContainer = namespaceListContainer;

                    managedTypes["TestNamespace"].Add(typeof(ThirdDummyScriptableObject));

                    controller.Build(dataVisualizer, ref namespaceListContainer);
                    dataVisualizer._namespaceListContainer = namespaceListContainer;

                    VisualElement namespaceGroupItem = namespaceListContainer.Children().First();
                    Assert.IsTrue(
                        namespaceGroupItem.ClassListContains(StyleConstants.SelectedClass)
                    );

                    VisualElement typesContainer = namespaceGroupItem.Q<VisualElement>(
                        name: "types-container-TestNamespace"
                    );
                    Assert.NotNull(typesContainer);

                    VisualElement typeItem = typesContainer
                        .Children()
                        .First(element =>
                            ReferenceEquals(element.userData, typeof(AnotherDummyScriptableObject))
                        );
                    Assert.IsTrue(typeItem.ClassListContains(StyleConstants.SelectedClass));

                    Label typeLabel = typeItem.Q<Label>(
                        name: NamespaceController.TypeItemLabelName
                    );
                    Assert.NotNull(typeLabel);
                    Assert.IsFalse(typeLabel.ClassListContains(StyleConstants.ClickableClass));
                }
                finally
                {
                    Object.DestroyImmediate(settings);
                }
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }

        [Test]
        public void BindObjectRowSelectedObjectAssignsSelectedElement()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;
                ObjectListState listState = dataVisualizer.ObjectListState;
                List<ScriptableObject> displayedObjects = listState.DisplayedObjectsBuffer;

                DummyScriptableObject selectedObject =
                    ScriptableObject.CreateInstance<DummyScriptableObject>();
                try
                {
                    displayedObjects.Add(selectedObject);

                    dataVisualizer._selectedObject = selectedObject;
                    VisualElement row = dataVisualizer.MakeObjectRow();
                    dataVisualizer.BindObjectRow(row, 0);
                    VisualElement selectedElement = dataVisualizer._selectedElement;

                    Assert.AreSame(row, selectedElement);
                    Assert.IsTrue(row.ClassListContains(StyleConstants.SelectedClass));

                    Label titleLabel = row.Q<Label>(name: "object-item-label");
                    Assert.NotNull(titleLabel);
                    Assert.IsFalse(titleLabel.ClassListContains(StyleConstants.ClickableClass));
                }
                finally
                {
                    Object.DestroyImmediate(selectedObject);
                    displayedObjects.Clear();
                }
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }

        [Test]
        public void BindObjectRowReuseClearsPreviousSelectedElement()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;
                ObjectListState listState = dataVisualizer.ObjectListState;
                List<ScriptableObject> displayedObjects = listState.DisplayedObjectsBuffer;

                DummyScriptableObject selectedObject =
                    ScriptableObject.CreateInstance<DummyScriptableObject>();
                DummyScriptableObject replacementObject =
                    ScriptableObject.CreateInstance<DummyScriptableObject>();
                try
                {
                    displayedObjects.Add(selectedObject);
                    dataVisualizer._selectedObject = selectedObject;

                    VisualElement row = dataVisualizer.MakeObjectRow();
                    dataVisualizer.BindObjectRow(row, 0);

                    VisualElement selectedElement = dataVisualizer._selectedElement;
                    Assert.AreSame(row, selectedElement);

                    displayedObjects.Clear();
                    displayedObjects.Add(replacementObject);

                    dataVisualizer.BindObjectRow(row, 0);

                    selectedElement = dataVisualizer._selectedElement;
                    Assert.IsNull(selectedElement);
                    Assert.IsFalse(row.ClassListContains(StyleConstants.SelectedClass));

                    Label titleLabel = row.Q<Label>(name: "object-item-label");
                    Assert.NotNull(titleLabel);
                    Assert.IsTrue(titleLabel.ClassListContains(StyleConstants.ClickableClass));
                }
                finally
                {
                    Object.DestroyImmediate(selectedObject);
                    Object.DestroyImmediate(replacementObject);
                    displayedObjects.Clear();
                }
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }

        [Test]
        public void UnbindObjectRowClearsSelectedElement()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;
                ObjectListState listState = dataVisualizer.ObjectListState;
                List<ScriptableObject> displayedObjects = listState.DisplayedObjectsBuffer;

                DummyScriptableObject selectedObject =
                    ScriptableObject.CreateInstance<DummyScriptableObject>();
                try
                {
                    displayedObjects.Add(selectedObject);
                    dataVisualizer._selectedObject = selectedObject;

                    VisualElement row = dataVisualizer.MakeObjectRow();
                    dataVisualizer.BindObjectRow(row, 0);
                    dataVisualizer.UnbindObjectRow(row, 0);
                    VisualElement selectedElement = dataVisualizer._selectedElement;

                    Assert.IsNull(selectedElement);
                    Assert.IsFalse(row.ClassListContains(StyleConstants.SelectedClass));

                    Label titleLabel = row.Q<Label>(name: "object-item-label");
                    Assert.NotNull(titleLabel);
                    Assert.IsTrue(titleLabel.ClassListContains(StyleConstants.ClickableClass));
                }
                finally
                {
                    Object.DestroyImmediate(selectedObject);
                    displayedObjects.Clear();
                }
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }
    }
}
