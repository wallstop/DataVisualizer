namespace WallstopStudios.Editor.DataVisualizer
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Text.RegularExpressions;
    using Data;
    using Extensions;
    using Helper;
    using Search;
#if ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using Sirenix.OdinInspector.Editor;
#endif
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer;

    public sealed class DataVisualizer : EditorWindow
    {
        private const string PackageId = "com.wallstop-studios.data-visualizer";
        private const string PrefsPrefix = "WallstopStudios.DataVisualizer.";

        private const string PrefsSplitterOuterKey = PrefsPrefix + "SplitterOuterFixedPaneWidth";
        private const string PrefsSplitterInnerKey = PrefsPrefix + "SplitterInnerFixedPaneWidth";

        private const string SettingsDefaultPath = "Assets/DataVisualizerSettings.asset";
        private const string UserStateFileName = "DataVisualizerUserState.json";

        private const string NamespaceItemClass = "namespace-item";
        private const string NamespaceHeaderClass = "namespace-header";
        private const string NamespaceGroupHeaderClass = "namespace-group-header";
        private const string NamespaceIndicatorClass = "namespace-indicator";
        private const string NamespaceLabelClass = "namespace-item__label";
        private const string TypeItemClass = "type-item";
        private const string TypeLabelClass = "type-item__label";
        private const string ObjectItemClass = "object-item";
        private const string ObjectItemContentClass = "object-item-content";
        private const string ObjectItemActionsClass = "object-item-actions";
        private const string ActionButtonClass = "action-button";
        private const string PopoverListItemClassName = "type-selection-list-item";
        private const string PopoverListItemDisabledClassName =
            "type-selection-list-item--disabled";
        private const string PopoverListNamespaceClassName = "type-selection-list-namespace";
        private const string PopoverNamespaceHeaderClassName = "popover-namespace-header";
        private const string PopoverNamespaceIndicatorClassName = "popover-namespace-indicator";
        private const string SearchResultItemClass = "search-result-item";
        private const string SearchResultHighlightClass = "search-result-item--highlighted";
        private const string PopoverHighlightClass = "popover-item--highlighted";
        private const string NavHighlightClass = "selected";

        private const string ArrowCollapsed = "►";
        private const string ArrowExpanded = "▼";

        private const int MaxSearchResults = 25;

        private const float DragDistanceThreshold = 5f;
        private const float DragUpdateThrottleTime = 0.05f;
        private const float DefaultOuterSplitWidth = 200f;
        private const float DefaultInnerSplitWidth = 250f;

        private enum DragType
        {
            None = 0,
            Object = 1,
            Namespace = 2,
            Type = 3,
        }

        private enum FocusArea
        {
            None = 0,
            TypeList = 1,
            AddTypePopover = 2,
            SearchResultsPopover = 3,
        }

        private readonly List<(string key, List<Type> types)> _scriptableObjectTypes = new();
        private readonly Dictionary<ScriptableObject, VisualElement> _objectVisualElementMap =
            new();
        private readonly List<ScriptableObject> _selectedObjects = new();
        private readonly List<ScriptableObject> _allManagedSOsCache = new();
        private readonly Dictionary<Type, VisualElement> _namespaceCache = new();
        private readonly List<VisualElement> _currentSearchResultItems = new();
        private readonly List<VisualElement> _currentTypePopoverItems = new();
        private readonly List<VisualElement> _currentNavigableTypeItems = new();

        private ScriptableObject _selectedObject;
        private VisualElement _selectedElement;
        private VisualElement _selectedNamespaceElement;

        private VisualElement _namespaceListContainer;
        private VisualElement _objectListContainer;
        private VisualElement _inspectorContainer;
        private ScrollView _objectScrollView;
        private ScrollView _inspectorScrollView;

        private TwoPaneSplitView _outerSplitView;
        private TwoPaneSplitView _innerSplitView;
        private VisualElement _namespaceColumnElement;
        private VisualElement _objectColumnElement;

        private VisualElement _settingsPopover;
        private VisualElement _renamePopover;
        private VisualElement _confirmDeletePopover;
        private VisualElement _typeAddPopover;
        private VisualElement _activePopover;
        private VisualElement _confirmNamespaceAddPopover;
        private VisualElement _activeNestedPopover;
        private object _popoverContext;

        private TextField _searchField;
        private VisualElement _searchPopover;
        private bool _isSearchCachePopulated;
        private string _lastSearchString;

        private Button _addTypeButton;
        private TextField _typeSearchField;
        private VisualElement _typePopoverListContainer;

        private float _lastSavedOuterWidth = -1f;
        private float _lastSavedInnerWidth = -1f;
        private IVisualElementScheduledItem _saveWidthsTask;

        private int _searchHighlightIndex = -1;
        private int _typePopoverHighlightIndex = -1;
        private int _typeHighlightIndex = -1;
        private VisualElement _typeListParentNamespaceHeader;
        private string _lastTypeAddSearchTerm;
        private FocusArea _lastActiveFocusArea = FocusArea.None;
        private DragType _activeDragType = DragType.None;
        private object _draggedData;
        private Type _selectedType;
        private VisualElement _selectedTypeElement;
        private VisualElement _inPlaceGhost;
        private int _lastGhostInsertIndex = -1;
        private VisualElement _lastGhostParent;
        private VisualElement _draggedElement;
        private VisualElement _dragGhost;
        private Vector2 _dragStartPosition;
        private bool _isDragging;
        private float _lastDragUpdateTime;
        private SerializedObject _currentInspectorScriptableObject;

        private string _userStateFilePath;
        private DataVisualizerUserState _userState;
        private bool _userStateDirty;

        private List<Type> _relevantScriptableObjectTypes;

        private DataVisualizerSettings _settings;

        private float? _lastAddTypeClicked;
        private float? _lastEnterPressed;

        private Label _dataFolderPathDisplay;
#if ODIN_INSPECTOR
        private PropertyTree _odinPropertyTree;
        private IMGUIContainer _odinInspectorContainer;
        private IVisualElementScheduledItem _odinRepaintSchedule;
#endif

        [MenuItem("Tools/Data Visualizer")]
        public static void ShowWindow()
        {
            DataVisualizer window = GetWindow<DataVisualizer>("Data Visualizer");
            window.titleContent = new GUIContent("Data Visualizer");
        }

        private void OnEnable()
        {
            _isSearchCachePopulated = false;
            _objectVisualElementMap.Clear();
            _selectedObject = null;
            _selectedElement = null;
            _selectedObjects.Clear();
#if ODIN_INSPECTOR
            _odinPropertyTree = null;
#endif
            _settings = LoadOrCreateSettings();
            _userStateFilePath = Path.Combine(Application.persistentDataPath, UserStateFileName);
            if (!_settings.persistStateInSettingsAsset)
            {
                LoadUserStateFromFile();
            }
            else
            {
                _userState = new DataVisualizerUserState();
            }

            LoadScriptableObjectTypes();
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            rootVisualElement.RegisterCallback<KeyDownEvent>(
                HandleGlobalKeyDown,
                TrickleDown.TrickleDown
            );
            rootVisualElement
                .schedule.Execute(() =>
                {
                    PopulateSearchCache();
                    RestorePreviousSelection();
                    StartPeriodicWidthSave();
                })
                .ExecuteLater(10);
        }

        private void OnDisable()
        {
            rootVisualElement.UnregisterCallback<KeyDownEvent>(
                HandleGlobalKeyDown,
                TrickleDown.TrickleDown
            );
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            Cleanup();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            _allManagedSOsCache.Clear();
            _currentSearchResultItems.Clear();
            _currentTypePopoverItems.Clear();
            _isSearchCachePopulated = false;
            CloseActivePopover();
            CancelDrag();
            _saveWidthsTask?.Pause();
            if (!_settings.persistStateInSettingsAsset && _userStateDirty)
            {
                SaveUserStateToFile();
            }

            _saveWidthsTask = null;
            _currentInspectorScriptableObject?.Dispose();
            _currentInspectorScriptableObject = null;
            _dragGhost?.RemoveFromHierarchy();
            _dragGhost = null;
            _draggedElement = null;
#if ODIN_INSPECTOR
            _odinRepaintSchedule?.Pause();
            _odinRepaintSchedule = null;
            if (_odinPropertyTree != null)
            {
                _odinPropertyTree.OnPropertyValueChanged -= HandleOdinPropertyValueChanged;
                _odinPropertyTree.Dispose();
                _odinPropertyTree = null;
            }
            _odinInspectorContainer?.RemoveFromHierarchy();
            _odinInspectorContainer?.Dispose();
            _odinInspectorContainer = null;
#endif
        }

        private void PopulateSearchCache()
        {
            if (_settings == null)
            {
                _settings = LoadOrCreateSettings();
            }

            _allManagedSOsCache.Clear();
            HashSet<Type> managedTypes = _scriptableObjectTypes
                .SelectMany(tuple => tuple.types)
                .ToHashSet();
            HashSet<string> uniqueGuids = new(StringComparer.OrdinalIgnoreCase);

            foreach (
                Type type in AppDomain
                    .CurrentDomain.GetAssemblies()
                    .SelectMany(assembly => assembly.GetTypes())
                    .Where(managedTypes.Contains)
            )
            {
                string[] guids = AssetDatabase.FindAssets($"t:{type.Name}");
                foreach (string guid in guids)
                {
                    if (!uniqueGuids.Add(guid))
                    {
                        continue;
                    }
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        ScriptableObject obj =
                            AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;

                        if (obj != null && obj.GetType() == type)
                        {
                            _allManagedSOsCache.Add(obj);
                        }
                    }
                }
            }

            _allManagedSOsCache.Sort(
                (a, b) =>
                {
                    int comparison = string.Compare(a.name, b.name, StringComparison.Ordinal);
                    if (comparison != 0)
                    {
                        return comparison;
                    }
                    return string.Compare(
                        a.GetType().FullName,
                        b.GetType().FullName,
                        StringComparison.Ordinal
                    );
                }
            );

            _isSearchCachePopulated = true;
        }

        public static void SignalRefresh()
        {
            if (!HasOpenInstances<DataVisualizer>())
            {
                return;
            }

            DataVisualizer window = GetWindow<DataVisualizer>(false, null, false);
            if (window != null)
            {
                window.ScheduleRefresh();
            }
        }

        private void ScheduleRefresh()
        {
            rootVisualElement.schedule.Execute(RefreshAllViews).ExecuteLater(50);
        }

        private void RefreshAllViews()
        {
            string previousNamespaceKey =
                _selectedType != null ? GetNamespaceKey(_selectedType) : null;
            string previousTypeName = _selectedType?.Name;
            string previousObjectGuid = null;
            if (_selectedObject != null)
            {
                string path = AssetDatabase.GetAssetPath(_selectedObject);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    previousObjectGuid = AssetDatabase.AssetPathToGUID(path);
                }
            }

            LoadScriptableObjectTypes();

            _selectedType = null;
            _selectedObject = null;
            _selectedElement = null;
            _selectedTypeElement = null;

            int namespaceIndex = -1;
            if (!string.IsNullOrWhiteSpace(previousNamespaceKey))
            {
                namespaceIndex = _scriptableObjectTypes.FindIndex(kvp =>
                    string.Equals(kvp.key, previousNamespaceKey, StringComparison.Ordinal)
                );
            }
            if (namespaceIndex < 0 && 0 < _scriptableObjectTypes.Count)
            {
                namespaceIndex = 0;
            }

            if (0 <= namespaceIndex)
            {
                List<Type> typesInNamespace = _scriptableObjectTypes[namespaceIndex].types;
                if (0 < typesInNamespace.Count)
                {
                    if (!string.IsNullOrWhiteSpace(previousTypeName))
                    {
                        _selectedType = typesInNamespace.FirstOrDefault(t =>
                            string.Equals(t.Name, previousTypeName, StringComparison.Ordinal)
                        );
                    }

                    _selectedType ??= typesInNamespace[0];
                }
            }

            if (_selectedType != null)
            {
                LoadObjectTypes(_selectedType);
            }
            else
            {
                _selectedObjects.Clear();
            }

            if (
                _selectedType != null
                && !string.IsNullOrWhiteSpace(previousObjectGuid)
                && 0 < _selectedObjects.Count
            )
            {
                _selectedObject = _selectedObjects.Find(obj =>
                {
                    if (obj == null)
                    {
                        return false;
                    }

                    string path = AssetDatabase.GetAssetPath(obj);
                    return !string.IsNullOrWhiteSpace(path)
                        && string.Equals(
                            AssetDatabase.AssetPathToGUID(path),
                            previousObjectGuid,
                            StringComparison.OrdinalIgnoreCase
                        );
                });
            }

            BuildNamespaceView();
            BuildObjectsView();

            VisualElement typeElementToSelect = FindTypeElement(_selectedType);
            if (typeElementToSelect != null)
            {
                _selectedTypeElement = typeElementToSelect;
                _selectedTypeElement.AddToClassList("selected");
                VisualElement ancestorGroup = FindAncestorNamespaceGroup(_selectedTypeElement);
                if (ancestorGroup != null)
                {
                    ExpandNamespaceGroupIfNeeded(ancestorGroup, false);
                }
            }

            SelectObject(_selectedObject);
            PopulateSearchCache();
        }

        private VisualElement FindAncestorNamespaceGroup(VisualElement startingElement)
        {
            if (startingElement == null)
            {
                return null;
            }

            VisualElement currentElement = startingElement;
            while (currentElement != null && currentElement != _namespaceListContainer)
            {
                if (currentElement.ClassListContains("object-item"))
                {
                    return currentElement;
                }
                currentElement = currentElement.parent;
            }
            return null;
        }

        private void ExpandNamespaceGroupIfNeeded(VisualElement namespaceGroupItem, bool saveState)
        {
            if (namespaceGroupItem == null)
            {
                return;
            }

            Label indicator = namespaceGroupItem.Q<Label>(className: "namespace-indicator");
            string nsKey = namespaceGroupItem.userData as string;
            VisualElement typesContainer = namespaceGroupItem.Q<VisualElement>(
                $"types-container-{nsKey}"
            );

            if (
                indicator != null
                && typesContainer != null
                && typesContainer.style.display == DisplayStyle.None
            )
            {
                ApplyNamespaceCollapsedState(indicator, typesContainer, false, saveState);
            }
        }

        private DataVisualizerSettings LoadOrCreateSettings()
        {
            DataVisualizerSettings settings = null;

            string[] guids = AssetDatabase.FindAssets($"t:{nameof(DataVisualizerSettings)}");

            if (0 < guids.Length)
            {
                if (1 < guids.Length)
                {
                    Debug.LogWarning(
                        $"Multiple DataVisualizerSettings assets found ({guids.Length}). Using the first one."
                    );
                }

                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }
                    settings = AssetDatabase.LoadAssetAtPath<DataVisualizerSettings>(path);
                    if (settings != null)
                    {
                        break;
                    }
                }
            }

            if (settings == null)
            {
                Debug.Log(
                    $"No DataVisualizerSettings found, creating default at '{SettingsDefaultPath}'"
                );
                settings = CreateInstance<DataVisualizerSettings>();
                settings._dataFolderPath = DataVisualizerSettings.DefaultDataFolderPath;

                string dir = Path.GetDirectoryName(SettingsDefaultPath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                try
                {
                    AssetDatabase.CreateAsset(settings, SettingsDefaultPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    settings = AssetDatabase.LoadAssetAtPath<DataVisualizerSettings>(
                        SettingsDefaultPath
                    );
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to create DataVisualizerSettings asset. {e}");
                    settings = CreateInstance<DataVisualizerSettings>();
                    settings._dataFolderPath = DataVisualizerSettings.DefaultDataFolderPath;
                }
            }

            if (settings != null)
            {
                return settings;
            }

            Debug.LogError(
                "Failed to load or create DataVisualizerSettings. Using temporary instance."
            );
            settings = CreateInstance<DataVisualizerSettings>();
            settings._dataFolderPath = DataVisualizerSettings.DefaultDataFolderPath;
            return settings;
        }

        private void StartPeriodicWidthSave()
        {
            _saveWidthsTask?.Pause();
            _saveWidthsTask = rootVisualElement
                .schedule.Execute(CheckAndSaveSplitterWidths)
                .Every(1000);
        }

        private void CheckAndSaveSplitterWidths()
        {
            if (
                _outerSplitView == null
                || _innerSplitView == null
                || _namespaceColumnElement == null
                || _objectColumnElement == null
                || float.IsNaN(_namespaceColumnElement.resolvedStyle.width)
                || float.IsNaN(_objectColumnElement.resolvedStyle.width)
            )
            {
                return;
            }

            float currentOuterWidth = _namespaceColumnElement.resolvedStyle.width;
            float currentInnerWidth = _objectColumnElement.resolvedStyle.width;

            if (!Mathf.Approximately(currentOuterWidth, _lastSavedOuterWidth))
            {
                EditorPrefs.SetFloat(PrefsSplitterOuterKey, currentOuterWidth);
                _lastSavedOuterWidth = currentOuterWidth;
            }

            if (!Mathf.Approximately(currentInnerWidth, _lastSavedInnerWidth))
            {
                EditorPrefs.SetFloat(PrefsSplitterInnerKey, currentInnerWidth);
                _lastSavedInnerWidth = currentInnerWidth;
            }
        }

        private void OnUndoRedoPerformed()
        {
            ScheduleRefresh();
        }

        private void RestorePreviousSelection()
        {
            if (_scriptableObjectTypes.Count == 0)
            {
                return;
            }

            string savedNamespaceKey = GetLastSelectedNamespaceKey();
            List<Type> typesInNamespace;
            int namespaceIndex = -1;

            if (!string.IsNullOrWhiteSpace(savedNamespaceKey))
            {
                namespaceIndex = _scriptableObjectTypes.FindIndex(kvp =>
                    string.Equals(kvp.key, savedNamespaceKey, StringComparison.Ordinal)
                );
            }

            if (0 <= namespaceIndex)
            {
                typesInNamespace = _scriptableObjectTypes[namespaceIndex].types;
            }
            else if (0 < _scriptableObjectTypes.Count)
            {
                (string key, List<Type> types) types = _scriptableObjectTypes[0];
                typesInNamespace = types.types;
            }
            else
            {
                typesInNamespace = null;
            }

            if (typesInNamespace is not { Count: > 0 })
            {
                return;
            }

            string savedTypeName = GetLastSelectedTypeName();
            Type selectedType = null;

            if (!string.IsNullOrWhiteSpace(savedTypeName))
            {
                selectedType = typesInNamespace.Find(t =>
                    string.Equals(t.Name, savedTypeName, StringComparison.Ordinal)
                );
            }

            selectedType ??= typesInNamespace[0];
            _selectedType = selectedType;

            LoadObjectTypes(_selectedType);
            BuildNamespaceView();
            BuildObjectsView();

            VisualElement typeElementToSelect = FindTypeElement(_selectedType);
            if (typeElementToSelect != null)
            {
                _selectedTypeElement?.RemoveFromClassList("selected");
                _selectedTypeElement = typeElementToSelect;
                _selectedTypeElement.AddToClassList("selected");

                VisualElement ancestorGroup = null;
                VisualElement currentElement = _selectedTypeElement;
                while (currentElement != null && currentElement != _namespaceListContainer)
                {
                    if (currentElement.ClassListContains(NamespaceItemClass))
                    {
                        ancestorGroup = currentElement;
                        break;
                    }
                    currentElement = currentElement.parent;
                }

                if (ancestorGroup != null)
                {
                    Label indicator = ancestorGroup.Q<Label>(className: NamespaceIndicatorClass);
                    VisualElement typesContainer = ancestorGroup.Q<VisualElement>(
                        $"types-container-{ancestorGroup.userData as string}"
                    );

                    if (
                        indicator != null
                        && typesContainer != null
                        && typesContainer.style.display == DisplayStyle.None
                    )
                    {
                        ApplyNamespaceCollapsedState(indicator, typesContainer, false, false);
                    }
                }
            }

            string savedObjectGuid = null;
            if (_selectedType != null)
            {
                savedObjectGuid = GetLastSelectedObjectGuidForType(_selectedType.Name);
            }
            ScriptableObject objectToSelect = null;

            if (!string.IsNullOrWhiteSpace(savedObjectGuid) && 0 < _selectedObjects.Count)
            {
                objectToSelect = _selectedObjects.Find(obj =>
                {
                    if (obj == null)
                    {
                        return false;
                    }

                    string path = AssetDatabase.GetAssetPath(obj);
                    return !string.IsNullOrWhiteSpace(path)
                        && string.Equals(
                            AssetDatabase.AssetPathToGUID(path),
                            savedObjectGuid,
                            StringComparison.OrdinalIgnoreCase
                        );
                });
            }

            if (objectToSelect == null && 0 < _selectedObjects.Count)
            {
                objectToSelect = _selectedObjects[0];
            }

            SelectObject(objectToSelect);
        }

        private VisualElement FindTypeElement(Type targetType)
        {
            if (targetType == null || _namespaceListContainer == null)
            {
                return null;
            }

            List<VisualElement> typeItems = _namespaceListContainer
                .Query<VisualElement>(className: "type-item")
                .ToList();

            foreach (VisualElement item in typeItems)
            {
                if (item.userData is Type itemType && itemType == targetType)
                {
                    return item;
                }
            }
            return null;
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            TryLoadStyleSheet(root);

            VisualElement headerRow = new()
            {
                name = "header-row",
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.FlexStart,
                    paddingTop = 5,
                    paddingBottom = 5,
                    paddingLeft = 5,
                    paddingRight = 5,
                    borderBottomWidth = 1,
                    borderBottomColor = Color.gray,
                },
            };
            root.Add(headerRow);

            Button settingsButton = null;
            // ReSharper disable once AccessToModifiedClosure
            settingsButton = new Button(() => TogglePopover(_settingsPopover, settingsButton))
            {
                text = "…",
                name = "settings-button",
                tooltip = "Open Settings",
            };

            settingsButton.AddToClassList("icon-button");
            settingsButton.AddToClassList("clickable");
            headerRow.Add(settingsButton);

            _searchField = new TextField
            {
                name = "global-search-field",
                style = { flexGrow = 1, marginRight = 10 },
            };
            _searchField.SetPlaceholderText("Search...");
            _searchField.RegisterValueChangedCallback(evt => PerformSearch(evt.newValue));
            _searchField.RegisterCallback<FocusInEvent, DataVisualizer>(
                (_, context) =>
                {
                    if (
                        !string.IsNullOrWhiteSpace(context._searchField.value)
                        && context._searchPopover.childCount > 0
                        && context._activePopover != context._searchPopover
                    )
                    {
                        context.OpenPopover(context._searchPopover, context._searchField);
                    }
                },
                this
            );
            _searchField.RegisterCallback<KeyDownEvent>(HandleSearchKeyDown);
            headerRow.Add(_searchField);

            float initialOuterWidth = EditorPrefs.GetFloat(
                PrefsSplitterOuterKey,
                DefaultOuterSplitWidth
            );
            float initialInnerWidth = EditorPrefs.GetFloat(
                PrefsSplitterInnerKey,
                DefaultInnerSplitWidth
            );

            _lastSavedOuterWidth = initialOuterWidth;
            _lastSavedInnerWidth = initialInnerWidth;
            _namespaceColumnElement = CreateNamespaceColumn();
            _objectColumnElement = CreateObjectColumn();
            VisualElement inspectorColumn = CreateInspectorColumn();

            _innerSplitView = new TwoPaneSplitView(
                0,
                (int)initialInnerWidth,
                TwoPaneSplitViewOrientation.Horizontal
            )
            {
                name = "inner-split-view",
                style = { flexGrow = 1 },
            };

            _innerSplitView.Add(_objectColumnElement);
            _innerSplitView.Add(inspectorColumn);
            _outerSplitView = new TwoPaneSplitView(
                0,
                (int)initialOuterWidth,
                TwoPaneSplitViewOrientation.Horizontal
            )
            {
                name = "outer-split-view",
                style = { flexGrow = 1 },
            };
            _outerSplitView.Add(_namespaceColumnElement);
            _outerSplitView.Add(_innerSplitView);
            root.Add(_outerSplitView);

            _settingsPopover = CreatePopoverBase("settings-popover");
            BuildSettingsPopoverContent();
            root.Add(_settingsPopover);

            _renamePopover = CreatePopoverBase("rename-popover");

            root.Add(_renamePopover);

            _confirmDeletePopover = CreatePopoverBase("confirm-delete-popover");

            root.Add(_confirmDeletePopover);

            _searchPopover = CreatePopoverBase("search-popover");
            _searchPopover.style.flexGrow = 1;
            _searchPopover.style.height = 500;
            root.Add(_searchPopover);

            _typeAddPopover = new VisualElement
            {
                name = "type-add-popover",
                style =
                {
                    position = Position.Absolute,
                    width = 250,
                    maxHeight = 400,
                    backgroundColor = new Color(0.22f, 0.22f, 0.22f, 1f),
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderBottomColor = Color.black,
                    borderLeftColor = Color.black,
                    borderRightColor = Color.black,
                    borderTopColor = Color.black,
                    display = DisplayStyle.None,
                },
            };

            _typeSearchField = new TextField
            {
                name = "type-search-field",
                style =
                {
                    marginLeft = 4,
                    marginRight = 4,
                    marginTop = 4,
                    marginBottom = 2,
                },
            };
            _typeSearchField.SetPlaceholderText("Search...");
            _typeSearchField.RegisterValueChangedCallback(evt => BuildTypeAddList(evt.newValue));
            _typeSearchField.RegisterCallback<KeyDownEvent>(HandleTypePopoverKeyDown);
            _typeAddPopover.Add(_typeSearchField);

            ScrollView typePopoverScrollView = new(ScrollViewMode.Vertical)
            {
                style = { flexGrow = 1 },
            };
            _typeAddPopover.Add(typePopoverScrollView);

            _typePopoverListContainer = new VisualElement { name = "type-add-list-content" };
            typePopoverScrollView.Add(_typePopoverListContainer);

            root.Add(_typeAddPopover);

            _confirmNamespaceAddPopover = CreatePopoverBase("confirm-namespace-add-popover");
            _confirmNamespaceAddPopover.style.width = 300;
            _confirmNamespaceAddPopover.style.minHeight = 80;
            _confirmNamespaceAddPopover.style.maxHeight = 150;
            root.Add(_confirmNamespaceAddPopover);

            BuildNamespaceView();
            BuildObjectsView();
            BuildInspectorView();
        }

        private static void TryLoadStyleSheet(VisualElement root)
        {
            StyleSheet styleSheet = null;
            Font font = null;
            string packageRoot = DirectoryHelper.FindPackageRootPath(
                DirectoryHelper.GetCallerScriptDirectory()
            );
            if (!string.IsNullOrWhiteSpace(packageRoot))
            {
                if (
                    packageRoot.StartsWith("Packages", StringComparison.OrdinalIgnoreCase)
                    && !packageRoot.Contains(PackageId, StringComparison.OrdinalIgnoreCase)
                )
                {
                    int dataVisualizerIndex = packageRoot.LastIndexOf(
                        "DataVisualizer",
                        StringComparison.Ordinal
                    );
                    if (0 <= dataVisualizerIndex)
                    {
                        packageRoot = packageRoot[..dataVisualizerIndex];
                        packageRoot += PackageId;
                    }
                }

                char pathSeparator = Path.DirectorySeparatorChar;
                string styleSheetPath =
                    $"{packageRoot}{pathSeparator}Editor{pathSeparator}DataVisualizer{pathSeparator}Styles{pathSeparator}DataVisualizerStyles.uss";
                string unityRelativeStyleSheetPath = DirectoryHelper.AbsoluteToUnityRelativePath(
                    styleSheetPath
                );
                if (!string.IsNullOrWhiteSpace(unityRelativeStyleSheetPath))
                {
                    styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                        unityRelativeStyleSheetPath
                    );
                    if (styleSheet == null)
                    {
                        Debug.LogError(
                            $"Failed to load Data Visualizer style sheet (package root: '{packageRoot}'), relative path '{unityRelativeStyleSheetPath}'."
                        );
                    }
                }
                else
                {
                    Debug.LogError(
                        $"Failed to convert absolute path '{styleSheetPath}' to Unity relative path."
                    );
                }

                string fontPath =
                    $"{packageRoot}{pathSeparator}Editor{pathSeparator}Fonts{pathSeparator}IBMPlexMono-Regular.ttf";
                string unityRelativeFontPath = DirectoryHelper.AbsoluteToUnityRelativePath(
                    fontPath
                );
                if (!string.IsNullOrWhiteSpace(unityRelativeFontPath))
                {
                    font = AssetDatabase.LoadAssetAtPath<Font>(unityRelativeFontPath);
                }
            }
            else
            {
                Debug.LogError(
                    $"Failed to find Data Visualizer style sheet (package root: '{packageRoot}')."
                );
            }
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }
            else
            {
                Debug.LogError(
                    $"Failed to find Data Visualizer style sheet (package root: '{packageRoot}')."
                );
            }

            if (font != null)
            {
                root.style.unityFontDefinition = new StyleFontDefinition(font);
            }
            else
            {
                Debug.LogError(
                    $"Failed to find Data Visualizer font (package root: '{packageRoot}')."
                );
            }

            root.style.fontSize = 14;
        }

        private void HandleSearchKeyDown(KeyDownEvent evt)
        {
            Debug.Log("SEARCH KEY DOWN");
            if (
                _activePopover != _searchPopover
                || _searchPopover.style.display == DisplayStyle.None
                || _currentSearchResultItems.Count == 0
            )
            {
                return;
            }

            bool highlightChanged = false;

            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                {
                    _searchHighlightIndex++;
                    if (_searchHighlightIndex >= _currentSearchResultItems.Count)
                    {
                        _searchHighlightIndex = 0;
                    }

                    highlightChanged = true;
                    break;
                }
                case KeyCode.UpArrow:
                {
                    _searchHighlightIndex--;
                    if (_searchHighlightIndex < 0)
                    {
                        _searchHighlightIndex = _currentSearchResultItems.Count - 1;
                    }

                    highlightChanged = true;
                    break;
                }
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                {
                    if (
                        _searchHighlightIndex >= 0
                        && _searchHighlightIndex < _currentSearchResultItems.Count
                    )
                    {
                        if (
                            _currentSearchResultItems[_searchHighlightIndex].userData
                            is ScriptableObject selectedObject
                        )
                        {
                            NavigateToObject(selectedObject);
                        }

                        evt.PreventDefault();
                        evt.StopPropagation();
                    }

                    break;
                }
                case KeyCode.Escape:
                {
                    CloseActivePopover();
                    evt.PreventDefault();
                    evt.StopPropagation();
                    break;
                }
                default:
                {
                    return;
                }
            }

            if (highlightChanged)
            {
                UpdateSearchResultHighlight();
                evt.PreventDefault();
                evt.StopPropagation();
            }
        }

        private void UpdateSearchResultHighlight()
        {
            if (_currentSearchResultItems == null || _searchPopover == null)
            {
                return;
            }

            ScrollView scrollView = _searchPopover.Q<ScrollView>("search-scroll");

            for (int i = 0; i < _currentSearchResultItems.Count; i++)
            {
                VisualElement item = _currentSearchResultItems[i];
                if (item == null)
                {
                    continue;
                }

                if (i == _searchHighlightIndex)
                {
                    item.AddToClassList(SearchResultHighlightClass);
                    scrollView?.ScrollTo(item);
                }
                else
                {
                    item.RemoveFromClassList(SearchResultHighlightClass);
                }
            }
        }

        private void PerformSearch(string searchText)
        {
            searchText = searchText?.Trim();
            if (string.Equals(searchText, _lastSearchString, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _lastSearchString = searchText;
            _searchPopover.Clear();
            _currentSearchResultItems.Clear();
            _searchHighlightIndex = -1;

            if (!_isSearchCachePopulated || string.IsNullOrWhiteSpace(searchText))
            {
                CloseActivePopover();
                return;
            }

            string[] searchTerms = searchText.Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries
            );
            if (searchTerms.Length == 0)
            {
                CloseActivePopover();
                return;
            }

            List<(ScriptableObject refernce, SearchResultMatchInfo match)> results = new();
            foreach (ScriptableObject obj in _allManagedSOsCache)
            {
                if (obj == null)
                {
                    continue;
                }

                SearchResultMatchInfo matchInfo = CheckMatch(obj, searchTerms);
                if (!matchInfo.isMatch)
                {
                    continue;
                }

                results.Add((obj, matchInfo));
                if (results.Count >= MaxSearchResults)
                {
                    break;
                }
            }

            if (results.Count > 0)
            {
                ScrollView scrollView =
                    _searchPopover.Q<ScrollView>("search-scroll")
                    ?? new ScrollView { name = "search-scroll", style = { flexGrow = 1 } };
                VisualElement listContainer =
                    scrollView.Q<VisualElement>("search-list-content")
                    ?? new VisualElement { name = "search-list-content" };
                listContainer.Clear();
                if (scrollView.parent != _searchPopover)
                {
                    _searchPopover.Add(scrollView);
                }

                if (listContainer.parent != scrollView)
                {
                    scrollView.Add(listContainer);
                }

                foreach ((ScriptableObject resultObj, SearchResultMatchInfo resultInfo) in results)
                {
                    List<string> termsMatchingThisObject = resultInfo.AllMatchedTerms.ToList();
                    VisualElement resultItem = new() { name = "result-item", userData = resultObj };
                    resultItem.AddToClassList(SearchResultItemClass);
                    resultItem.AddToClassList("clickable");
                    resultItem.style.flexDirection = FlexDirection.Column;
                    resultItem.style.paddingBottom = new StyleLength(
                        new Length(4, LengthUnit.Pixel)
                    );
                    resultItem.style.paddingLeft = new StyleLength(new Length(4, LengthUnit.Pixel));
                    resultItem.style.paddingRight = new StyleLength(
                        new Length(4, LengthUnit.Pixel)
                    );
                    resultItem.style.paddingTop = new StyleLength(new Length(4, LengthUnit.Pixel));

                    // ReSharper disable once HeapView.CanAvoidClosure
                    resultItem.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (
                            evt.button != 0
                            || resultItem.userData is not ScriptableObject clickedObj
                        )
                        {
                            return;
                        }

                        NavigateToObject(clickedObj);
                        evt.StopPropagation();
                    });
                    resultItem.RegisterCallback<MouseEnterEvent, VisualElement>(
                        (_, context) => context.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f),
                        resultItem
                    );
                    resultItem.RegisterCallback<MouseLeaveEvent, VisualElement>(
                        (_, context) => context.style.backgroundColor = Color.clear,
                        resultItem
                    );

                    VisualElement mainInfoRow = new()
                    {
                        style =
                        {
                            flexDirection = FlexDirection.Row,
                            justifyContent = Justify.SpaceBetween,
                        },
                    };
                    mainInfoRow.AddToClassList("clickable");

                    Label nameLabel = CreateHighlightedLabel(
                        resultObj.name,
                        termsMatchingThisObject,
                        "result-name-label"
                    );
                    nameLabel.style.flexGrow = 1;
                    nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                    nameLabel.style.marginRight = 10;
                    nameLabel.AddToClassList("clickable");

                    Label typeLabel = CreateHighlightedLabel(
                        resultObj.GetType().Name,
                        termsMatchingThisObject,
                        "result-type-label"
                    );
                    typeLabel.style.color = Color.grey;
                    typeLabel.style.fontSize = 10;
                    typeLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                    typeLabel.style.flexShrink = 0;
                    typeLabel.AddToClassList("clickable");

                    mainInfoRow.Add(nameLabel);
                    mainInfoRow.Add(typeLabel);
                    resultItem.Add(mainInfoRow);

                    if (!resultInfo.MatchInPrimaryField)
                    {
                        VisualElement contextContainer = new() { style = { marginTop = 2 } };
                        resultItem.Add(contextContainer);

                        IEnumerable<IGrouping<string, MatchDetail>> reflectedDetails = resultInfo
                            .matchedFields.Where(mf =>
                                mf.fieldName != MatchSource.ObjectName
                                && mf.fieldName != MatchSource.TypeName
                                && mf.fieldName != MatchSource.GUID
                            )
                            .GroupBy(mf => mf.fieldName)
                            .Take(2);

                        foreach (IGrouping<string, MatchDetail> fieldGroup in reflectedDetails)
                        {
                            string fieldName = fieldGroup.Key;
                            string fieldValue = fieldGroup.First().matchedValue;
                            Label contextLabel = CreateHighlightedLabel(
                                $"{fieldName}: {fieldValue}",
                                termsMatchingThisObject,
                                "result-context-label"
                            );
                            contextLabel.style.fontSize = 9;
                            contextLabel.style.color = Color.gray;
                            contextLabel.style.marginLeft = 5;
                            contextContainer.Add(contextLabel);
                        }
                    }

                    listContainer.Add(resultItem);
                    _currentSearchResultItems.Add(resultItem);
                }

                if (_activePopover != _searchPopover)
                {
                    OpenPopover(_searchPopover, _searchField);
                }
            }
            else
            {
                _searchPopover.Clear();
                CloseActivePopover();
            }
        }

        private SearchResultMatchInfo CheckMatch(ScriptableObject obj, string[] lowerSearchTerms)
        {
            SearchResultMatchInfo resultInfo = new();
            if (obj == null || lowerSearchTerms == null || lowerSearchTerms.Length == 0)
            {
                return resultInfo;
            }

            string objectName = obj.name;
            string typeName = obj.GetType().Name;
            string assetPath = AssetDatabase.GetAssetPath(obj);
            string guid = string.IsNullOrWhiteSpace(assetPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(assetPath);

            foreach (string term in lowerSearchTerms)
            {
                bool termMatchedThisLoop = false;
                List<MatchDetail> detailsForThisTerm = new();

                if (objectName.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    detailsForThisTerm.Add(
                        new MatchDetail(term)
                        {
                            fieldName = MatchSource.ObjectName,
                            matchedValue = objectName,
                        }
                    );
                    termMatchedThisLoop = true;
                }
                if (typeName.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    detailsForThisTerm.Add(
                        new MatchDetail(term)
                        {
                            fieldName = MatchSource.TypeName,
                            matchedValue = typeName,
                        }
                    );
                    termMatchedThisLoop = true;
                }
                if (
                    !string.IsNullOrWhiteSpace(guid)
                    && guid.Equals(term, StringComparison.OrdinalIgnoreCase)
                )
                {
                    detailsForThisTerm.Add(
                        new MatchDetail(term) { fieldName = MatchSource.GUID, matchedValue = guid }
                    );
                    termMatchedThisLoop = true;
                }

                if (!termMatchedThisLoop)
                {
                    MatchDetail reflectedMatch = SearchStringProperties(
                        obj,
                        term,
                        0,
                        2,
                        new HashSet<object>()
                    );
                    if (reflectedMatch != null)
                    {
                        reflectedMatch.matchedTerms.Add(term);
                        detailsForThisTerm.Add(reflectedMatch);
                        termMatchedThisLoop = true;
                    }
                }

                if (termMatchedThisLoop)
                {
                    resultInfo.isMatch = true;
                    resultInfo.matchedFields.AddRange(detailsForThisTerm);
                }
            }

            return resultInfo;
        }

        private Label CreateHighlightedLabel(
            string fullText,
            IReadOnlyList<string> termsToHighlight,
            string baseStyleClass = null
        )
        {
            Label label = new();
            if (!string.IsNullOrWhiteSpace(baseStyleClass))
            {
                label.AddToClassList(baseStyleClass);
            }

            label.enableRichText = true;

            if (
                string.IsNullOrWhiteSpace(fullText)
                || termsToHighlight == null
                || !termsToHighlight.Any()
            )
            {
                label.text = fullText;
                return label;
            }

            StringBuilder richText = new();
            int currentIndex = 0;

            List<Tuple<int, int>> matches = termsToHighlight
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .SelectMany(term =>
                {
                    List<Tuple<int, int>> indices = new();
                    int start = 0;
                    while (
                        (start = fullText.IndexOf(term, start, StringComparison.OrdinalIgnoreCase))
                        >= 0
                    )
                    {
                        indices.Add(Tuple.Create(start, term.Length));
                        start += term.Length;
                    }
                    return indices;
                })
                .Where(t => t != null)
                .OrderBy(t => t.Item1)
                .ToList();

            foreach ((int startIndex, int length) in matches)
            {
                if (startIndex < currentIndex)
                {
                    continue;
                }

                richText.Append(
                    EscapeRichText(fullText.Substring(currentIndex, startIndex - currentIndex))
                );
                richText.Append("<color=yellow>");
                richText.Append("<b>");
                richText.Append(EscapeRichText(fullText.Substring(startIndex, length)));
                richText.Append("</b>");
                richText.Append("</color>");
                currentIndex = startIndex + length;
            }

            if (currentIndex < fullText.Length)
            {
                richText.Append(EscapeRichText(fullText.Substring(currentIndex)));
            }

            label.text = richText.ToString();
            return label;
        }

        private string EscapeRichText(string input)
        {
            return string.IsNullOrWhiteSpace(input)
                ? ""
                : input.Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static MatchDetail SearchStringProperties(
            object obj,
            string searchTerm,
            int currentDepth,
            int maxDepth,
            HashSet<object> visited
        )
        {
            if (obj == null || currentDepth > maxDepth)
            {
                return null;
            }

            Type objType = obj.GetType();

            if (
                objType.IsPrimitive
                || objType == typeof(Vector2)
                || objType == typeof(Vector3)
                || objType == typeof(Vector4)
                || objType == typeof(Quaternion)
                || objType == typeof(Color)
                || objType == typeof(Rect)
                || objType == typeof(Bounds)
            )
            {
                return null;
            }

            if (!objType.IsValueType)
            {
                if (!visited.Add(obj))
                {
                    return null;
                }
            }

            try
            {
                FieldInfo[] fields = objType.GetFields(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic
                );
                foreach (FieldInfo field in fields)
                {
                    object fieldValue = field.GetValue(obj);
                    if (fieldValue == null)
                    {
                        continue;
                    }

                    if (field.FieldType == typeof(string))
                    {
                        string stringValue = fieldValue as string;
                        if (
                            !string.IsNullOrWhiteSpace(stringValue)
                            && stringValue.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            return new MatchDetail(searchTerm)
                            {
                                fieldName = field.Name,
                                matchedValue = stringValue,
                            };
                        }
                    }
                    else if (
                        (
                            field.FieldType.IsClass
                            || field.FieldType is { IsValueType: true, IsPrimitive: false }
                        ) && !typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)
                    )
                    {
                        MatchDetail nestedMatch = SearchStringProperties(
                            fieldValue,
                            searchTerm,
                            currentDepth + 1,
                            maxDepth,
                            visited
                        );
                        if (nestedMatch != null)
                        {
                            return nestedMatch;
                        }
                    }
                }
            }
            catch
            {
                // Swallow
            }

            return null;
        }

        private void NavigateToObject(ScriptableObject targetObject)
        {
            if (targetObject == null)
            {
                return;
            }

            Type targetType = targetObject.GetType();

            bool typeChanged = _selectedType != targetType;

            _selectedType = targetType;

            if (typeChanged)
            {
                LoadObjectTypes(_selectedType);
                BuildNamespaceView();
            }

            BuildObjectsView();
            SelectObject(targetObject);
            CloseActivePopover();
            _selectedElement
                ?.schedule.Execute(() =>
                {
                    _objectScrollView?.ScrollTo(_selectedElement);
                })
                .ExecuteLater(10);
        }

        private static VisualElement CreatePopoverBase(string name)
        {
            VisualElement popover = new()
            {
                name = name,
                style =
                {
                    position = Position.Absolute,
                    minWidth = 200,
                    minHeight = 50,
                    backgroundColor = new Color(0.22f, 0.22f, 0.22f, 0.98f),
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderBottomColor = Color.black,
                    borderLeftColor = Color.black,
                    borderRightColor = Color.black,
                    borderTopColor = Color.black,
                    display = DisplayStyle.None,
                    flexDirection = FlexDirection.Column,
                    paddingBottom = 10,
                    paddingLeft = 10,
                    paddingRight = 10,
                    paddingTop = 10,
                },
            };
            popover.AddToClassList("popover");
            return popover;
        }

        private void CloseNestedPopover()
        {
            if (_activeNestedPopover == null)
            {
                return;
            }

            _activeNestedPopover.style.display = DisplayStyle.None;
            _activeNestedPopover = null;
        }

        private void OpenPopover(
            VisualElement popover,
            VisualElement triggerElement,
            object context = null,
            bool isNested = false
        )
        {
            if (!isNested)
            {
                CloseActivePopover();
            }
            else
            {
                CloseNestedPopover();
            }

            if (popover == null || triggerElement == null)
            {
                return;
            }

            if (popover == _searchPopover)
            {
                _lastActiveFocusArea = FocusArea.SearchResultsPopover;
            }
            else if (popover == _typeAddPopover)
            {
                _lastActiveFocusArea = FocusArea.AddTypePopover;
            }
            else
            {
                _lastActiveFocusArea = FocusArea.None;
            }

            _popoverContext = context;
            if (isNested)
            {
                _activeNestedPopover = popover;
            }
            else
            {
                _activePopover = popover;
            }

            triggerElement
                .schedule.Execute(() =>
                {
                    VisualElement currentlyActive = isNested
                        ? _activeNestedPopover
                        : _activePopover;
                    if (currentlyActive != popover)
                    {
                        return;
                    }

                    Rect triggerBounds = triggerElement.worldBound;
                    Vector2 localPos = rootVisualElement.WorldToLocal(triggerBounds.position);
                    popover.style.left = localPos.x;
                    popover.style.top = localPos.y + triggerBounds.height + 2;
                    popover.style.display = DisplayStyle.Flex;

                    if (!isNested)
                    {
                        rootVisualElement
                            .schedule.Execute(() =>
                            {
                                if (_activePopover == popover)
                                {
                                    rootVisualElement.RegisterCallback<PointerDownEvent>(
                                        HandleClickOutsidePopover,
                                        TrickleDown.TrickleDown
                                    );
                                }
                            })
                            .ExecuteLater(10);
                    }
                })
                .ExecuteLater(1);
        }

        private void CloseActivePopover()
        {
            _lastActiveFocusArea = FocusArea.None;
            if (_activePopover == _searchPopover)
            {
                _currentSearchResultItems.Clear();
                _searchHighlightIndex = -1;
                _lastSearchString = null;
            }
            else if (_activePopover == _typeAddPopover)
            {
                _currentTypePopoverItems.Clear();
                _typePopoverHighlightIndex = -1;
                _typeSearchField?.SetValueWithoutNotify("");
            }

            CloseNestedPopover();

            if (_activePopover == null)
            {
                return;
            }

            _activePopover.style.display = DisplayStyle.None;
            rootVisualElement.UnregisterCallback<PointerDownEvent>(
                HandleClickOutsidePopover,
                TrickleDown.TrickleDown
            );

            _activePopover = null;
            _popoverContext = null;
        }

        private void HandleGlobalKeyDown(KeyDownEvent evt)
        {
            Debug.Log($"GlobalKeyDown: Key={evt.keyCode}, FocusArea={_lastActiveFocusArea}");

            // First, check if an overlay popover wants to handle the key (e.g., Escape to close)
            if (_activePopover != null && _activePopover.style.display == DisplayStyle.Flex)
            {
                switch (evt.keyCode)
                {
                    case KeyCode.Escape:
                        // Debug.Log("Global Escape: Closing active popover.");
                        CloseActivePopover();
                        evt.PreventDefault();
                        evt.StopPropagation();
                        return; // Handled

                    case KeyCode.DownArrow:
                    case KeyCode.UpArrow:
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        // Route to specific popover handlers if they are active
                        if (_lastActiveFocusArea == FocusArea.SearchResultsPopover)
                        {
                            _lastEnterPressed = Time.realtimeSinceStartup;
                            HandleSearchKeyDown(evt); // Use existing handler logic
                            return; // Handled by popover
                        }
                        else if (_lastActiveFocusArea == FocusArea.AddTypePopover)
                        {
                            _lastEnterPressed = Time.realtimeSinceStartup;
                            HandleTypePopoverKeyDown(evt); // Use existing handler logic
                            return; // Handled by popover
                        }
                        // Let Enter potentially fall through if popover doesn't handle it (e.g., settings)
                        // Let Arrows fall through if popover doesn't handle it
                        break; // Break switch, don't return yet for Enter maybe
                }
                // If Escape wasn't pressed, and it wasn't search/type popover nav,
                // let the key potentially be handled by controls within other popovers (e.g., text field in Rename)
                // OR stop propagation if we don't want main lists navigating while popover open. Let's stop it.
                if (evt.keyCode == KeyCode.DownArrow || evt.keyCode == KeyCode.UpArrow)
                {
                    // Prevent arrows scrolling main lists when *any* popover is open
                    evt.PreventDefault();
                    evt.StopPropagation();
                    return;
                }
            }

            // If no popover handled it, check focus area for main list navigation
            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                case KeyCode.UpArrow:
                    int direction = (evt.keyCode == KeyCode.DownArrow) ? 1 : -1;
                    bool navigationHandled = false;

                    switch (_lastActiveFocusArea)
                    {
                        case FocusArea.TypeList:
                            navigationHandled = NavigateTypeList(direction);
                            break;
                    }

                    if (navigationHandled)
                    {
                        evt.PreventDefault();
                        evt.StopPropagation();
                    }
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    bool enterHandled = false;
                    switch (_lastActiveFocusArea)
                    {
                        case FocusArea.TypeList:
                            _lastEnterPressed = Time.realtimeSinceStartup;
                            // Handle Enter on Type item (Select Type)
                            enterHandled = HandleEnterOnTypeList();
                            break;
                    }
                    if (enterHandled)
                    {
                        evt.PreventDefault();
                        evt.StopPropagation();
                    }
                    break;

                // Let other keys fall through
            }
        }

        private bool HandleEnterOnTypeList()
        {
            if (_typeHighlightIndex < 0 || _typeHighlightIndex >= _currentNavigableTypeItems.Count)
                return false;
            VisualElement typeElement = _currentTypePopoverItems[_typeHighlightIndex];
            if (typeElement?.userData is Type selectedType)
            {
                // Trigger the same action as clicking the type label
                Debug.Log($"Enter on Type: {selectedType.Name}");
                // --- Copied from type label click handler ---
                if (_selectedTypeElement != null)
                    _selectedTypeElement.RemoveFromClassList("selected");
                _selectedType = selectedType;
                _selectedTypeElement = typeElement; // This might be problematic if element is recreated
                _selectedTypeElement.AddToClassList("selected");
                SaveNamespaceAndTypeSelectionState(
                    GetNamespaceKey(_selectedType),
                    _selectedType.Name
                );
                LoadObjectTypes(selectedType);
                ScriptableObject objectToSelect = DetermineObjectToAutoSelect();
                BuildObjectsView();
                SelectObject(objectToSelect);
                // --- End Copy ---
                return true; // Handled Enter
            }
            return false;
        }

        private bool NavigateTypeList(int direction)
        {
            // Requires knowing which types are *currently visible* in the specific expanded namespace
            // And mapping them back to System.Type for selection. This is complex.
            // Need _currentNavigableTypeItems populated correctly when focus is set.

            // --- Repopulate Navigable Type Items ---
            // This needs to be done when focus *enters* this state, potentially triggered by click/expand
            PopulateNavigableTypeItems(); // Create this helper

            if (_currentNavigableTypeItems.Count == 0)
                return false;

            _typeHighlightIndex += direction;
            // Wrap
            if (_typeHighlightIndex >= _currentNavigableTypeItems.Count)
                _typeHighlightIndex = 0;
            else if (_typeHighlightIndex < 0)
                _typeHighlightIndex = _currentNavigableTypeItems.Count - 1;

            // Need the correct scrollview for types (likely the Namespace scrollview)
            ScrollView nsScrollView = _namespaceColumnElement?.Q<ScrollView>();
            VisualElement typeElement = _currentNavigableTypeItems[_typeHighlightIndex];
            SelectTypeIndex(_typeHighlightIndex, typeElement);
            nsScrollView?.ScrollTo(typeElement);
            return true;
        }

        private void PopulateNavigableTypeItems()
        {
            _currentNavigableTypeItems.Clear();
            if (_typeListParentNamespaceHeader == null)
                return; // Need context

            var typesContainer = _typeListParentNamespaceHeader
                .IterateChildrenRecursively()
                .FirstOrDefault(child =>
                    child.name.Contains("types-container", StringComparison.OrdinalIgnoreCase)
                );
            if (typesContainer == null || typesContainer.style.display == DisplayStyle.None)
                return; // Must be expanded

            _currentNavigableTypeItems.AddRange(typesContainer.Children());
        }

        private static void UpdateListHighlight(
            List<VisualElement> items,
            int index,
            ScrollView scrollView,
            string highlightClass
        )
        {
            if (items == null)
                return;
            VisualElement elementToScrollTo = null;
            for (int i = 0; i < items.Count; ++i)
            {
                if (items[i] == null)
                    continue;
                bool shouldHighlight = (i == index);
                items[i].EnableInClassList(highlightClass, shouldHighlight);
                if (shouldHighlight)
                {
                    Debug.Log($"SELECTING {i} TYPE INDEX");
                }
                if (shouldHighlight)
                    elementToScrollTo = items[i];
            }
            // Schedule scroll
            if (elementToScrollTo != null && scrollView != null)
            {
                elementToScrollTo
                    .schedule.Execute(() =>
                    {
                        if (elementToScrollTo.panel != null)
                            scrollView.ScrollTo(elementToScrollTo);
                    })
                    .ExecuteLater(1);
            }
        }

        private void HandleClickOutsidePopover(PointerDownEvent evt)
        {
            VisualElement target = evt.target as VisualElement;
            if (target == _addTypeButton)
            {
                _lastAddTypeClicked = Time.realtimeSinceStartup;
            }

            bool clickInsideNested = false;
            bool clickInsideMain = false;

            if (
                _activeNestedPopover != null
                && _activeNestedPopover.style.display == DisplayStyle.Flex
            )
            {
                VisualElement current = target;
                while (current != null)
                {
                    if (current == _activeNestedPopover)
                    {
                        clickInsideNested = true;
                        break;
                    }
                    current = current.parent;
                }
            }

            if (clickInsideNested)
            {
                return;
            }

            if (_activePopover != null && _activePopover.style.display == DisplayStyle.Flex)
            {
                VisualElement current = target;
                while (current != null)
                {
                    if (current == _activePopover)
                    {
                        clickInsideMain = true;
                        break;
                    }
                    current = current.parent;
                }
            }

            if (
                _activeNestedPopover != null
                && _activeNestedPopover.style.display == DisplayStyle.Flex
            )
            {
                if (clickInsideMain)
                {
                    CloseNestedPopover();
                }
                else
                {
                    CloseActivePopover();
                }
            }
            else if (_activePopover != null && _activePopover.style.display == DisplayStyle.Flex)
            {
                if (!clickInsideMain)
                {
                    CloseActivePopover();
                }
            }
            else
            {
                rootVisualElement.UnregisterCallback<PointerDownEvent>(
                    HandleClickOutsidePopover,
                    TrickleDown.TrickleDown
                );
            }
        }

        private void BuildSettingsPopoverContent()
        {
            _settingsPopover.Clear();

            _settingsPopover.Add(
                new Label("Settings")
                {
                    style =
                    {
                        unityFontStyleAndWeight = FontStyle.Bold,
                        marginBottom = 10,
                        alignSelf = Align.Center,
                    },
                }
            );

            Toggle prefsToggle = new("Persist State in Settings Asset:")
            {
                value = _settings.persistStateInSettingsAsset,
                tooltip = "...",
            };
            prefsToggle.RegisterValueChangedCallback(evt =>
            {
                if (_settings == null)
                {
                    return;
                }

                bool newModeIsSettingsAsset = evt.newValue;
                bool previousModeWasSettingsAsset = _settings.persistStateInSettingsAsset;
                if (previousModeWasSettingsAsset == newModeIsSettingsAsset)
                {
                    return;
                }

                _settings.persistStateInSettingsAsset = newModeIsSettingsAsset;
                MigratePersistenceState(migrateToSettingsAsset: newModeIsSettingsAsset);
                MarkSettingsDirty();
                AssetDatabase.SaveAssets();
                if (!newModeIsSettingsAsset)
                {
                    SaveUserStateToFile();
                }
            });
            _settingsPopover.Add(prefsToggle);

            VisualElement dataFolderContainer = new()
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginTop = 10,
                },
            };
            dataFolderContainer.Add(
                new Label("Data Folder:") { style = { width = 80, flexShrink = 0 } }
            );
            TextField dataFolderPathDisplay = new()
            {
                value = _settings.DataFolderPath,
                isReadOnly = true,
                name = "data-folder-display",
                style =
                {
                    flexGrow = 1,
                    marginLeft = 5,
                    marginRight = 5,
                },
            };
            dataFolderContainer.Add(dataFolderPathDisplay);
            Button selectFolderButton = new(() => SelectDataFolderForPopover(dataFolderPathDisplay))
            {
                text = "Select...",
                style = { flexShrink = 0 },
            };
            dataFolderContainer.Add(selectFolderButton);
            _settingsPopover.Add(dataFolderContainer);

            VisualElement buttonContainer = new()
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.FlexEnd,
                    marginTop = 15,
                },
            };
            Button closeButton = new(CloseActivePopover) { text = "Close" };
            buttonContainer.Add(closeButton);
            _settingsPopover.Add(buttonContainer);
        }

        private void SelectDataFolderForPopover(TextField displayField)
        {
            if (_settings == null)
            {
                Debug.LogError("Cannot select data folder: Settings object not loaded.");
                return;
            }
            if (displayField == null)
            {
                Debug.LogError("Cannot select data folder: Display field reference is null.");
                return;
            }

            string currentRelativePath = _settings.DataFolderPath;
            string projectRoot = Path.GetFullPath(Directory.GetCurrentDirectory()).SanitizePath();
            string startDir = Application.dataPath;

            if (!string.IsNullOrWhiteSpace(currentRelativePath))
            {
                try
                {
                    string currentFullPath = Path.GetFullPath(
                            Path.Combine(projectRoot, currentRelativePath)
                        )
                        .SanitizePath();
                    if (Directory.Exists(currentFullPath))
                    {
                        startDir = currentFullPath;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"Could not resolve current DataFolderPath '{currentRelativePath}': {ex.Message}. Starting selection in Assets."
                    );
                }
            }

            string selectedAbsolutePath = EditorUtility.OpenFolderPanel(
                title: "Select Data Folder (Must be inside Assets)",
                folder: startDir,
                defaultName: ""
            );

            if (string.IsNullOrWhiteSpace(selectedAbsolutePath))
            {
                return;
            }

            selectedAbsolutePath = Path.GetFullPath(selectedAbsolutePath).SanitizePath();

            string projectAssetsPath = Path.GetFullPath(Application.dataPath).SanitizePath();

            if (
                !selectedAbsolutePath.StartsWith(
                    projectAssetsPath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                Debug.LogError("Selected folder must be inside the project's Assets folder.");
                EditorUtility.DisplayDialog(
                    "Invalid Folder",
                    "The selected folder must be inside the project's 'Assets' directory.",
                    "OK"
                );
                return;
            }

            string relativePath;
            if (selectedAbsolutePath.Equals(projectAssetsPath, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = "Assets";
            }
            else
            {
                relativePath = "Assets" + selectedAbsolutePath.Substring(projectAssetsPath.Length);
                relativePath = relativePath.Replace("//", "/");
            }

            if (_settings.DataFolderPath == relativePath)
            {
                return;
            }

            Debug.Log(
                $"Updating Data Folder from '{_settings.DataFolderPath}' to '{relativePath}'"
            );
            _settings._dataFolderPath = relativePath;

            MarkSettingsDirty();

            AssetDatabase.SaveAssets();
            displayField.value = _settings.DataFolderPath;
        }

        private void OpenRenamePopover(ScriptableObject dataObject)
        {
            if (dataObject == null)
            {
                return;
            }

            string currentPath = AssetDatabase.GetAssetPath(dataObject);
            if (string.IsNullOrWhiteSpace(currentPath))
            {
                return;
            }

            BuildRenamePopoverContent(currentPath, dataObject.name);
            VisualElement triggerRow = _objectVisualElementMap.GetValueOrDefault(dataObject);
            if (triggerRow != null)
            {
                OpenPopover(_renamePopover, triggerRow, currentPath);
            }
        }

        private void BuildRenamePopoverContent(string originalPath, string originalName)
        {
            _renamePopover.Clear();
            _renamePopover.userData = originalPath;

            _renamePopover.Add(
                new Label("Enter new name (without extension):") { style = { marginBottom = 5 } }
            );
            TextField nameTextField = new()
            {
                value = Path.GetFileNameWithoutExtension(originalName),
                name = "rename-textfield",
                style = { marginBottom = 5 },
            };
            nameTextField.schedule.Execute(() => nameTextField.SelectAll()).ExecuteLater(50);
            _renamePopover.Add(nameTextField);
            Label errorLabel = new()
            {
                name = "error-label",
                style =
                {
                    color = Color.red,
                    height = 18,
                    display = DisplayStyle.None,
                },
            };
            _renamePopover.Add(errorLabel);
            VisualElement buttonContainer = new()
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.FlexEnd,
                    marginTop = 5,
                },
            };
            Button cancelButton = new(CloseActivePopover)
            {
                text = "Cancel",
                style = { marginRight = 5 },
            };
            Button renameButton = new(() => HandleRenameConfirmed(nameTextField, errorLabel))
            {
                text = "Rename",
            };
            buttonContainer.Add(cancelButton);
            buttonContainer.Add(renameButton);
            _renamePopover.Add(buttonContainer);
        }

        private void HandleRenameConfirmed(TextField nameField, Label errorLabel)
        {
            errorLabel.style.display = DisplayStyle.None;
            string originalPath = _popoverContext as string;
            string newName = nameField.value;

            if (
                string.IsNullOrWhiteSpace(originalPath)
                || string.IsNullOrWhiteSpace(newName)
                || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            )
            {
                errorLabel.text = "Invalid name.";
                errorLabel.style.display = DisplayStyle.Flex;
                return;
            }
            if (
                newName.Equals(
                    Path.GetFileNameWithoutExtension(originalPath),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                errorLabel.text = "Name is unchanged.";
                errorLabel.style.display = DisplayStyle.Flex;
                return;
            }

            string directory = Path.GetDirectoryName(originalPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                errorLabel.text =
                    $"Failed to find directory of original asset path '{originalPath}'.";
                errorLabel.style.display = DisplayStyle.Flex;
                return;
            }
            string newPath = Path.Combine(directory, newName + Path.GetExtension(originalPath))
                .SanitizePath();
            string validationError = AssetDatabase.ValidateMoveAsset(originalPath, newPath);

            if (!string.IsNullOrWhiteSpace(validationError))
            {
                errorLabel.text = $"Invalid: {validationError}";
                errorLabel.style.display = DisplayStyle.Flex;
                return;
            }

            string error = AssetDatabase.RenameAsset(originalPath, newName);
            if (string.IsNullOrWhiteSpace(error))
            {
                Debug.Log($"Asset renamed successfully to: {newName}");
                CloseActivePopover();
                ScheduleRefresh();
            }
            else
            {
                Debug.LogError($"Asset rename failed: {error}");
                errorLabel.text = $"Failed: {error}";
                errorLabel.style.display = DisplayStyle.Flex;
            }
        }

        private void OpenConfirmDeletePopover(ScriptableObject dataObject)
        {
            if (dataObject == null)
            {
                return;
            }

            BuildConfirmDeletePopoverContent(dataObject);
            VisualElement triggerRow = _objectVisualElementMap.GetValueOrDefault(dataObject);
            if (triggerRow != null)
            {
                OpenPopover(_confirmDeletePopover, triggerRow, dataObject);
            }
        }

        private void BuildConfirmDeletePopoverContent(ScriptableObject objectToDelete)
        {
            _confirmDeletePopover.Clear();
            _confirmDeletePopover.userData = objectToDelete;

            _confirmDeletePopover.Add(
                new Label($"Delete '{objectToDelete.name}'?\nThis cannot be undone.")
                {
                    style = { whiteSpace = WhiteSpace.Normal, marginBottom = 15 },
                }
            );
            VisualElement buttonContainer = new()
            {
                style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd },
            };
            Button cancelButton = new(CloseActivePopover)
            {
                text = "Cancel",
                style = { marginRight = 5 },
            };
            Button deleteButton = new(HandleDeleteConfirmed) { text = "Delete" };
            deleteButton.AddToClassList("delete-button");
            buttonContainer.Add(cancelButton);
            buttonContainer.Add(deleteButton);
            _confirmDeletePopover.Add(buttonContainer);
        }

        private void HandleDeleteConfirmed()
        {
            ScriptableObject objectToDelete = _popoverContext as ScriptableObject;
            CloseActivePopover();

            if (objectToDelete == null)
            {
                Debug.LogError("Delete failed: context object lost.");
                return;
            }

            string path = AssetDatabase.GetAssetPath(objectToDelete);
            if (string.IsNullOrWhiteSpace(path))
            {
                Debug.LogError($"Delete failed: path not found for {objectToDelete.name}");
                return;
            }

            _selectedObjects.Remove(objectToDelete);
            _objectVisualElementMap.Remove(objectToDelete, out VisualElement visualElement);
            bool deleted = AssetDatabase.DeleteAsset(path);
            if (deleted)
            {
                Debug.Log($"Asset '{path}' deleted successfully.");
                AssetDatabase.Refresh();
                visualElement?.RemoveFromHierarchy();
                if (_selectedObject == objectToDelete)
                {
                    SelectObject(null);
                }
            }
            else
            {
                Debug.LogError($"Failed delete: {path}");
                ScheduleRefresh();
            }
        }

        private void TogglePopover(VisualElement popover, VisualElement triggerElement)
        {
            if (_activePopover == popover && popover.style.display == DisplayStyle.Flex)
            {
                CloseActivePopover();
            }
            else
            {
                if (popover == _settingsPopover)
                {
                    BuildSettingsPopoverContent();
                }
                else if (popover == _typeAddPopover)
                {
                    if (Time.realtimeSinceStartup <= _lastAddTypeClicked + 0.5f)
                    {
                        return;
                    }
                    BuildTypeAddList();
                }

                OpenPopover(popover, triggerElement);
            }
        }

        private VisualElement CreateNamespaceColumn()
        {
            VisualElement namespaceColumn = new()
            {
                name = "namespace-column",
                style =
                {
                    borderRightWidth = 1,
                    borderRightColor = Color.gray,
                    height = Length.Percent(100),
                },
            };

            VisualElement nsHeader = new();
            nsHeader.Add(
                new Label("Namespaces")
                {
                    style = { unityFontStyleAndWeight = FontStyle.Bold, paddingLeft = 2 },
                }
            );
            nsHeader.AddToClassList(NamespaceGroupHeaderClass);
            _addTypeButton = new Button(() =>
            {
                if (Time.realtimeSinceStartup < _lastEnterPressed + 0.5f)
                {
                    return;
                }
                TogglePopover(_typeAddPopover, _addTypeButton);
            })
            {
                text = "+",
                tooltip = "Manage Visible Types",
            };
            _addTypeButton.AddToClassList("create-button");
            _addTypeButton.AddToClassList("icon-button");
            _addTypeButton.AddToClassList("clickable");
            nsHeader.Add(_addTypeButton);
            namespaceColumn.Add(nsHeader);

            ScrollView namespaceScrollView = new(ScrollViewMode.Vertical)
            {
                name = "namespace-scrollview",
            };
            namespaceScrollView.AddToClassList("namespace-scrollview");
            _namespaceListContainer = new VisualElement { name = "namespace-list" };
            namespaceScrollView.Add(_namespaceListContainer);
            namespaceColumn.Add(namespaceScrollView);
            return namespaceColumn;
        }

        private void BuildTypeAddList(string filter = null)
        {
            if (string.Equals("Search...", filter, StringComparison.Ordinal))
            {
                filter = null;
            }

            if (_lastTypeAddSearchTerm == filter && _currentTypePopoverItems.Any())
            {
                return;
            }

            try
            {
                _currentTypePopoverItems.Clear();
                _typePopoverHighlightIndex = -1;

                if (_typePopoverListContainer == null)
                {
                    return;
                }

                _typePopoverListContainer.Clear();

                List<string> searchTerms = string.IsNullOrWhiteSpace(filter)
                    ? new List<string>()
                    : filter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                bool isFiltering = searchTerms.Count > 0;

                List<Type> allObjectTypes = LoadRelevantScriptableObjectTypes();
                List<string> managedTypeFullNames = GetManagedTypeNames();
                HashSet<string> managedSet = new(managedTypeFullNames);

                IOrderedEnumerable<IGrouping<string, Type>> groupedTypes = allObjectTypes
                    .GroupBy(GetNamespaceKey)
                    .OrderBy(grouping => grouping.Key);

                bool foundMatches = false;
                foreach (IGrouping<string, Type> group in groupedTypes)
                {
                    string namespaceKey = group.Key;
                    List<Type> addableTypes = new();
                    List<VisualElement> typesToShowInGroup = new();

                    bool namespaceMatchesAll =
                        isFiltering
                        && searchTerms.All(term =>
                            namespaceKey.Contains(term, StringComparison.OrdinalIgnoreCase)
                        );

                    foreach (Type type in group.OrderBy(t => t.Name))
                    {
                        string typeName = type.Name;
                        bool typeMatchesSearch =
                            !isFiltering
                            || namespaceMatchesAll
                            || searchTerms.All(term =>
                                typeName.Contains(term, StringComparison.OrdinalIgnoreCase)
                                || namespaceKey.Contains(term, StringComparison.OrdinalIgnoreCase)
                            );

                        if (!typeMatchesSearch)
                        {
                            continue;
                        }

                        bool isManaged = managedSet.Contains(type.FullName);
                        Label typeLabel = CreateHighlightedLabel(
                            $"  {type.Name}",
                            searchTerms,
                            PopoverListItemClassName
                        );
                        typeLabel.style.paddingTop = 1;
                        typeLabel.style.paddingBottom = 1;
                        typeLabel.style.marginLeft = 10;
                        typeLabel.AddToClassList(PopoverListItemClassName);

                        if (isManaged)
                        {
                            typeLabel.SetEnabled(false);
                            typeLabel.AddToClassList(PopoverListItemDisabledClassName);
                        }
                        else
                        {
                            typeLabel.RegisterCallback<PointerDownEvent, Type>(
                                HandleTypeSelectionFromPopover,
                                type
                            );
                            typeLabel.RegisterCallback<MouseEnterEvent, Label>(
                                (_, context) =>
                                    context.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f),
                                typeLabel
                            );
                            typeLabel.RegisterCallback<MouseLeaveEvent, Label>(
                                (_, context) => context.style.backgroundColor = Color.clear,
                                typeLabel
                            );
                        }

                        addableTypes.Add(type);
                        typesToShowInGroup.Add(typeLabel);
                    }

                    if (typesToShowInGroup.Count > 0)
                    {
                        foundMatches = true;

                        VisualElement namespaceGroupContainer = new()
                        {
                            name = $"ns-group-container-{group.Key}",
                        };
                        VisualElement header = new() { name = $"ns-header-{group.Key}" };
                        header.AddToClassList(PopoverNamespaceHeaderClassName);

                        bool startCollapsed = !isFiltering;
                        Label indicator = new(startCollapsed ? ArrowCollapsed : ArrowExpanded)
                        {
                            name = $"ns-indicator-{group.Key}",
                        };
                        indicator.AddToClassList(PopoverNamespaceIndicatorClassName);
                        indicator.AddToClassList("clickable");

                        Label namespaceLabel = CreateHighlightedLabel(
                            group.Key,
                            searchTerms,
                            PopoverListNamespaceClassName
                        );
                        namespaceLabel.AddToClassList(PopoverListNamespaceClassName);
                        namespaceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        namespaceLabel.style.flexGrow = 1;

                        Dictionary<string, object> clickContext = new()
                        {
                            ["NamespaceKey"] = group.Key,
                            ["AddableTypes"] = addableTypes,
                            ["ExpandNamespace"] = (Action<PointerDownEvent>)ExpandNamespace,
                        };
                        header.userData = clickContext;

                        if (typesToShowInGroup.Count > 1)
                        {
                            namespaceLabel.AddToClassList(
                                "type-selection-list-namespace--not-empty"
                            );
                            namespaceLabel.AddToClassList("clickable");
                            namespaceLabel.RegisterCallback<MouseEnterEvent, Label>(
                                (_, context) =>
                                    context.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f),
                                namespaceLabel
                            );
                            namespaceLabel.RegisterCallback<MouseLeaveEvent, Label>(
                                (_, context) => context.style.backgroundColor = Color.clear,
                                namespaceLabel
                            );

                            // ReSharper disable once HeapView.CanAvoidClosure
                            namespaceLabel.RegisterCallback<PointerDownEvent>(evt =>
                            {
                                if (evt.button != 0)
                                {
                                    return;
                                }

                                string clickedNamespace = clickContext["NamespaceKey"] as string;
                                List<Type> typesToAdd = clickContext["AddableTypes"] as List<Type>;
                                ;
                                int countToAdd = typesToAdd?.Count ?? 0;

                                if (countToAdd == 0)
                                {
                                    return;
                                }

                                BuildConfirmNamespaceAddPopoverContent(
                                    clickedNamespace,
                                    typesToAdd
                                );
                                OpenPopover(
                                    _confirmNamespaceAddPopover,
                                    namespaceLabel,
                                    isNested: true
                                );
                                evt.StopPropagation();
                            });
                        }
                        else
                        {
                            namespaceLabel.AddToClassList("type-selection-list-namespace--empty");
                        }

                        header.Add(indicator);
                        header.Add(namespaceLabel);

                        VisualElement typesSubContainer = new()
                        {
                            name = $"types-subcontainer-{group.Key}",
                            style =
                            {
                                marginLeft = 15,
                                display = startCollapsed ? DisplayStyle.None : DisplayStyle.Flex,
                            },
                        };

                        foreach (VisualElement typeVisualElement in typesToShowInGroup)
                        {
                            typesSubContainer.Add(typeVisualElement);
                        }

                        namespaceGroupContainer.Add(header);
                        namespaceGroupContainer.Add(typesSubContainer);

                        // ReSharper disable once HeapView.CanAvoidClosure
                        indicator.RegisterCallback<PointerDownEvent>(ExpandNamespace);

                        void ExpandNamespace(PointerDownEvent evt)
                        {
                            if (evt != null && evt.button != 0)
                            {
                                return;
                            }

                            Label currentIndicator = header.Q<Label>(
                                className: PopoverNamespaceIndicatorClassName
                            );
                            VisualElement currentTypesContainer = header.parent.Q<VisualElement>(
                                $"types-subcontainer-{group.Key}"
                            );
                            if (currentIndicator != null && currentTypesContainer != null)
                            {
                                bool nowCollapsed =
                                    currentTypesContainer.style.display == DisplayStyle.None;
                                currentTypesContainer.style.display = nowCollapsed
                                    ? DisplayStyle.Flex
                                    : DisplayStyle.None;
                                currentIndicator.text = nowCollapsed
                                    ? ArrowExpanded
                                    : ArrowCollapsed;
                            }

                            evt?.StopPropagation();
                        }

                        _currentTypePopoverItems.Add(header);
                        _typePopoverListContainer.Add(namespaceGroupContainer);
                    }
                }

                if (isFiltering && !foundMatches)
                {
                    _typePopoverListContainer.Add(
                        new Label("No matching types found.")
                        {
                            style =
                            {
                                color = Color.grey,
                                paddingBottom = 10,
                                paddingTop = 10,
                                paddingLeft = 10,
                                paddingRight = 10,
                                unityTextAlign = TextAnchor.MiddleCenter,
                            },
                        }
                    );
                }
            }
            finally
            {
                _lastTypeAddSearchTerm = filter;
            }
        }

        private void HandleTypePopoverKeyDown(KeyDownEvent evt)
        {
            Debug.Log("TYPE POPOVER KEY DOWN");
            if (
                _activePopover != _typeAddPopover
                || _typeAddPopover.style.display == DisplayStyle.None
                || _currentTypePopoverItems.Count == 0
            )
            {
                return;
            }

            bool highlightChanged = false;

            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                {
                    _typePopoverHighlightIndex++;
                    if (_typePopoverHighlightIndex >= _currentTypePopoverItems.Count)
                    {
                        _typePopoverHighlightIndex = 0;
                    }

                    highlightChanged = true;
                    break;
                }
                case KeyCode.UpArrow:
                {
                    _typePopoverHighlightIndex--;
                    if (_typePopoverHighlightIndex < 0)
                    {
                        _typePopoverHighlightIndex = _currentTypePopoverItems.Count - 1;
                    }

                    highlightChanged = true;
                    break;
                }
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                {
                    if (
                        _typePopoverHighlightIndex >= 0
                        && _typePopoverHighlightIndex < _currentTypePopoverItems.Count
                    )
                    {
                        VisualElement selectedElement = _currentTypePopoverItems[
                            _typePopoverHighlightIndex
                        ];
                        HandleEnterOnPopoverItem(selectedElement);
                        evt.PreventDefault();
                        evt.StopPropagation();
                    }

                    break;
                }
                case KeyCode.Escape:
                {
                    CloseActivePopover();
                    evt.PreventDefault();
                    evt.StopPropagation();
                    break;
                }
                default:
                {
                    return;
                }
            }

            if (highlightChanged)
            {
                UpdateTypePopoverHighlight();
                evt.PreventDefault();
                evt.StopPropagation();
            }
        }

        private void HandleEnterOnPopoverItem(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            if (element.userData is Type selectedType)
            {
                HandleTypeSelectionFromPopover(null, selectedType);
            }
            else if (
                element.ClassListContains(PopoverNamespaceHeaderClassName)
                && element.userData != null
            )
            {
                try
                {
                    Dictionary<string, object> context =
                        element.userData as Dictionary<string, object>;
                    string nsKey = context.GetValueOrDefault("NamespaceKey") as string;
                    List<Type> addableTypes =
                        context.GetValueOrDefault("AddableTypes") as List<Type>;
                    int addableCount = addableTypes?.Count ?? 0;

                    VisualElement parentGroup = element.parent;
                    VisualElement typesSubContainer = parentGroup?.Q<VisualElement>(
                        $"types-subcontainer-{nsKey}"
                    );
                    Label indicator = element.Q<Label>(
                        className: PopoverNamespaceIndicatorClassName
                    );

                    if (typesSubContainer == null || indicator == null)
                    {
                        return;
                    }

                    bool isCollapsed = typesSubContainer.style.display == DisplayStyle.None;

                    if (isCollapsed)
                    {
                        Action<PointerDownEvent> explode =
                            context.GetValueOrDefault("ExpandNamespace")
                            as Action<PointerDownEvent>;
                        explode?.Invoke(null);
                    }
                    else
                    {
                        if (addableCount > 0)
                        {
                            string confirmationMessage =
                                $"Add {addableCount} type{(addableCount > 1 ? "s" : "")} from namespace '{nsKey}'?";

                            BuildConfirmNamespaceAddPopoverContent(nsKey, addableTypes);
                            OpenPopover(_confirmNamespaceAddPopover, element, isNested: true);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error processing Enter on namespace header: {e}");
                }
            }
        }

        private void UpdateTypePopoverHighlight()
        {
            if (_currentTypePopoverItems == null || _typeAddPopover == null)
            {
                return;
            }

            ScrollView scrollView = _typeAddPopover.Q<ScrollView>("search-scroll");
            for (int i = 0; i < _currentTypePopoverItems.Count; i++)
            {
                VisualElement item = _currentTypePopoverItems[i];
                if (item == null)
                {
                    continue;
                }

                if (i == _typePopoverHighlightIndex)
                {
                    item.AddToClassList(PopoverHighlightClass);
                    scrollView?.ScrollTo(item);
                }
                else
                {
                    item.RemoveFromClassList(PopoverHighlightClass);
                }
            }
        }

        private void HandleTypeSelectionFromPopover(PointerDownEvent evt, Type selectedType)
        {
            if (selectedType != null)
            {
                List<string> currentManagedList = GetManagedTypeNames();
                if (!currentManagedList.Contains(selectedType.FullName))
                {
                    currentManagedList.Add(selectedType.FullName);
                    if (_settings.persistStateInSettingsAsset)
                    {
                        MarkSettingsDirty();
                        AssetDatabase.SaveAssets();
                    }
                    else
                    {
                        MarkUserStateDirty();
                    }

                    LoadScriptableObjectTypes();
                    BuildNamespaceView();
                }
            }
            CloseActivePopover();
            evt.StopPropagation();
        }

        private VisualElement CreateObjectColumn()
        {
            VisualElement objectColumn = new()
            {
                name = "object-column",
                style =
                {
                    // TODO: MIGRATE ALL STYLES TO USS + SPLIT STYLE SHEETS
                    borderRightWidth = 1,
                    borderRightColor = Color.gray,
                    flexDirection = FlexDirection.Column,
                    height = Length.Percent(100),
                },
            };

            VisualElement objectHeader = new() { name = "object-header" };
            objectHeader.AddToClassList("object-header");

            objectHeader.Add(new Label("Objects"));
            Button createButton = new(CreateNewObject)
            {
                text = "+",
                tooltip = "Create New Object",
                name = "create-object-button",
            };
            createButton.AddToClassList("create-button");
            createButton.AddToClassList("icon-button");
            createButton.AddToClassList("clickable");
            objectHeader.Add(createButton);
            objectColumn.Add(objectHeader);
            _objectScrollView = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "object-scrollview",
            };
            _objectScrollView.AddToClassList("object-scrollview");
            _objectListContainer = new VisualElement { name = "object-list" };
            _objectScrollView.Add(_objectListContainer);
            objectColumn.Add(_objectScrollView);
            return objectColumn;
        }

        private VisualElement CreateInspectorColumn()
        {
            VisualElement inspectorColumn = new()
            {
                name = "inspector-column",
                style = { flexGrow = 1, height = Length.Percent(100) },
            };
            _inspectorScrollView = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "inspector-scrollview",
                style = { flexGrow = 1 },
            };
            _inspectorContainer = new VisualElement { name = "inspector-content" };
            _inspectorScrollView.Add(_inspectorContainer);
            inspectorColumn.Add(_inspectorScrollView);
            return inspectorColumn;
        }

        private void CreateNewObject()
        {
            if (_selectedType == null)
            {
                EditorUtility.DisplayDialog(
                    "Cannot Create Object",
                    "Please select a Type in the first column before creating an object.",
                    "OK"
                );
                return;
            }

            if (_settings == null || string.IsNullOrWhiteSpace(_settings.DataFolderPath))
            {
                EditorUtility.DisplayDialog(
                    "Cannot Create Object",
                    "Data Folder Path is not set correctly in Settings.",
                    "OK"
                );
                return;
            }

            string targetDirectory = Path.Combine(
                Directory.GetCurrentDirectory(),
                _settings.DataFolderPath
            );
            targetDirectory = Path.GetFullPath(targetDirectory).SanitizePath();

            string projectAssetsPath = Path.GetFullPath(Application.dataPath).SanitizePath();
            if (!targetDirectory.StartsWith(projectAssetsPath, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog(
                    "Invalid Data Folder",
                    $"The configured Data Folder ('{_settings.DataFolderPath}') is not inside the project's Assets folder.",
                    "OK"
                );
                return;
            }

            try
            {
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(
                    "Error",
                    $"Could not create data directory '{_settings.DataFolderPath}': {e.Message}",
                    "OK"
                );
                return;
            }

            ScriptableObject instance = CreateInstance(_selectedType);
            if (instance == null)
            {
                EditorUtility.DisplayDialog(
                    "Error",
                    $"Failed to create instance of type '{_selectedType.Name}'.",
                    "OK"
                );
                return;
            }

            string baseAssetName = $"New {_selectedType.Name}.asset";
            string proposedPath = Path.Combine(_settings.DataFolderPath, baseAssetName)
                .SanitizePath();
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(proposedPath);

            try
            {
                AssetDatabase.CreateAsset(instance, uniquePath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                ScriptableObject newObject = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                    uniquePath
                );

                if (newObject != null)
                {
                    _selectedObjects.Insert(0, newObject);
                    UpdateAndSaveObjectOrderList(_selectedType, _selectedObjects);
                    BuildObjectsView();
                    SelectObject(newObject);
                }
                else
                {
                    Debug.LogError($"Failed to load the newly created asset at {uniquePath}");
                    BuildObjectsView();
                    SelectObject(null);
                }
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(
                    "Error Creating Asset",
                    $"Failed to create asset at '{uniquePath}': {e.Message}",
                    "OK"
                );
                DestroyImmediate(instance);
            }
        }

        private void HandleListKeyDown(KeyDownEvent evt)
        {
            Debug.Log("HANDLE LIST KEY DOWN");
            // Ignore if a popover handled the event or should handle it
            if (_activePopover != null && _activePopover.style.display == DisplayStyle.Flex)
            {
                // If search/type popover is active, let their specific handlers work
                if (
                    _lastActiveFocusArea == FocusArea.SearchResultsPopover
                    || _lastActiveFocusArea == FocusArea.AddTypePopover
                )
                {
                    // We could call their specific handlers here, but they are already registered on the search field.
                    // Let's just prevent list navigation if these are open.
                    return;
                }
                // Allow Escape in other popovers maybe?
                if (evt.keyCode == KeyCode.Escape)
                {
                    CloseActivePopover();
                    evt.PreventDefault();
                    evt.StopPropagation();
                    return;
                }
                // Otherwise, block list navigation while other popovers (Rename, Confirm) are open
                // return; // Or maybe just for up/down?
                if (evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow)
                    return;
            }

            // Determine target list based on last known focus
            bool navigationHandled = false;
            bool enterHandled = false;

            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                case KeyCode.UpArrow:
                    int direction = (evt.keyCode == KeyCode.DownArrow) ? 1 : -1;
                    switch (_lastActiveFocusArea)
                    {
                        case FocusArea.TypeList:
                            navigationHandled = NavigateTypeList(direction);
                            break;
                    }
                    if (navigationHandled)
                    {
                        evt.PreventDefault(); // Stop default scroll
                        evt.StopPropagation(); // Stop event bubbling further
                    }
                    break;

                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    switch (_lastActiveFocusArea)
                    {
                        case FocusArea.TypeList:
                            enterHandled = HandleEnterOnTypeList();
                            break;
                        // No action on Enter for Object list currently
                    }
                    if (enterHandled)
                    {
                        evt.PreventDefault();
                        evt.StopPropagation();
                    }
                    break;

                case KeyCode.Escape:
                    // Maybe clear highlight/selection? Or unfocus area?
                    // ClearKeyboardHighlight(); // Implement this if needed
                    _lastActiveFocusArea = FocusArea.None; // Reset focus area?
                    break;
            }
        }

