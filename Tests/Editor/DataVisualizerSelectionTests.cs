#pragma warning disable CS0618 // Type or member is obsolete
namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Controllers;
    using Data;
    using NUnit.Framework;
    using Services;
    using State;
    using Styles;
    using UnityEditor;
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

        private const string SelectionTestFolderPath = "Assets/TempDataVisualizerSelectionTests";

        private sealed class StubLabelService : ILabelService
        {
            private readonly Dictionary<Type, TypeLabelFilterConfig> _configs =
                new Dictionary<Type, TypeLabelFilterConfig>();
            private readonly LabelSuggestionProvider _suggestionProvider =
                new LabelSuggestionProvider(new StubDataAssetService());

            public LabelSuggestionProvider SuggestionProvider
            {
                get { return _suggestionProvider; }
            }

            public TypeLabelFilterConfig GetOrCreateConfig(Type type)
            {
                if (type == null)
                {
                    return null;
                }

                if (!_configs.TryGetValue(type, out TypeLabelFilterConfig config))
                {
                    config = new TypeLabelFilterConfig { typeFullName = type.FullName };
                    _configs[type] = config;
                }

                return config;
            }

            public void SaveConfig(TypeLabelFilterConfig config) { }

            public void UpdateSessionState(
                Type type,
                TypeLabelFilterConfig config,
                VisualizerSessionState sessionState
            ) { }

            public LabelFilterResult ApplyFilter(
                Type type,
                IReadOnlyList<ScriptableObject> availableObjects,
                TypeLabelFilterConfig config
            )
            {
                List<ScriptableObject> filteredObjects =
                    availableObjects != null
                        ? new List<ScriptableObject>(availableObjects)
                        : new List<ScriptableObject>();
                List<DataAssetMetadata> filteredMetadata = new List<DataAssetMetadata>();
                List<string> uniqueLabels = new List<string>();
                int totalCount = filteredObjects.Count;
                int matchedCount = filteredObjects.Count;
                return new LabelFilterResult(
                    filteredObjects,
                    filteredMetadata,
                    uniqueLabels,
                    totalCount,
                    matchedCount,
                    string.Empty
                );
            }

            public IReadOnlyCollection<string> GetAvailableLabels(Type type)
            {
                return Array.Empty<string>();
            }

            public void ClearFilters(Type type) { }

            private sealed class StubDataAssetService : IDataAssetService
            {
                public event Action<DataAssetChangeEventArgs> AssetsChanged;

                public void ConfigureTrackedTypes(IEnumerable<Type> types) { }

                public void MarkDirty() { }

                public void ForceRebuild() { }

                public int GetAssetCount(Type type)
                {
                    return 0;
                }

                public DataAssetPage GetAssetsPage(Type type, int offset, int count)
                {
                    return new DataAssetPage(type, Array.Empty<DataAssetMetadata>(), 0, 0);
                }

                public IReadOnlyList<DataAssetMetadata> GetAssetsForType(Type type)
                {
                    return Array.Empty<DataAssetMetadata>();
                }

                public IReadOnlyList<string> GetGuidsForType(Type type)
                {
                    return Array.Empty<string>();
                }

                public IEnumerable<DataAssetMetadata> GetAllAssets()
                {
                    return Array.Empty<DataAssetMetadata>();
                }

                public bool TryGetAssetByGuid(string guid, out DataAssetMetadata metadata)
                {
                    metadata = null;
                    return false;
                }

                public bool TryGetAssetByPath(string path, out DataAssetMetadata metadata)
                {
                    metadata = null;
                    return false;
                }

                public void RefreshAsset(string guid) { }

                public void RefreshType(Type type) { }

                public void RemoveAsset(string guid) { }

                public IReadOnlyCollection<string> EnumerateLabels(Type type)
                {
                    return Array.Empty<string>();
                }
            }
        }

        [SetUp]
        public void SelectionSetUp()
        {
            if (!AssetDatabase.IsValidFolder(SelectionTestFolderPath))
            {
                AssetDatabase.CreateFolder("Assets", "TempDataVisualizerSelectionTests");
            }
        }

        [TearDown]
        public void SelectionTearDown()
        {
            if (AssetDatabase.IsValidFolder(SelectionTestFolderPath))
            {
                AssetDatabase.DeleteAsset(SelectionTestFolderPath);
                AssetDatabase.SaveAssets();
            }
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
                    StubUserStateRepository repository = new StubUserStateRepository
                    {
                        Settings = settings,
                        UserState = userState,
                    };

                    dataVisualizer.OverrideUserStateRepositoryForTesting(
                        repository,
                        settings,
                        userState
                    );

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
                    StubUserStateRepository repository = new StubUserStateRepository
                    {
                        Settings = settings,
                        UserState = userState,
                    };

                    dataVisualizer.OverrideUserStateRepositoryForTesting(
                        repository,
                        settings,
                        userState
                    );

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

        [Test]
        public void RestorePreviousSelectionWhenAssetNotLoadedDefersUntilAssetInserted()
        {
            DummyScriptableObject savedObject =
                ScriptableObject.CreateInstance<DummyScriptableObject>();
            string assetPath = SelectionTestFolderPath + "/DeferredSelection.asset";
            AssetDatabase.CreateAsset(savedObject, assetPath);
            AssetDatabase.SaveAssets();
            string savedGuid = AssetDatabase.AssetPathToGUID(assetPath);

            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            DataVisualizerSettings settings = null;
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;

                settings = ScriptableObject.CreateInstance<DataVisualizerSettings>();
                settings.persistStateInSettingsAsset = true;
                settings.lastSelectedTypeName = typeof(DummyScriptableObject).FullName;
                settings.SetLastObjectForType(typeof(DummyScriptableObject).FullName, savedGuid);

                DataVisualizerUserState userState = new DataVisualizerUserState();
                StubUserStateRepository repository = new StubUserStateRepository
                {
                    Settings = settings,
                    UserState = userState,
                };

                dataVisualizer.OverrideUserStateRepositoryForTesting(
                    repository,
                    settings,
                    userState
                );

                Dictionary<string, List<Type>> typeMap = new Dictionary<string, List<Type>>(
                    StringComparer.Ordinal
                )
                {
                    {
                        "DummyNamespace",
                        new List<Type> { typeof(DummyScriptableObject) }
                    },
                };
                Dictionary<string, int> namespaceOrder = new Dictionary<string, int>(
                    StringComparer.Ordinal
                )
                {
                    { "DummyNamespace", 0 },
                };
                dataVisualizer._scriptableObjectTypes = typeMap;
                dataVisualizer._namespaceOrder = namespaceOrder;
                dataVisualizer._namespaceController = new NamespaceController(
                    typeMap,
                    namespaceOrder
                )
                {
                    _selectedType = typeof(DummyScriptableObject),
                };

                StubLabelService labelService = new StubLabelService();
                dataVisualizer._labelService = labelService;
                dataVisualizer._labelPanelController = new LabelPanelController(
                    dataVisualizer,
                    labelService,
                    dataVisualizer.SessionState
                );

                dataVisualizer._inspectorContainer = new VisualElement();
                dataVisualizer._filterStatusLabel = new Label();
                dataVisualizer._labelFilterSelectionRoot = new VisualElement();

                dataVisualizer._sessionState.Selection.SetSelectedNamespace("DummyNamespace");
                dataVisualizer._sessionState.Selection.SetSelectedType(
                    typeof(DummyScriptableObject).FullName
                );
                dataVisualizer._sessionState.Selection.SetPrimarySelectedObject(savedGuid);

                dataVisualizer._selectedObjects.Clear();
                ObjectListState listState = dataVisualizer.ObjectListState;
                listState.ClearFiltered();
                listState.ClearDisplayed();

                dataVisualizer.RestorePreviousSelection();

                Assert.AreEqual(savedGuid, dataVisualizer._pendingSelectionGuid);
                Assert.IsNull(dataVisualizer._selectedObject);

                FieldInfo lookupField = typeof(DataVisualizer).GetField(
                    "_currentGuidOrderLookup",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Dictionary<string, int> currentLookup =
                    (Dictionary<string, int>)lookupField.GetValue(dataVisualizer);
                currentLookup.Clear();
                currentLookup[savedGuid] = 0;

                MethodInfo insertMethod = typeof(DataVisualizer).GetMethod(
                    "InsertLoadedAsset",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );

                DataAssetMetadata metadata = new DataAssetMetadata(
                    savedGuid,
                    assetPath,
                    typeof(DummyScriptableObject),
                    typeof(DummyScriptableObject).FullName,
                    savedObject.name,
                    Array.Empty<string>(),
                    DateTime.UtcNow
                );

                insertMethod.Invoke(
                    dataVisualizer,
                    new object[] { savedGuid, savedObject, metadata }
                );

                Assert.IsNull(dataVisualizer._pendingSelectionGuid);
                Assert.AreSame(savedObject, dataVisualizer._selectedObject);
                CollectionAssert.Contains(dataVisualizer._selectedObjects, savedObject);
            }
            finally
            {
                if (settings != null)
                {
                    Object.DestroyImmediate(settings);
                }

                Object.DestroyImmediate(dataVisualizer);
                if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath) != null)
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }

                AssetDatabase.SaveAssets();
                Object.DestroyImmediate(savedObject);
            }
        }
    }
}