#if ODIN_INSPECTOR
        private void HandleOdinPropertyValueChanged(InspectorProperty property, int selectionIndex)
        {
            if (_selectedObject == null || _odinPropertyTree == null || property == null)
            {
                return;
            }

            if (
                _odinPropertyTree.WeakTargets == null
                || _odinPropertyTree.WeakTargets.Count <= selectionIndex
                || !ReferenceEquals(_odinPropertyTree.WeakTargets[selectionIndex], _selectedObject)
            )
            {
                return;
            }

            const string titleFieldName = nameof(BaseDataObject._title);
            bool titlePotentiallyChanged =
                string.Equals(property.Name, titleFieldName, StringComparison.Ordinal)
                || string.Equals(property.Name, nameof(name), StringComparison.Ordinal);

            if (titlePotentiallyChanged)
            {
                rootVisualElement
                    .schedule.Execute(() => RefreshSelectedElementVisuals(_selectedObject))
                    .ExecuteLater(1);
            }
        }
#endif

        private void BuildConfirmNamespaceAddPopoverContent(
            string namespaceKey,
            List<Type> typesToAdd
        )
        {
            if (_confirmNamespaceAddPopover == null)
            {
                return;
            }

            _confirmNamespaceAddPopover.Clear();

            int countToAdd = typesToAdd.Count;
            if (countToAdd == 0)
            {
                return;
            }

            _confirmNamespaceAddPopover.style.paddingBottom = 10;
            _confirmNamespaceAddPopover.style.paddingTop = 10;
            _confirmNamespaceAddPopover.style.paddingLeft = 10;
            _confirmNamespaceAddPopover.style.paddingRight = 10;

            string message =
                $"Add {countToAdd} type{(countToAdd > 1 ? "s" : "")} from namespace '{namespaceKey}' to Data Visualizer?";
            Label messageLabel = new(message)
            {
                style =
                {
                    whiteSpace = WhiteSpace.Normal,
                    marginBottom = 15,
                    fontSize = 12,
                },
            };
            _confirmNamespaceAddPopover.Add(messageLabel);

            VisualElement buttonContainer = new()
            {
                style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd },
            };
            _confirmNamespaceAddPopover.Add(buttonContainer);

            Button cancelButton = new(CloseNestedPopover)
            {
                text = "<b><color=red>Cancel</color></b>",
                style = { marginRight = 5 },
            };
            buttonContainer.Add(cancelButton);

            Button confirmButton = new(() =>
            {
                bool stateChanged = false;
                List<string> currentManagedList = GetManagedTypeNames();
                foreach (Type typeToAdd in typesToAdd)
                {
                    if (currentManagedList.Contains(typeToAdd.FullName))
                    {
                        continue;
                    }

                    currentManagedList.Add(typeToAdd.FullName);
                    stateChanged = true;
                }

                if (stateChanged)
                {
                    if (_settings.persistStateInSettingsAsset)
                    {
                        MarkSettingsDirty();
                        AssetDatabase.SaveAssets();
                    }
                    else
                    {
                        MarkUserStateDirty();
                    }
                }
                CloseActivePopover();
                if (stateChanged)
                {
                    ScheduleRefresh();
                }
            })
            {
                text = "<b><color=green>Add</color></b>",
            };
            buttonContainer.Add(confirmButton);
        }

        private void BuildNamespaceView()
        {
            if (_namespaceListContainer == null)
            {
                return;
            }

            _namespaceListContainer.Clear();
            _namespaceCache.Clear();
            foreach ((string key, List<Type> types) in _scriptableObjectTypes)
            {
                VisualElement namespaceGroupItem = new()
                {
                    name = $"namespace-group-{key}",
                    userData = key,
                };

                namespaceGroupItem.AddToClassList(NamespaceItemClass);
                namespaceGroupItem.userData = key;
                if (types.Count == 0)
                {
                    namespaceGroupItem.AddToClassList("namespace-group-item--empty");
                }
                _namespaceListContainer.Add(namespaceGroupItem);
                namespaceGroupItem.RegisterCallback<PointerDownEvent>(OnNamespacePointerDown);

                VisualElement header = new() { name = $"namespace-header-{key}" };
                header.AddToClassList(NamespaceHeaderClass);
                namespaceGroupItem.Add(header);

                Label indicator = new(ArrowExpanded) { name = $"namespace-indicator-{key}" };
                indicator.AddToClassList(NamespaceIndicatorClass);
                indicator.AddToClassList("clickable");
                header.Add(indicator);

                Label namespaceLabel = new(key)
                {
                    name = $"namespace-name-{key}",
                    style = { unityFontStyleAndWeight = FontStyle.Bold },
                };
                namespaceLabel.AddToClassList(NamespaceLabelClass);
                header.Add(namespaceLabel);

                VisualElement typesContainer = new()
                {
                    name = $"types-container-{key}",
                    style = { marginLeft = 10 },
                    userData = key,
                };
                namespaceGroupItem.Add(typesContainer);

                bool isCollapsed = GetIsNamespaceCollapsed(key);
                ApplyNamespaceCollapsedState(indicator, typesContainer, isCollapsed, false);

                // ReSharper disable once HeapView.CanAvoidClosure
                indicator.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0 || evt.propagationPhase == PropagationPhase.TrickleDown)
                    {
                        return;
                    }

                    VisualElement parentGroup = header.parent;
                    Label associatedIndicator = parentGroup?.Q<Label>(
                        className: NamespaceIndicatorClass
                    );
                    VisualElement associatedTypesContainer = parentGroup?.Q<VisualElement>(
                        $"types-container-{key}"
                    );
                    string nsKey = parentGroup?.userData as string;

                    if (
                        associatedIndicator != null
                        && associatedTypesContainer != null
                        && !string.IsNullOrWhiteSpace(nsKey)
                    )
                    {
                        bool currentlyCollapsed =
                            associatedTypesContainer.style.display == DisplayStyle.None;
                        bool newCollapsedState = !currentlyCollapsed;

                        ApplyNamespaceCollapsedState(
                            associatedIndicator,
                            associatedTypesContainer,
                            newCollapsedState,
                            true
                        );
                    }

                    evt.StopPropagation();
                });

                for (int i = 0; i < types.Count; i++)
                {
                    int index = i;
                    Type type = types[i];
                    VisualElement typeItem = new()
                    {
                        name = $"type-item-{type.Name}",
                        userData = type,
                        pickingMode = PickingMode.Position,
                        focusable = true,
                    };
                    _namespaceCache[type] = typeItem;

                    typeItem.AddToClassList(TypeItemClass);
                    Label typeLabel = new(type.Name) { name = "type-item-label" };
                    typeLabel.AddToClassList(TypeLabelClass);
                    typeLabel.AddToClassList("clickable");
                    typeItem.Add(typeLabel);
                    // ReSharper disable once HeapView.CanAvoidClosure
                    typeItem.RegisterCallback<PointerDownEvent>(evt =>
                        OnTypePointerDown(namespaceGroupItem, evt)
                    );
                    // ReSharper disable once HeapView.CanAvoidClosure
                    typeItem.RegisterCallback<PointerUpEvent>(evt =>
                    {
                        if (_isDragging || evt.button != 0)
                        {
                            return;
                        }

                        SelectTypeIndex(index, typeItem);
                        evt.StopPropagation();
                    });
                    typesContainer.Add(typeItem);
                }
            }
        }

        private void SelectTypeIndex(int index, VisualElement typeItem)
        {
            _selectedNamespaceElement?.RemoveFromClassList("selected");
            _typeHighlightIndex = index;
            Type clickedType = typeItem.userData as Type;
            _selectedTypeElement?.parent?.RemoveFromClassList("selected");
            _selectedTypeElement?.RemoveFromClassList("selected");
            _selectedType = clickedType;
            _selectedTypeElement = typeItem;
            _selectedTypeElement.AddToClassList("selected");
            SaveNamespaceAndTypeSelectionState(GetNamespaceKey(_selectedType), _selectedType?.Name);
            _selectedNamespaceElement = typeItem?.parent?.parent;
            _selectedNamespaceElement?.EnableInClassList("selected", true);

            LoadObjectTypes(clickedType);
            ScriptableObject objectToSelect = DetermineObjectToAutoSelect();
            BuildObjectsView();
            SelectObject(objectToSelect);
        }

        private void BuildObjectsView()
        {
            if (_objectListContainer == null)
            {
                return;
            }

            _objectListContainer.Clear();
            _objectVisualElementMap.Clear();
            _objectScrollView.scrollOffset = Vector2.zero;

            if (_selectedType != null && _selectedObjects.Count == 0)
            {
                Label emptyLabel = new(
                    $"No objects of type '{_selectedType.Name}' found.\nUse the '+' button above to create one."
                )
                {
                    name = "empty-object-list-label",
                };
                emptyLabel.AddToClassList("empty-object-list-label");
                _objectListContainer.Add(emptyLabel);
            }

            foreach (ScriptableObject dataObject in _selectedObjects)
            {
                if (dataObject == null)
                {
                    continue;
                }

                VisualElement objectItemRow = new()
                {
                    name = $"object-item-row-{dataObject.GetInstanceID()}",
                };
                objectItemRow.AddToClassList(ObjectItemClass);
                objectItemRow.AddToClassList("clickable");
                objectItemRow.style.flexDirection = FlexDirection.Row;
                objectItemRow.style.alignItems = Align.Center;
                objectItemRow.userData = dataObject;
                objectItemRow.RegisterCallback<PointerDownEvent>(OnObjectPointerDown);

                VisualElement contentArea = new() { name = "content" };
                contentArea.AddToClassList(ObjectItemContentClass);
                contentArea.AddToClassList("clickable");
                objectItemRow.Add(contentArea);

                string dataObjectName;
                if (dataObject is BaseDataObject baseDataObject)
                {
                    dataObjectName = baseDataObject.Title;
                }
                else
                {
                    dataObjectName = dataObject.name;
                }
                Label titleLabel = new(dataObjectName) { name = "object-item-label" };
                titleLabel.AddToClassList("object-item__label");
                titleLabel.AddToClassList("clickable");
                contentArea.Add(titleLabel);

                VisualElement actionsArea = new()
                {
                    name = "actions",
                    style =
                    {
                        flexDirection = FlexDirection.Row,
                        alignItems = Align.Center,
                        flexShrink = 0,
                    },
                };
                actionsArea.AddToClassList(ObjectItemActionsClass);
                objectItemRow.Add(actionsArea);

                Button cloneButton = new(() => CloneObject(dataObject))
                {
                    text = "++",
                    tooltip = "Clone Object",
                };
                cloneButton.AddToClassList(ActionButtonClass);
                cloneButton.AddToClassList("clone-button");
                cloneButton.AddToClassList("clickable");
                actionsArea.Add(cloneButton);

                Button renameButton = new(() => OpenRenamePopover(dataObject)) { text = "✎" };
                renameButton.AddToClassList(ActionButtonClass);
                renameButton.AddToClassList("rename-button");
                renameButton.AddToClassList("clickable");
                actionsArea.Add(renameButton);

                Button deleteButton = new(() => OpenConfirmDeletePopover(dataObject))
                {
                    text = "X",
                };
                deleteButton.AddToClassList(ActionButtonClass);
                deleteButton.AddToClassList("delete-button");
                deleteButton.AddToClassList("clickable");
                actionsArea.Add(deleteButton);

                _objectVisualElementMap[dataObject] = objectItemRow;
                _objectListContainer.Add(objectItemRow);

                if (_selectedObject == dataObject)
                {
                    objectItemRow.AddToClassList("selected");
                    _selectedElement = objectItemRow;
                }
            }
        }

        private void BuildInspectorView()
        {
            if (_inspectorContainer == null)
            {
                return;
            }

            _inspectorContainer.Clear();
            _inspectorScrollView.scrollOffset = Vector2.zero;

            if (_selectedObject == null || _currentInspectorScriptableObject == null)
            {
                _inspectorContainer.Add(
                    new Label("Select an object to inspect.")
                    {
                        style = { unityTextAlign = TextAnchor.MiddleCenter, paddingTop = 20 },
                    }
                );
#if ODIN_INSPECTOR
                if (_odinPropertyTree != null)
                {
                    _odinPropertyTree.OnPropertyValueChanged -= HandleOdinPropertyValueChanged;
                    _odinPropertyTree.Dispose();
                    _odinPropertyTree = null;
                }
                _odinInspectorContainer?.MarkDirtyRepaint();
#endif
                return;
            }

            // ReSharper disable once RedundantAssignment
            bool useOdinInspector = false;
            Type objectType = _selectedObject.GetType();
#if ODIN_INSPECTOR

            if (objectType.IsAttributeDefined(out CustomDataVisualization customVisualization))
            {
                useOdinInspector = customVisualization.UseOdinInspector;
            }
            else if (_selectedObject is SerializedScriptableObject)
            {
                useOdinInspector = true;
            }
            else if (
                objectType.IsAttributeDefined<ShowOdinSerializedPropertiesInInspectorAttribute>()
                && typeof(ISerializationCallbackReceiver).IsAssignableFrom(objectType)
            )
            {
                useOdinInspector = true;
            }

            if (useOdinInspector)
            {
                try
                {
                    bool recreateTree =
                        _odinPropertyTree?.WeakTargets == null
                        || _odinPropertyTree.WeakTargets.Count == 0
                        || !ReferenceEquals(_odinPropertyTree.WeakTargets[0], _selectedObject);

                    if (recreateTree)
                    {
                        if (_odinPropertyTree != null)
                        {
                            _odinPropertyTree.OnPropertyValueChanged -=
                                HandleOdinPropertyValueChanged;
                            _odinPropertyTree.Dispose();
                        }

                        _odinPropertyTree = PropertyTree.Create(_selectedObject);
                        _odinPropertyTree.OnPropertyValueChanged += HandleOdinPropertyValueChanged;
                        _odinInspectorContainer?.MarkDirtyRepaint();
                    }

                    if (_odinInspectorContainer == null)
                    {
                        _odinInspectorContainer = new IMGUIContainer(
                            // ReSharper disable once AccessToDisposedClosure
                            () => _odinPropertyTree?.Draw()
                        )
                        {
                            name = "odin-inspector",
                            style = { flexGrow = 1 },
                        };
                    }
                    else
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        _odinInspectorContainer.onGUIHandler = () => _odinPropertyTree?.Draw();
                    }

                    if (_odinInspectorContainer.parent != _inspectorContainer)
                    {
                        _inspectorContainer.Add(_odinInspectorContainer);
                    }

                    _odinInspectorContainer.MarkDirtyRepaint();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error setting up Odin Inspector. {e}");
                    _inspectorContainer.Add(new Label($"Odin Inspector Error: {e.Message}"));
                    _odinPropertyTree = null;
                }
            }
#endif
            if (!useOdinInspector)
            {
#if ODIN_INSPECTOR
                if (
                    _odinInspectorContainer != null
                    && _odinInspectorContainer.parent == _inspectorContainer
                )
                {
                    _odinInspectorContainer.RemoveFromHierarchy();
                }
                _odinPropertyTree?.Dispose();
                _odinPropertyTree = null;
#endif
                try
                {
                    _currentInspectorScriptableObject.UpdateIfRequiredOrScript();
                    SerializedProperty serializedProperty =
                        _currentInspectorScriptableObject.GetIterator();
                    bool enterChildren = true;
                    const string titleFieldName = nameof(BaseDataObject._title);

                    if (serializedProperty.NextVisible(true))
                    {
                        using (
                            new EditorGUI.DisabledScope(
                                string.Equals(
                                    "m_Script",
                                    serializedProperty.propertyPath,
                                    StringComparison.Ordinal
                                )
                            )
                        )
                        {
                            PropertyField scriptField = new(serializedProperty);
                            scriptField.Bind(_currentInspectorScriptableObject);
                            _inspectorContainer.Add(scriptField);
                        }

                        enterChildren = false;
                    }

                    while (serializedProperty.NextVisible(enterChildren))
                    {
                        SerializedProperty currentPropCopy = serializedProperty.Copy();
                        PropertyField propertyField = new(currentPropCopy);
                        propertyField.Bind(_currentInspectorScriptableObject);

                        if (
                            string.Equals(
                                currentPropCopy.propertyPath,
                                titleFieldName,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            propertyField.RegisterValueChangeCallback(_ =>
                            {
                                _currentInspectorScriptableObject.ApplyModifiedProperties();
                                rootVisualElement
                                    .schedule.Execute(
                                        () => RefreshSelectedElementVisuals(_selectedObject)
                                    )
                                    .ExecuteLater(1);
                            });
                        }

                        _inspectorContainer.Add(propertyField);
                        enterChildren = false;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error creating standard inspector. {e}");
                    _inspectorContainer.Add(new Label($"Inspector Error: {e.Message}"));
                }
            }

            VisualElement customElement = TryGetCustomVisualElement(objectType);
            if (customElement != null)
            {
                _inspectorContainer.Add(customElement);
            }
        }

        private VisualElement TryGetCustomVisualElement(Type objectType)
        {
            if (_selectedObject is BaseDataObject baseDataObject)
            {
                return baseDataObject.BuildGUI(
                    new DataVisualizerGUIContext(_currentInspectorScriptableObject)
                );
            }

            // TODO: CACHE
            MethodInfo visualMethod = objectType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.ReturnType.IsAssignableFrom(typeof(VisualElement)))
                .OrderBy(method => method.GetParameters().Length)
                .Where(method =>
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 0)
                    {
                        return true;
                    }

                    return parameters[0].ParameterType == typeof(SerializedObject);
                })
                .FirstOrDefault();
            if (visualMethod == null)
            {
                return null;
            }

            ParameterInfo[] parameters = visualMethod.GetParameters();
            if (parameters.Length == 0)
            {
                return visualMethod.Invoke(_selectedObject, Array.Empty<object>()) as VisualElement;
            }

            if (parameters[0].ParameterType == typeof(SerializedObject))
            {
                List<object> arguments = new(parameters.Length)
                {
                    _currentInspectorScriptableObject,
                };
                for (int i = 1; i < parameters.Length; i++)
                {
                    ParameterInfo parameter = parameters[i];
                    if (parameter.ParameterType.IsValueType)
                    {
                        try
                        {
                            var parameterInstance = Activator.CreateInstance(
                                parameter.ParameterType
                            );
                            arguments.Add(parameterInstance);
                        }
                        catch
                        {
                            arguments.Add(null);
                        }
                    }
                    else
                    {
                        arguments.Add(null);
                    }
                }

                try
                {
                    return visualMethod.Invoke(_selectedObject, arguments.ToArray())
                        as VisualElement;
                }
                catch
                {
                    // Swallow
                }
            }

            return null;
        }

        private void CloneObject(ScriptableObject originalObject)
        {
            if (originalObject == null)
            {
                return;
            }

            string originalPath = AssetDatabase.GetAssetPath(originalObject);
            if (string.IsNullOrWhiteSpace(originalPath))
            {
                EditorUtility.DisplayDialog(
                    "Error",
                    "Cannot clone object: Original asset path not found.",
                    "OK"
                );
                return;
            }

            ScriptableObject cloneInstance = Instantiate(originalObject);
            if (cloneInstance == null)
            {
                EditorUtility.DisplayDialog(
                    "Error",
                    "Failed to instantiate a clone of the object.",
                    "OK"
                );
                return;
            }

            if (cloneInstance is BaseDataObject baseDataObject)
            {
                baseDataObject._assetGuid = string.Empty;
            }

            string originalDirectory = Path.GetDirectoryName(originalPath);
            if (string.IsNullOrWhiteSpace(originalDirectory))
            {
                EditorUtility.DisplayDialog(
                    "Error",
                    "Cannot clone object: Original asset path is invalid.",
                    "OK"
                );
                return;
            }

            const string pattern = @"\(Clone(\s+-?\d+)?\)";

            string directory = originalDirectory.SanitizePath();
            string originalName = Path.GetFileNameWithoutExtension(originalPath);
            originalName = Regex.Replace(originalName, pattern, string.Empty);
            if (originalName.EndsWith(' '))
            {
                int lastIndex = originalName.Length - 1;
                for (; 0 <= lastIndex; --lastIndex)
                {
                    if (!char.IsWhiteSpace(originalName[lastIndex]))
                    {
                        break;
                    }
                }
                originalName = originalName.Substring(0, lastIndex + 1);
            }
            string extension = Path.GetExtension(originalPath);
            string proposedPath;
            string uniquePath;
            int count = 0;
            do
            {
                string proposedName =
                    $"{originalName} (Clone{(count++ == 0 ? string.Empty : $" {count}")}){extension}";
                proposedPath = Path.Combine(directory, proposedName).SanitizePath();
                uniquePath = AssetDatabase.GenerateUniqueAssetPath(proposedPath);
            } while (!string.Equals(uniquePath, proposedPath, StringComparison.Ordinal));

            try
            {
                AssetDatabase.CreateAsset(cloneInstance, uniquePath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                ScriptableObject cloneAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                    uniquePath
                );
                if (cloneAsset != null)
                {
                    if (cloneAsset is BaseDataObject cloneDataObject)
                    {
                        cloneDataObject._title = cloneAsset.name;
                        cloneDataObject.OnValidate();
                    }

                    int originalIndex = _selectedObjects.IndexOf(originalObject);
                    if (0 <= originalIndex)
                    {
                        _selectedObjects.Insert(originalIndex + 1, cloneAsset);
                    }
                    else
                    {
                        _selectedObjects.Add(cloneAsset);
                    }

                    UpdateAndSaveObjectOrderList(cloneAsset.GetType(), _selectedObjects);
                    BuildObjectsView();
                    SelectObject(cloneAsset);
                }
                else
                {
                    Debug.LogError($"Failed to load the cloned asset at {uniquePath}");
                    BuildObjectsView();
                }
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(
                    "Error Cloning Asset",
                    $"Failed to create cloned asset at '{uniquePath}': {e.Message}",
                    "OK"
                );
                DestroyImmediate(cloneInstance);
            }
        }

        private ScriptableObject DetermineObjectToAutoSelect()
        {
            if (_selectedType == null || _selectedObjects == null || _selectedObjects.Count == 0)
            {
                return null;
            }

            ScriptableObject objectToSelect = null;
            string savedObjectGuid = GetLastSelectedObjectGuidForType(_selectedType.Name);
            if (!string.IsNullOrWhiteSpace(savedObjectGuid))
            {
                objectToSelect = _selectedObjects.Find(obj =>
                    obj != null
                    && string.Equals(
                        AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(obj)),
                        savedObjectGuid,
                        StringComparison.Ordinal
                    )
                );
            }

            if (!string.IsNullOrWhiteSpace(savedObjectGuid))
            {
                objectToSelect = _selectedObjects.Find(obj =>
                {
                    if (obj == null)
                    {
                        return false;
                    }

                    string path = AssetDatabase.GetAssetPath(obj);
                    return !string.IsNullOrWhiteSpace(path)
                        && string.Equals(
                            AssetDatabase.AssetPathToGUID(path),
                            savedObjectGuid,
                            StringComparison.Ordinal
                        );
                });
            }

            if (objectToSelect == null)
            {
                objectToSelect = _selectedObjects[0];
            }

            return objectToSelect;
        }

        private void ApplyNamespaceCollapsedState(
            Label indicator,
            VisualElement typesContainer,
            bool collapsed,
            bool saveState
        )
        {
            if (_settings == null || indicator == null || typesContainer == null)
            {
                return;
            }

            indicator.text = collapsed ? ArrowCollapsed : ArrowExpanded;
            typesContainer.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;

            if (saveState)
            {
                string namespaceKey = typesContainer.parent?.userData as string;
                if (string.IsNullOrWhiteSpace(namespaceKey))
                {
                    return;
                }
                SetIsNamespaceCollapsed(namespaceKey, collapsed);
            }
        }

        private void SaveNamespaceAndTypeSelectionState(string namespaceKey, string typeName)
        {
            if (_settings == null)
            {
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(namespaceKey))
                {
                    return;
                }

                SetLastSelectedNamespaceKey(namespaceKey);
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    SetLastSelectedTypeName(typeName);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error saving type/namespace selection state. {e}");
            }
        }

        private void RefreshSelectedElementVisuals(ScriptableObject dataObject)
        {
            if (
                dataObject == null
                || !_objectVisualElementMap.TryGetValue(dataObject, out VisualElement visualElement)
            )
            {
                return;
            }
            UpdateObjectTitleRepresentation(dataObject, visualElement);
        }

        private void UpdateObjectTitleRepresentation(
            ScriptableObject dataObject,
            VisualElement element
        )
        {
            if (dataObject == null || element == null)
            {
                return;
            }

            Label titleLabel = element.Q<Label>(className: "object-item__label");
            if (titleLabel == null)
            {
                Debug.LogError("Could not find title label within object item element.");
                return;
            }

            string currentTitle;
            if (dataObject is BaseDataObject baseDataObject)
            {
                currentTitle = baseDataObject.Title;
            }
            else
            {
                currentTitle = dataObject.name;
            }
            if (titleLabel.text != currentTitle)
            {
                titleLabel.text = currentTitle;
            }
        }

        private void LoadObjectTypes(Type type)
        {
            if (type == null)
            {
                return;
            }

            _selectedObjects.Clear();
            _objectVisualElementMap.Clear();

            List<string> customGuidOrder = GetObjectOrderForType(type.FullName);
            Dictionary<string, ScriptableObject> objectsByGuid = new();
            string[] assetGuids = AssetDatabase.FindAssets($"t:{type.Name}");
            foreach (string assetGuid in assetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (string.IsNullOrWhiteSpace(assetPath))
                {
                    continue;
                }

                ScriptableObject asset =
                    AssetDatabase.LoadMainAssetAtPath(assetPath) as ScriptableObject;
                if (asset == null || asset.GetType() != type)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(assetGuid))
                {
                    objectsByGuid[assetGuid] = asset;
                }
            }

            List<ScriptableObject> sortedObjects = new();

            foreach (string guid in customGuidOrder)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                ScriptableObject dataObject = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (dataObject == null || dataObject.GetType() != type)
                {
                    continue;
                }

                sortedObjects.Add(dataObject);
                objectsByGuid.Remove(guid);
            }

            List<ScriptableObject> remainingObjects = objectsByGuid.Values.ToList();
            remainingObjects.Sort(
                (a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase)
            );
            sortedObjects.AddRange(remainingObjects);

            _selectedObjects.Clear();
            _selectedObjects.AddRange(sortedObjects);
            SelectObject(null);
        }

        private void LoadScriptableObjectTypes()
        {
            if (_settings == null)
            {
                _settings = LoadOrCreateSettings();
            }

            List<string> managedTypeFullNames = GetManagedTypeNames();
            HashSet<string> managedTypeSet = new(managedTypeFullNames);

            List<Type> allObjectTypes = LoadRelevantScriptableObjectTypes();

            List<Type> typesToDisplay = allObjectTypes
                .Where(t =>
                    managedTypeSet.Contains(t.FullName)
                    || typeof(BaseDataObject).IsAssignableFrom(t)
                )
                .ToList();

            IEnumerable<(string key, List<Type> types)> groups = typesToDisplay
                .GroupBy(GetNamespaceKey)
                .Select(g => (key: g.Key, types: g.ToList()));

            _scriptableObjectTypes.Clear();
            _scriptableObjectTypes.AddRange(groups);

            List<string> customNamespaceOrder = GetNamespaceOrder();
            _scriptableObjectTypes.Sort(
                (lhs, rhs) => CompareUsingCustomOrder(lhs.key, rhs.key, customNamespaceOrder)
            );

            foreach ((string key, List<Type> types) in _scriptableObjectTypes)
            {
                List<string> customTypeNameOrder = GetTypeOrderForNamespace(key);
                types.Sort(
                    (lhs, rhs) => CompareUsingCustomOrder(lhs.Name, rhs.Name, customTypeNameOrder)
                );
            }
        }

        private List<Type> LoadRelevantScriptableObjectTypes()
        {
            return _relevantScriptableObjectTypes ??= TypeCache
                .GetTypesDerivedFrom<ScriptableObject>()
                .Where(t => !t.IsAbstract && !t.IsGenericType)
                .ToList();
        }

        private void SelectObject(ScriptableObject dataObject)
        {
            _selectedElement?.RemoveFromClassList("selected");
            _selectedObject = dataObject;

            _selectedElement = null;

            if (
                _selectedObject != null
                && _objectVisualElementMap.TryGetValue(
                    _selectedObject,
                    out VisualElement newSelectedElement
                )
            )
            {
                _selectedElement = newSelectedElement;
                _selectedElement.AddToClassList("selected");
                Selection.activeObject = _selectedObject;
                _objectScrollView.ScrollTo(_selectedElement);
                try
                {
                    if (_selectedType != null)
                    {
                        string namespaceKey = GetNamespaceKey(_selectedType);
                        string typeName = _selectedType.Name;
                        string assetPath = AssetDatabase.GetAssetPath(_selectedObject);
                        string objectGuid = null;
                        if (!string.IsNullOrWhiteSpace(assetPath))
                        {
                            objectGuid = AssetDatabase.AssetPathToGUID(assetPath);
                        }
                        SetLastSelectedNamespaceKey(namespaceKey);
                        SetLastSelectedTypeName(typeName);
                        SetLastSelectedObjectGuidForType(typeName, objectGuid);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error saving selection state. {e}");
                }
            }
            else
            {
                if (_selectedType != null)
                {
                    string typeName = _selectedType.Name;
                    SetLastSelectedObjectGuidForType(typeName, null);
                }
            }

            _currentInspectorScriptableObject?.Dispose();
            _currentInspectorScriptableObject =
                dataObject != null ? new SerializedObject(dataObject) : null;
            BuildInspectorView();

            if (
                dataObject != null
                && _namespaceCache.TryGetValue(
                    dataObject.GetType(),
                    out VisualElement namespaceElement
                )
            )
            {
                // TODO CONSOLIDATE
                namespaceElement?.EnableInClassList("selected", true);
                _selectedNamespaceElement = namespaceElement?.parent?.parent;
                _selectedNamespaceElement?.EnableInClassList("selected", true);
                VisualElement immediateParent = namespaceElement?.parent;
                if (immediateParent != null)
                {
                    _typeHighlightIndex = immediateParent.IndexOf(namespaceElement);
                }
            }
        }

        private void OnObjectPointerDown(PointerDownEvent evt)
        {
            VisualElement targetElement = evt.currentTarget as VisualElement;
            if (targetElement?.userData is not ScriptableObject clickedObject)
            {
                return;
            }

            if (_selectedObject != clickedObject)
            {
                SelectObject(clickedObject);
            }

            if (evt.button == 0)
            {
                _draggedElement = targetElement;
                _draggedData = clickedObject;
                _activeDragType = DragType.Object;
                _dragStartPosition = evt.position;
                targetElement.CapturePointer(evt.pointerId);
                targetElement.RegisterCallback<PointerMoveEvent>(OnCapturedPointerMove);
                targetElement.RegisterCallback<PointerUpEvent>(OnCapturedPointerUp);
                targetElement.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
                evt.StopPropagation();
            }
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            if (_activeDragType != DragType.None && _draggedElement != null)
            {
                _draggedElement.UnregisterCallback<PointerMoveEvent>(OnCapturedPointerMove);
                _draggedElement.UnregisterCallback<PointerUpEvent>(OnCapturedPointerUp);
                _draggedElement.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
                CancelDrag();
            }
        }

        private void OnCapturedPointerMove(PointerMoveEvent evt)
        {
            if (
                _draggedElement == null
                || !_draggedElement.HasPointerCapture(evt.pointerId)
                || _activeDragType == DragType.None
            )
            {
                return;
            }

            if (_dragGhost != null)
            {
                _dragGhost.style.left = evt.position.x - _dragGhost.resolvedStyle.width / 2;
                _dragGhost.style.top = evt.position.y - _dragGhost.resolvedStyle.height;
            }

            float currentTime = Time.realtimeSinceStartup;
            if (currentTime - _lastDragUpdateTime < DragUpdateThrottleTime)
            {
                return;
            }
            _lastDragUpdateTime = currentTime;

            if (!_isDragging)
            {
                if (DragDistanceThreshold < Vector2.Distance(evt.position, _dragStartPosition))
                {
                    _isDragging = true;
                    string dragText = _draggedData switch
                    {
                        BaseDataObject dataObj => dataObj.Title,
                        ScriptableObject dataObj => dataObj.name,
                        string nsKey => nsKey,
                        Type type => type.Name,
                        _ => "Dragging Item",
                    };
                    StartDragVisuals(evt.position, dragText);
                }
                else
                {
                    return;
                }
            }

            if (_isDragging)
            {
                UpdateInPlaceGhostPosition(evt.position);
            }
        }

        private void OnCapturedPointerUp(PointerUpEvent evt)
        {
            if (
                _draggedElement == null
                || !_draggedElement.HasPointerCapture(evt.pointerId)
                || _activeDragType == DragType.None
            )
            {
                return;
            }

            int pointerId = evt.pointerId;
            bool performDrop = _isDragging;
            DragType dropType = _activeDragType;

            VisualElement draggedElement = _draggedElement;
            try
            {
                _draggedElement.ReleasePointer(pointerId);

                if (performDrop)
                {
                    switch (dropType)
                    {
                        case DragType.Object:
                        {
                            PerformObjectDrop();
                            break;
                        }
                        case DragType.Namespace:
                        {
                            PerformNamespaceDrop();
                            break;
                        }
                        case DragType.Type:
                        {
                            PerformTypeDrop();
                            break;
                        }
                        default:
                        {
                            throw new InvalidEnumArgumentException(
                                nameof(dropType),
                                (int)dropType,
                                typeof(DragType)
                            );
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during drop execution for {dropType}. {e}");
            }
            finally
            {
                draggedElement.UnregisterCallback<PointerMoveEvent>(OnCapturedPointerMove);
                draggedElement.UnregisterCallback<PointerUpEvent>(OnCapturedPointerUp);
                draggedElement.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);

                CancelDrag();
            }

            evt.StopPropagation();
        }

        private void PerformNamespaceDrop()
        {
            int targetIndex = _inPlaceGhost?.userData is int index ? index : -1;

            _inPlaceGhost?.RemoveFromHierarchy();

            if (
                _draggedElement == null
                || _draggedData is not string draggedKey
                || _namespaceListContainer == null
            )
            {
                return;
            }

            if (targetIndex < 0)
            {
                return;
            }

            int currentIndex = _namespaceListContainer.IndexOf(_draggedElement);
            if (currentIndex < 0)
            {
                return;
            }

            if (currentIndex < targetIndex)
            {
                targetIndex--;
            }

            _draggedElement.style.display = DisplayStyle.Flex;
            _draggedElement.style.opacity = 1.0f;
            _namespaceListContainer.Insert(targetIndex, _draggedElement);

            int oldDataIndex = _scriptableObjectTypes.FindIndex(kvp =>
                string.Equals(kvp.key, draggedKey, StringComparison.Ordinal)
            );
            if (0 <= oldDataIndex)
            {
                (string key, List<Type> types) draggedItem = _scriptableObjectTypes[oldDataIndex];
                _scriptableObjectTypes.RemoveAt(oldDataIndex);
                int dataInsertIndex = targetIndex;
                dataInsertIndex = Mathf.Clamp(dataInsertIndex, 0, _scriptableObjectTypes.Count);
                _scriptableObjectTypes.Insert(dataInsertIndex, draggedItem);
                UpdateAndSaveNamespaceOrder();
            }
        }

        private void OnNamespacePointerDown(PointerDownEvent evt)
        {
            if (
                evt.currentTarget
                is not VisualElement { userData: string namespaceKey } targetElement
            )
            {
                return;
            }

            if (evt.button == 0)
            {
                _draggedElement = targetElement;
                _draggedData = namespaceKey;
                _activeDragType = DragType.Namespace;
                _dragStartPosition = evt.position;
                targetElement.CapturePointer(evt.pointerId);
                targetElement.RegisterCallback<PointerMoveEvent>(OnCapturedPointerMove);
                targetElement.RegisterCallback<PointerUpEvent>(OnCapturedPointerUp);
                targetElement.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
                evt.StopPropagation();
            }
        }

        private void UpdateAndSaveNamespaceOrder()
        {
            if (_settings == null)
            {
                return;
            }

            List<string> newNamespaceOrder = _scriptableObjectTypes.Select(kvp => kvp.key).ToList();
            SetNamespaceOrder(newNamespaceOrder);
        }

        private void OnTypePointerDown(VisualElement namespaceHeader, PointerDownEvent evt)
        {
            // TODO IMPLEMENT NEW HANDLER
            if (evt.currentTarget is not VisualElement { userData: Type type } targetElement)
            {
                return;
            }

            if (evt.button == 0)
            {
                _lastActiveFocusArea = FocusArea.TypeList;
                _typeListParentNamespaceHeader = namespaceHeader;
                _draggedElement = targetElement;
                _draggedData = type;
                _activeDragType = DragType.Type;
                _dragStartPosition = evt.position;
                _isDragging = false;
                targetElement.CapturePointer(evt.pointerId);
                targetElement.RegisterCallback<PointerMoveEvent>(OnCapturedPointerMove);
                targetElement.RegisterCallback<PointerUpEvent>(OnCapturedPointerUp);
                targetElement.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
                evt.StopPropagation();
            }
        }

        private void PerformTypeDrop()
        {
            int targetIndex = _inPlaceGhost?.userData is int index ? index : -1;
            _inPlaceGhost?.RemoveFromHierarchy();

            VisualElement typesContainer = _draggedElement?.parent;
            string namespaceKey = typesContainer?.userData as string;

            if (
                _draggedElement == null
                || _draggedData is not Type draggedType
                || typesContainer == null
                || string.IsNullOrWhiteSpace(namespaceKey)
            )
            {
                return;
            }

            if (targetIndex < 01)
            {
                return;
            }

            int currentIndex = typesContainer.IndexOf(_draggedElement);
            if (currentIndex < targetIndex)
            {
                targetIndex--;
            }

            _draggedElement.style.display = DisplayStyle.Flex;
            _draggedElement.style.opacity = 1.0f;
            typesContainer.Insert(targetIndex, _draggedElement);

            int namespaceIndex = _scriptableObjectTypes.FindIndex(kvp =>
                string.Equals(kvp.key, namespaceKey, StringComparison.Ordinal)
            );
            if (0 <= namespaceIndex)
            {
                List<Type> typesList = _scriptableObjectTypes[namespaceIndex].types;
                int oldDataIndex = typesList.IndexOf(draggedType);
                if (0 <= oldDataIndex)
                {
                    typesList.RemoveAt(oldDataIndex);
                    int dataInsertIndex = targetIndex;
                    dataInsertIndex = Mathf.Clamp(dataInsertIndex, 0, typesList.Count);
                    typesList.Insert(dataInsertIndex, draggedType);

                    UpdateAndSaveTypeOrder(namespaceKey, typesList);
                }
            }
        }

        private void UpdateAndSaveTypeOrder(string namespaceKey, List<Type> orderedTypes)
        {
            if (_settings == null)
            {
                return;
            }

            List<string> newTypeNameOrder = orderedTypes.Select(t => t.Name).ToList();
            SetTypeOrderForNamespace(namespaceKey, newTypeNameOrder);
        }

        private void PerformObjectDrop()
        {
            int targetIndex = _inPlaceGhost?.userData is int index ? index : -1;
            _inPlaceGhost?.RemoveFromHierarchy();
            if (
                _draggedElement == null
                || _draggedData is not ScriptableObject draggedObject
                || _objectListContainer == null
            )
            {
                return;
            }

            if (targetIndex < 0)
            {
                return;
            }

            int currentIndex = _objectListContainer.IndexOf(_draggedElement);
            if (currentIndex < 0)
            {
                return;
            }

            if (currentIndex < targetIndex)
            {
                targetIndex--;
            }

            _draggedElement.style.display = DisplayStyle.Flex;
            _draggedElement.style.opacity = 1.0f;
            _objectListContainer.Insert(targetIndex, _draggedElement);

            int oldDataIndex = _selectedObjects.IndexOf(draggedObject);
            if (0 > oldDataIndex)
            {
                return;
            }

            _selectedObjects.RemoveAt(oldDataIndex);
            int dataInsertIndex = targetIndex;
            dataInsertIndex = Mathf.Clamp(dataInsertIndex, 0, _selectedObjects.Count);
            _selectedObjects.Insert(dataInsertIndex, draggedObject);
            if (_selectedType != null)
            {
                UpdateAndSaveObjectOrderList(_selectedType, _selectedObjects);
            }
        }

        private void StartDragVisuals(Vector2 currentPosition, string dragText)
        {
            if (_draggedElement == null || _draggedData == null)
            {
                return;
            }

            if (_dragGhost == null)
            {
                _dragGhost = new VisualElement
                {
                    name = "drag-ghost-cursor",
                    style = { visibility = Visibility.Visible },
                };
                _dragGhost.style.left = currentPosition.x - _dragGhost.resolvedStyle.width / 2;
                _dragGhost.style.top = currentPosition.y - _dragGhost.resolvedStyle.height;
                _dragGhost.AddToClassList("drag-ghost");
                _dragGhost.BringToFront();
                Label ghostLabel = new(dragText)
                {
                    style = { unityTextAlign = TextAnchor.MiddleLeft },
                };
                _dragGhost.Add(ghostLabel);
                rootVisualElement.Add(_dragGhost);
            }
            else
            {
                Label ghostLabel = _dragGhost.Q<Label>();
                if (ghostLabel != null)
                {
                    ghostLabel.text = dragText;
                }
            }

            _dragGhost.style.visibility = Visibility.Visible;
            _dragGhost.style.left = currentPosition.x - _draggedElement.resolvedStyle.width / 2;
            _dragGhost.style.top = currentPosition.y - _draggedElement.resolvedStyle.height;
            _dragGhost.BringToFront();

            if (_inPlaceGhost == null)
            {
                try
                {
                    _inPlaceGhost = new VisualElement
                    {
                        name = "drag-ghost-overlay",
                        style =
                        {
                            height = _draggedElement.resolvedStyle.height,
                            marginTop = _draggedElement.resolvedStyle.marginTop,
                            marginBottom = _draggedElement.resolvedStyle.marginBottom,
                            marginLeft = _draggedElement.resolvedStyle.marginLeft,
                            marginRight = _draggedElement.resolvedStyle.marginRight,
                        },
                    };

                    foreach (string className in _draggedElement.GetClasses())
                    {
                        _inPlaceGhost.AddToClassList(className);
                    }
                    _inPlaceGhost.AddToClassList("in-place-ghost");

                    Label originalLabel =
                        _draggedElement.Q<Label>(className: "object-item__label")
                        ?? _draggedElement.Q<Label>(className: "type-item__label")
                        ?? _draggedElement.Q<Label>();

                    if (originalLabel != null)
                    {
                        Label ghostLabel = new(originalLabel.text);
                        foreach (string className in originalLabel.GetClasses())
                        {
                            ghostLabel.AddToClassList(className);
                        }
                        ghostLabel.pickingMode = PickingMode.Ignore;
                        _inPlaceGhost.Add(ghostLabel);
                    }
                    else
                    {
                        Label fallbackLabel = new(dragText) { pickingMode = PickingMode.Ignore };
                        _inPlaceGhost.Add(fallbackLabel);
                    }

                    _inPlaceGhost.pickingMode = PickingMode.Ignore;
                    _inPlaceGhost.style.visibility = Visibility.Hidden;
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error creating in-place ghost. {e}");
                    _inPlaceGhost = null;
                }
            }

            _lastGhostInsertIndex = -1;
            _lastGhostParent = null;
            _lastDragUpdateTime = Time.realtimeSinceStartup;
            _draggedElement.style.display = DisplayStyle.None;
            _draggedElement.style.opacity = 0.5f;
        }

        private void UpdateInPlaceGhostPosition(Vector2 pointerPosition)
        {
            VisualElement container = null;
            VisualElement positioningParent;

            switch (_activeDragType)
            {
                case DragType.Object:
                {
                    container = _objectListContainer.contentContainer;
                    positioningParent = _objectListContainer.contentContainer;
                    break;
                }
                case DragType.Namespace:
                {
                    container = _namespaceListContainer;
                    positioningParent = _namespaceListContainer;
                    break;
                }
                case DragType.Type:
                {
                    if (_draggedElement != null)
                    {
                        container = _draggedElement.parent;
                    }

                    positioningParent = container;
                    break;
                }
                default:
                {
                    if (_inPlaceGhost?.parent != null)
                    {
                        _inPlaceGhost.RemoveFromHierarchy();
                    }

                    if (_inPlaceGhost != null)
                    {
                        _inPlaceGhost.style.visibility = Visibility.Hidden;
                    }

                    _lastGhostInsertIndex = -1;
                    _lastGhostParent = null;
                    return;
                }
            }

            if (
                container == null
                || positioningParent == null
                || _draggedElement == null
                || _inPlaceGhost == null
            )
            {
                if (_inPlaceGhost?.parent != null)
                {
                    _inPlaceGhost.RemoveFromHierarchy();
                }

                if (_inPlaceGhost != null)
                {
                    _inPlaceGhost.style.visibility = Visibility.Hidden;
                }

                _lastGhostInsertIndex = -1;
                _lastGhostParent = null;
                return;
            }

            int childCount = container.childCount;
            int targetIndex = -1;
            Vector2 localPointerPos = container.WorldToLocal(pointerPosition);

            for (int i = 0; i < childCount; ++i)
            {
                VisualElement child = container.ElementAt(i);
                float yMin = child.layout.yMin + child.layout.height / 2;
                if (localPointerPos.y < yMin)
                {
                    targetIndex = i;
                    break;
                }
            }

            if (targetIndex < 0)
            {
                targetIndex = childCount;
                targetIndex = Math.Max(0, targetIndex);
            }

            bool targetIndexValid = true;
            int maxIndex = positioningParent.childCount;

            if (_inPlaceGhost.parent == positioningParent)
            {
                maxIndex--;
            }

            maxIndex = Math.Max(0, maxIndex);
            targetIndex = Mathf.Clamp(targetIndex, 0, maxIndex + 1);

            if (targetIndex != _lastGhostInsertIndex || positioningParent != _lastGhostParent)
            {
                if (_inPlaceGhost.parent != null && _inPlaceGhost.parent != positioningParent)
                {
                    _inPlaceGhost.RemoveFromHierarchy();
                    positioningParent.Add(_inPlaceGhost);
                }
                else if (0 <= targetIndex && targetIndex <= positioningParent.childCount)
                {
                    _inPlaceGhost.RemoveFromHierarchy();
                    if (positioningParent.childCount < targetIndex)
                    {
                        positioningParent.Add(_inPlaceGhost);
                    }
                    else
                    {
                        positioningParent.Insert(targetIndex, _inPlaceGhost);
                    }
                }
                else
                {
                    targetIndexValid = false;
                }

                if (targetIndexValid)
                {
                    _inPlaceGhost.style.visibility = Visibility.Visible;
                }

                _lastGhostInsertIndex = targetIndex;
                _lastGhostParent = positioningParent;
            }
            else
            {
                _inPlaceGhost.style.visibility = Visibility.Visible;
            }

            if (targetIndexValid)
            {
                _inPlaceGhost.userData = targetIndex;
            }
            else
            {
                if (_inPlaceGhost.parent != null)
                {
                    _inPlaceGhost.RemoveFromHierarchy();
                }

                _inPlaceGhost.style.visibility = Visibility.Hidden;
                _inPlaceGhost.userData = -1;
                _lastGhostInsertIndex = -1;
                _lastGhostParent = null;
            }
        }

        private void CancelDrag()
        {
            if (_inPlaceGhost != null)
            {
                _inPlaceGhost.RemoveFromHierarchy();
                _inPlaceGhost = null;
            }
            _lastGhostInsertIndex = -1;
            _lastGhostParent = null;

            if (_draggedElement != null)
            {
                _draggedElement.style.display = DisplayStyle.Flex;
                _draggedElement.style.opacity = 1.0f;
            }

            if (_dragGhost != null)
            {
                _dragGhost.style.visibility = Visibility.Hidden;
            }

            _isDragging = false;
            _draggedElement = null;
            _draggedData = null;
            _activeDragType = DragType.None;
        }

        private void MarkSettingsDirty()
        {
            if (_settings != null)
            {
                EditorUtility.SetDirty(_settings);
            }
        }

        private void LoadUserStateFromFile()
        {
            if (File.Exists(_userStateFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_userStateFilePath);
                    _userState = JsonUtility.FromJson<DataVisualizerUserState>(json);
                    if (_userState == null)
                    {
                        Debug.LogWarning(
                            $"User state file '{_userStateFilePath}' was empty or invalid. Creating new state."
                        );
                        _userState = new DataVisualizerUserState();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(
                        $"Error loading user state from '{_userStateFilePath}': {e}. Using default state."
                    );
                    _userState = new DataVisualizerUserState();
                }
            }
            else
            {
                _userState = new DataVisualizerUserState();
            }
            _userStateDirty = false;
        }

        private void SaveUserStateToFile()
        {
            if (_userState == null)
            {
                return;
            }

            try
            {
                string json = JsonUtility.ToJson(_userState, true);
                File.WriteAllText(_userStateFilePath, json);
                _userStateDirty = false;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error saving user state to '{_userStateFilePath}': {e}");
            }
        }

        private void MarkUserStateDirty()
        {
            if (_settings.persistStateInSettingsAsset)
            {
                return;
            }

            _userStateDirty = true;
            SaveUserStateToFile();
        }

        private void UpdateAndSaveObjectOrderList(Type type, List<ScriptableObject> orderedObjects)
        {
            if (type == null || orderedObjects == null)
            {
                return;
            }

            List<string> orderedGuids = new();
            foreach (ScriptableObject obj in orderedObjects)
            {
                if (obj == null)
                {
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    if (!string.IsNullOrWhiteSpace(guid))
                    {
                        orderedGuids.Add(guid);
                    }
                }
                else
                {
                    Debug.LogWarning(
                        $"Cannot get path/GUID for object '{obj.name}' during order save."
                    );
                }
            }

            SetObjectOrderForType(type.FullName, orderedGuids);
        }

        private void MigratePersistenceState(bool migrateToSettingsAsset)
        {
            if (_settings == null)
            {
                return;
            }

            if (!migrateToSettingsAsset && _userState == null)
            {
                LoadUserStateFromFile();
                if (_userState == null)
                {
                    return;
                }
            }

            try
            {
                if (migrateToSettingsAsset)
                {
                    if (_userState == null)
                    {
                        return;
                    }

                    _settings.HydrateFrom(_userState);
                    MarkSettingsDirty();
                    Debug.Log("Migration to Settings Object complete.");
                }
                else
                {
                    _userState ??= new DataVisualizerUserState();
                    _userState.HydrateFrom(_settings);
                    MarkUserStateDirty();
                    Debug.Log("Migration to User File complete.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during persistence state migration: {e}");
            }
        }

        private string GetLastSelectedNamespaceKey()
        {
            if (_settings == null)
            {
                return null;
            }

            return _settings.persistStateInSettingsAsset
                ? _settings.lastSelectedNamespaceKey
                : _userState?.lastSelectedNamespaceKey;
        }

        private List<string> GetManagedTypeNames()
        {
            if (_settings == null)
            {
                return new List<string>();
            }

            if (_settings.persistStateInSettingsAsset)
            {
                return _settings.managedTypeNames
                    ?? (_settings.managedTypeNames = new List<string>());
            }
            if (_userState == null)
            {
                LoadUserStateFromFile();
            }

            return _userState!.managedTypeNames
                ?? (_userState.managedTypeNames = new List<string>());
        }

        private List<string> GetObjectOrderForType(string typeFullName)
        {
            if (_settings == null || string.IsNullOrWhiteSpace(typeFullName))
            {
                return new List<string>();
            }

            if (_settings.persistStateInSettingsAsset)
            {
                TypeObjectOrder entry = _settings.objectOrders?.Find(o =>
                    string.Equals(o.TypeFullName, typeFullName, StringComparison.Ordinal)
                );
                return entry?.ObjectGuids ?? new List<string>();
            }
            else
            {
                if (_userState == null)
                {
                    LoadUserStateFromFile();
                }

                TypeObjectOrder entry = _userState!.objectOrders?.Find(o =>
                    string.Equals(o.TypeFullName, typeFullName, StringComparison.Ordinal)
                );
                return entry?.ObjectGuids ?? new List<string>();
            }
        }

        private void SetObjectOrderForType(string typeFullName, List<string> objectGuids)
        {
            if (_settings == null || string.IsNullOrWhiteSpace(typeFullName) || objectGuids == null)
            {
                return;
            }

            if (_settings.persistStateInSettingsAsset)
            {
                List<string> entryList = _settings.GetOrCreateObjectOrderList(typeFullName);
                if (entryList.SequenceEqual(objectGuids))
                {
                    return;
                }

                entryList.Clear();
                entryList.AddRange(objectGuids);
                MarkSettingsDirty();
            }
            else if (_userState != null)
            {
                List<string> entryList = _userState.GetOrCreateObjectOrderList(typeFullName);
                if (entryList.SequenceEqual(objectGuids))
                {
                    return;
                }

                entryList.Clear();
                entryList.AddRange(objectGuids);
                MarkUserStateDirty();
            }
        }

        private void SetLastSelectedNamespaceKey(string value)
        {
            if (_settings == null)
            {
                return;
            }

            if (_settings.persistStateInSettingsAsset)
            {
                if (
                    string.Equals(
                        _settings.lastSelectedNamespaceKey,
                        value,
                        StringComparison.Ordinal
                    )
                )
                {
                    return;
                }

                _settings.lastSelectedNamespaceKey = value;
                MarkSettingsDirty();
            }
            else if (_userState != null)
            {
                if (
                    string.Equals(
                        _userState.lastSelectedNamespaceKey,
                        value,
                        StringComparison.Ordinal
                    )
                )
                {
                    return;
                }

                _userState.lastSelectedNamespaceKey = value;
                MarkUserStateDirty();
            }
        }

        private string GetLastSelectedTypeName()
        {
            if (_settings == null)
            {
                return null;
            }

            return _settings.persistStateInSettingsAsset
                ? _settings.lastSelectedTypeName
                : _userState?.lastSelectedTypeName;
        }

        private void SetLastSelectedTypeName(string value)
        {
            if (_settings == null)
            {
                return;
            }

            if (_settings.persistStateInSettingsAsset)
            {
                if (string.Equals(_settings.lastSelectedTypeName, value, StringComparison.Ordinal))
                {
                    return;
                }

                _settings.lastSelectedTypeName = value;
                MarkSettingsDirty();
            }
            else if (_userState != null)
            {
                if (string.Equals(_userState.lastSelectedTypeName, value, StringComparison.Ordinal))
                {
                    return;
                }

                _userState.lastSelectedTypeName = value;
                MarkUserStateDirty();
            }
        }

        private string GetLastSelectedObjectGuidForType(string typeName)
        {
            if (_settings == null || string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            if (_settings.persistStateInSettingsAsset)
            {
                return _settings.GetLastObjectForType(typeName);
            }

            if (_userState == null)
            {
                LoadUserStateFromFile();
            }

            return _userState?.GetLastObjectForType(typeName);
        }

        private void SetLastSelectedObjectGuidForType(string typeName, string objectGuid)
        {
            if (_settings == null || string.IsNullOrWhiteSpace(typeName))
            {
                return;
            }

            if (_settings.persistStateInSettingsAsset)
            {
                _settings.SetLastObjectForType(typeName, objectGuid);
                MarkSettingsDirty();
            }
            else if (_userState != null)
            {
                _userState.SetLastObjectForType(typeName, objectGuid);
                MarkUserStateDirty();
            }
        }

        private List<string> GetNamespaceOrder()
        {
            if (_settings == null)
            {
                return new List<string>();
            }

            if (_settings.persistStateInSettingsAsset)
            {
                return _settings.namespaceOrder ?? (_settings.namespaceOrder = new List<string>());
            }
            if (_userState == null)
            {
                LoadUserStateFromFile();
            }

            _userState!.namespaceOrder ??= new List<string>();
            return _userState.namespaceOrder;
        }

        private void SetNamespaceOrder(List<string> value)
        {
            if (_settings == null || value == null)
            {
                return;
            }

            if (_settings.persistStateInSettingsAsset)
            {
                if (
                    _settings.namespaceOrder != null
                    && _settings.namespaceOrder.SequenceEqual(value)
                )
                {
                    return;
                }

                _settings.namespaceOrder = new List<string>(value);
                MarkSettingsDirty();
            }
            else if (_userState != null)
            {
                if (
                    _userState.namespaceOrder != null
                    && _userState.namespaceOrder.SequenceEqual(value)
                )
                {
                    return;
                }

                _userState.namespaceOrder = new List<string>(value);
                MarkUserStateDirty();
            }
        }

        private List<string> GetTypeOrderForNamespace(string namespaceKey)
        {
            if (_settings == null || string.IsNullOrWhiteSpace(namespaceKey))
            {
                return new List<string>();
            }

            if (_settings.persistStateInSettingsAsset)
            {
                NamespaceTypeOrder entry = _settings.typeOrder?.Find(o =>
                    string.Equals(o.namespaceKey, namespaceKey, StringComparison.Ordinal)
                );
                return entry?.typeNames ?? new List<string>();
            }
            else
            {
                if (_userState == null)
                {
                    LoadUserStateFromFile();
                }

                NamespaceTypeOrder entry = _userState!.typeOrders?.Find(o =>
                    string.Equals(o.namespaceKey, namespaceKey, StringComparison.Ordinal)
                );
                return entry?.typeNames ?? new List<string>();
            }
        }

        private void SetTypeOrderForNamespace(string namespaceKey, List<string> typeNames)
        {
            if (_settings == null || string.IsNullOrWhiteSpace(namespaceKey) || typeNames == null)
            {
                return;
            }

            if (_settings.persistStateInSettingsAsset)
            {
                List<string> entryList = _settings.GetOrCreateTypeOrderList(namespaceKey);
                if (entryList.SequenceEqual(typeNames))
                {
                    return;
                }

                entryList.Clear();
                entryList.AddRange(typeNames);
                MarkSettingsDirty();
            }
            else if (_userState != null)
            {
                List<string> entryList = _userState.GetOrCreateTypeOrderList(namespaceKey);
                if (entryList.SequenceEqual(typeNames))
                {
                    return;
                }

                entryList.Clear();
                entryList.AddRange(typeNames);
                MarkUserStateDirty();
            }
        }

        private bool GetIsNamespaceCollapsed(string namespaceKey)
        {
            if (_settings == null || string.IsNullOrWhiteSpace(namespaceKey))
            {
                return false;
            }

            if (_settings.persistStateInSettingsAsset)
            {
                NamespaceCollapseState entry = _settings.namespaceCollapseStates?.Find(o =>
                    string.Equals(o.namespaceKey, namespaceKey, StringComparison.Ordinal)
                );
                return entry?.isCollapsed ?? false;
            }
            else
            {
                if (_userState == null)
                {
                    LoadUserStateFromFile();
                }

                NamespaceCollapseState entry = _userState!.namespaceCollapseStates?.Find(o =>
                    string.Equals(o.namespaceKey, namespaceKey, StringComparison.Ordinal)
                );
                return entry?.isCollapsed ?? false;
            }
        }

        private void SetIsNamespaceCollapsed(string namespaceKey, bool isCollapsed)
        {
            if (_settings == null || string.IsNullOrWhiteSpace(namespaceKey))
            {
                return;
            }

            if (_settings.persistStateInSettingsAsset)
            {
                NamespaceCollapseState entry = _settings.GetOrCreateCollapseState(namespaceKey);
                if (entry.isCollapsed == isCollapsed)
                {
                    return;
                }

                entry.isCollapsed = isCollapsed;
                MarkSettingsDirty();
            }
            else if (_userState != null)
            {
                NamespaceCollapseState entry = _userState.GetOrCreateCollapseState(namespaceKey);
                if (entry.isCollapsed == isCollapsed)
                {
                    return;
                }

                entry.isCollapsed = isCollapsed;
                MarkUserStateDirty();
            }
        }

        private static int CompareUsingCustomOrder(
            string keyA,
            string keyB,
            List<string> customOrder
        )
        {
            int indexA = customOrder.IndexOf(keyA);
            int indexB = customOrder.IndexOf(keyB);

            switch (indexA)
            {
                case >= 0 when indexB >= 0:
                    return indexA.CompareTo(indexB);
                case >= 0:
                    return -1;
            }

            return 0 <= indexB ? 1 : string.Compare(keyA, keyB, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetNamespaceKey(Type type)
        {
            if (
                type.IsAttributeDefined(out CustomDataVisualization attribute)
                && !string.IsNullOrWhiteSpace(attribute.Namespace)
            )
            {
                return attribute.Namespace;
            }
            return type.Namespace?.Split('.').LastOrDefault() ?? "No Namespace";
        }
    }
#endif
}
