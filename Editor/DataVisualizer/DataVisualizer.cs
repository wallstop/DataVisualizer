// ReSharper disable AccessToModifiedClosure
// ReSharper disable AccessToDisposedClosure
namespace WallstopStudios.DataVisualizer.Editor
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
    using Search;
#if ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using Sirenix.OdinInspector.Editor;
#endif
    using Styles;
    using UI;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Utilities;
    using Helper;
    using Object = UnityEngine.Object;

    public sealed class DataVisualizer : EditorWindow
    {
        private const string PackageId = "com.wallstop-studios.data-visualizer";
        private const string PrefsPrefix = "WallstopStudios.Editor.DataVisualizer.";

        private const string PrefsSplitterOuterKey = PrefsPrefix + "SplitterOuterFixedPaneWidth";
        private const string PrefsSplitterInnerKey = PrefsPrefix + "SplitterInnerFixedPaneWidth";
        private const string PrefsInitialSizeAppliedKey = PrefsPrefix + "InitialSizeApplied";

        private const string SettingsDefaultPath = "Assets/DataVisualizerSettings.asset";
        private const string UserStateFileName = "DataVisualizerUserState.json";

        private const string NamespaceItemClass = "namespace-item";
        private const string NamespaceGroupHeaderClass = "namespace-group-header";
        private const string NamespaceIndicatorClass = "namespace-indicator";
        private const string ObjectItemClass = "object-item";
        private const string ObjectItemContentClass = "object-item-content";
        private const string ObjectItemActionsClass = "object-item-actions";
        private const string PopoverListItemClassName = "type-selection-list-item";

        private const string PopoverListItemDisabledClassName =
            "type-selection-list-item--disabled";

        private const string PopoverListNamespaceClassName = "type-selection-list-namespace";
        private const string PopoverNamespaceHeaderClassName = "popover-namespace-header";
        private const string PopoverNamespaceIndicatorClassName = "popover-namespace-indicator";
        private const string SearchResultItemClass = "search-result-item";
        private const string SearchResultHighlightClass = "search-result-item--highlighted";
        private const string PopoverHighlightClass = "popover-item--highlighted";

        private const string SearchPlaceholder = "Search...";

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

        private static readonly StringBuilder CachedStringBuilder = new();

        internal DataVisualizerUserState UserState
        {
            get
            {
#pragma warning disable CS0618 // Type or member is obsolete
                if (_userState == null)
                {
                    LoadUserStateFromFile();
                }

                return _userState;
#pragma warning restore CS0618 // Type or member is obsolete
            }
        }

        internal DataVisualizerSettings Settings
        {
            get
            {
#pragma warning disable CS0618 // Type or member is obsolete
                if (_settings == null)
                {
                    _settings = LoadOrCreateSettings();
                }

                return _settings;
#pragma warning restore CS0618 // Type or member is obsolete
            }
        }

        private readonly Dictionary<string, List<Type>> _scriptableObjectTypes = new(
            StringComparer.Ordinal
        );

        private readonly Dictionary<string, int> _namespaceOrder = new(StringComparer.Ordinal);

        private readonly Dictionary<ScriptableObject, VisualElement> _objectVisualElementMap =
            new();

        private readonly List<ScriptableObject> _selectedObjects = new();
        private readonly List<ScriptableObject> _allManagedSOsCache = new();
        private readonly List<VisualElement> _currentSearchResultItems = new();
        private readonly List<VisualElement> _currentTypePopoverItems = new();

        private readonly NamespaceController _namespaceController;

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
        private VisualElement _createPopover;
        private VisualElement _confirmDeletePopover;
        private VisualElement _confirmActionPopover;
        private VisualElement _typeAddPopover;
        private VisualElement _activePopover;
        private VisualElement _confirmNamespaceAddPopover;
        private VisualElement _activeNestedPopover;
        private object _popoverContext;
        private bool _isDraggingPopover;
        private Vector2 _popoverDragStartMousePos;
        private Vector2 _popoverDragStartPos;

        private TextField _searchField;
        private VisualElement _searchPopover;
        private bool _isSearchCachePopulated;
        private string _lastSearchString;

        private Button _addTypeButton;
        private Button _settingsButton;
        private TextField _typeSearchField;
        private VisualElement _typePopoverListContainer;

        private float _lastSavedOuterWidth = -1f;
        private float _lastSavedInnerWidth = -1f;
        private IVisualElementScheduledItem _saveWidthsTask;

        private int _searchHighlightIndex = -1;
        private int _typePopoverHighlightIndex = -1;
        private string _lastTypeAddSearchTerm;
        private FocusArea _lastActiveFocusArea = FocusArea.None;
        private DragType _activeDragType = DragType.None;
        private object _draggedData;
        private VisualElement _inPlaceGhost;
        private int _lastGhostInsertIndex = -1;
        private VisualElement _lastGhostParent;
        private VisualElement _draggedElement;
        private VisualElement _dragGhost;
        private Vector2 _dragStartPosition;
        internal bool _isDragging;
        private float _lastDragUpdateTime;
        private SerializedObject _currentInspectorScriptableObject;

        private string _userStateFilePath;

        [Obsolete("Use UserState instead.")]
        private DataVisualizerUserState _userState;

        private bool _userStateDirty;

        [Obsolete("User Settings instead.")]
        private DataVisualizerSettings _settings;

        private List<Type> _relevantScriptableObjectTypes;

        private float? _lastAddTypeClicked;
        private float? _lastSettingsClicked;
        private float? _lastEnterPressed;

        private Label _dataFolderPathDisplay;
#if ODIN_INSPECTOR
        private PropertyTree _odinPropertyTree;
        private IMGUIContainer _odinInspectorContainer;
        private IVisualElementScheduledItem _odinRepaintSchedule;
#endif

        public DataVisualizer()
        {
            _namespaceController = new NamespaceController(_scriptableObjectTypes, _namespaceOrder);
        }

        [MenuItem("Tools/Wallstop Studios/Data Visualizer")]
        public static void ShowWindow()
        {
            DataVisualizer window = GetWindow<DataVisualizer>("Data Visualizer");
            window.titleContent = new GUIContent("Data Visualizer");

            bool initialSizeApplied = EditorPrefs.GetBool(PrefsInitialSizeAppliedKey, false);
            if (initialSizeApplied)
            {
                return;
            }

            float width = Mathf.Max(800, window.position.width);
            float height = Mathf.Max(400, window.position.height);
            Rect monitorArea = MonitorUtility.GetPrimaryMonitorRect();

            float centerX = (monitorArea.width - width) / 2f;
            float centerY = (monitorArea.height - height) / 2f;

            float x = Mathf.Max(0, centerX);
            float y = Mathf.Max(0, centerY);

            window.position = new Rect(x, y, width, height);
            EditorPrefs.SetBool(PrefsInitialSizeAppliedKey, true);
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
            _userStateFilePath = Path.Combine(Application.persistentDataPath, UserStateFileName);

            LoadScriptableObjectTypes();
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
            Cleanup();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            _selectedElement = null;
            _selectedObject = null;
            _namespaceController.SelectType(this, null);
            _scriptableObjectTypes.Clear();
            _namespaceOrder.Clear();
            _namespaceController.Clear();
            _allManagedSOsCache.Clear();
            _currentSearchResultItems.Clear();
            _currentTypePopoverItems.Clear();
            _isSearchCachePopulated = false;
            CloseActivePopover();
            CancelDrag();
            _saveWidthsTask?.Pause();
            if (!Settings.persistStateInSettingsAsset && _userStateDirty)
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
            _allManagedSOsCache.Clear();
            HashSet<Type> managedTypes = _scriptableObjectTypes
                .SelectMany(tuple => tuple.Value)
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

        private void SyncNamespaceAndTypeOrders()
        {
            List<string> namespaceOrder = _namespaceOrder
                .OrderBy(kvp => kvp.Value)
                .Select(kvp => kvp.Key)
                .ToList();
            List<NamespaceTypeOrder> typeOrder = _namespaceOrder
                .OrderBy(kvp => kvp.Value)
                .Select(kvp => new NamespaceTypeOrder
                {
                    namespaceKey = kvp.Key,
                    typeNames = _scriptableObjectTypes[kvp.Key]
                        .Select(type => type.FullName)
                        .ToList(),
                })
                .ToList();
            PersistSettings(
                settings =>
                {
                    settings.namespaceOrder = namespaceOrder;
                    settings.typeOrders = typeOrder;
                    return true;
                },
                userState =>
                {
                    userState.namespaceOrder = namespaceOrder;
                    userState.typeOrders = typeOrder;
                    return true;
                }
            );
        }

        internal void PersistSettings(
            Func<DataVisualizerSettings, bool> settingsApplier,
            Func<DataVisualizerUserState, bool> userStateApplier
        )
        {
            DataVisualizerSettings settings = Settings;
            if (settings.persistStateInSettingsAsset)
            {
                if (settingsApplier(settings))
                {
                    settings.MarkDirty();
                    AssetDatabase.SaveAssets();
                }
            }
            else if (userStateApplier(UserState))
            {
                MarkUserStateDirty();
            }
        }

        private void RefreshAllViews()
        {
            Type selectedType = _namespaceController.SelectedType;

            string previousNamespaceKey =
                selectedType != null
                    ? NamespaceController.GetNamespaceKey(selectedType)
                    : string.Empty;
            string previousTypeName = selectedType?.Name;
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

            int namespaceIndex = -1;
            if (!string.IsNullOrWhiteSpace(previousNamespaceKey))
            {
                namespaceIndex = _namespaceOrder.GetValueOrDefault(previousNamespaceKey, -1);
            }

            if (namespaceIndex < 0 && 0 < _scriptableObjectTypes.Count)
            {
                namespaceIndex = 0;
            }

            if (0 <= namespaceIndex)
            {
                List<Type> typesInNamespace = _scriptableObjectTypes.GetValueOrDefault(
                    previousNamespaceKey,
                    null
                );
                if (0 < typesInNamespace?.Count)
                {
                    if (!string.IsNullOrWhiteSpace(previousTypeName))
                    {
                        selectedType = typesInNamespace.FirstOrDefault(t =>
                            string.Equals(t.Name, previousTypeName, StringComparison.Ordinal)
                        );
                    }

                    selectedType ??= typesInNamespace[0];
                }
                else
                {
                    Debug.LogWarning(
                        $"Failed to find any types for namespace {previousNamespaceKey}."
                    );
                }
            }

            if (selectedType != null)
            {
                LoadObjectTypes(selectedType);
            }
            else
            {
                _selectedObjects.Clear();
            }

            ScriptableObject selectedObject = _selectedObject;
            if (
                selectedType != null
                && !string.IsNullOrWhiteSpace(previousObjectGuid)
                && 0 < _selectedObjects.Count
            )
            {
                selectedObject = _selectedObjects.Find(obj =>
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

            VisualElement typeElementToSelect = FindTypeElement(selectedType);
            if (typeElementToSelect != null)
            {
                VisualElement ancestorGroup = FindAncestorNamespaceGroup(typeElementToSelect);
                if (ancestorGroup != null)
                {
                    ExpandNamespaceGroupIfNeeded(ancestorGroup, false);
                }
            }

            SelectObject(selectedObject);
            PopulateSearchCache();
            _namespaceController.SelectType(this, selectedType);
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
                    DataVisualizerSettings newSettings =
                        AssetDatabase.LoadAssetAtPath<DataVisualizerSettings>(SettingsDefaultPath);
                    if (newSettings != null)
                    {
                        settings = newSettings;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to create DataVisualizerSettings asset. {e}");
                }
            }

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
                namespaceIndex = _namespaceOrder.GetValueOrDefault(savedNamespaceKey, -1);
            }

            if (0 <= namespaceIndex)
            {
                typesInNamespace = _scriptableObjectTypes.GetValueOrDefault(
                    savedNamespaceKey,
                    null
                );
            }
            else if (0 < _namespaceOrder.Count)
            {
                int bestIndex = _scriptableObjectTypes.Count;
                string bestNamespace = null;
                foreach (KeyValuePair<string, int> entry in _namespaceOrder)
                {
                    if (entry.Value < bestIndex)
                    {
                        bestNamespace = entry.Key;
                    }
                }

                typesInNamespace = _scriptableObjectTypes.GetValueOrDefault(bestNamespace, null);
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
                    string.Equals(t.FullName, savedTypeName, StringComparison.Ordinal)
                );
            }

            selectedType ??= typesInNamespace[0];
            LoadObjectTypes(selectedType);
            BuildNamespaceView();
            BuildObjectsView();

            VisualElement typeElementToSelect = FindTypeElement(selectedType);
            if (typeElementToSelect != null)
            {
                VisualElement ancestorGroup = null;
                VisualElement currentElement = typeElementToSelect;
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
            if (selectedType != null)
            {
                savedObjectGuid = GetLastSelectedObjectGuidForType(selectedType.Name);
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

            _settingsButton = new Button(() => TogglePopover(_settingsPopover, _settingsButton))
            {
                text = "…",
                name = "settings-button",
                tooltip = "Open Settings",
            };

            _settingsButton.AddToClassList("settings-button");
            _settingsButton.AddToClassList(StyleConstants.ClickableClass);
            headerRow.Add(_settingsButton);

            _searchField = new TextField
            {
                name = "global-search-field",
                style = { flexGrow = 1, marginRight = 10 },
            };
            _searchField.SetPlaceholderText(SearchPlaceholder);
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

            _createPopover = CreatePopoverBase("create-popover");
            root.Add(_createPopover);
            _renamePopover = CreatePopoverBase("rename-popover");
            root.Add(_renamePopover);
            _confirmDeletePopover = CreatePopoverBase("confirm-delete-popover");
            root.Add(_confirmDeletePopover);
            _confirmActionPopover = CreatePopoverBase("confirm-action-popover");
            root.Add(_confirmActionPopover);
            _searchPopover = new VisualElement { name = "search-popover" };
            _searchPopover.AddToClassList("search-popover");
            root.Add(_searchPopover);

            _typeAddPopover = new VisualElement { name = "type-add-popover" };
            _typeAddPopover.AddToClassList("type-add-popover");

            _typeSearchField = new TextField { name = "type-search-field" };
            _typeSearchField.AddToClassList("type-search-field");
            _typeSearchField.SetPlaceholderText(SearchPlaceholder);
            _typeSearchField.RegisterValueChangedCallback(evt => BuildTypeAddList(evt.newValue));
            _typeSearchField.RegisterCallback<KeyDownEvent>(HandleTypePopoverKeyDown);
            _typeAddPopover.Add(_typeSearchField);

            ScrollView typePopoverScrollView = new(ScrollViewMode.Vertical);
            typePopoverScrollView.AddToClassList("type-add-popover-scrollview");
            _typeAddPopover.Add(typePopoverScrollView);

            _typePopoverListContainer = new VisualElement { name = "type-add-list-content" };
            _typePopoverListContainer.AddToClassList("type-add-list-container");
            typePopoverScrollView.Add(_typePopoverListContainer);

            root.Add(_typeAddPopover);

            _confirmNamespaceAddPopover = CreatePopoverBase("confirm-namespace-add-popover");
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

                const string packageCache = "PackageCache";
                int packageCacheIndex = unityRelativeStyleSheetPath.IndexOf(
                    packageCache,
                    StringComparison.OrdinalIgnoreCase
                );
                if (0 <= packageCacheIndex)
                {
                    unityRelativeStyleSheetPath = unityRelativeStyleSheetPath[
                        (packageCacheIndex + packageCache.Length)..
                    ];
                    unityRelativeStyleSheetPath = "Packages" + unityRelativeStyleSheetPath;
                }
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
                packageCacheIndex = unityRelativeFontPath.IndexOf(
                    packageCache,
                    StringComparison.OrdinalIgnoreCase
                );
                if (0 <= packageCacheIndex)
                {
                    unityRelativeFontPath = unityRelativeFontPath[
                        (packageCacheIndex + packageCache.Length)..
                    ];
                    unityRelativeFontPath = "Packages" + unityRelativeFontPath;
                }
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
        }

        private void HandleSearchKeyDown(KeyDownEvent evt)
        {
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
                            _searchField.value = string.Empty;
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
            if (results.Count > 0)
            {
                _searchPopover.style.maxHeight = StyleKeyword.Null;
                foreach ((ScriptableObject resultObj, SearchResultMatchInfo resultInfo) in results)
                {
                    List<string> termsMatchingThisObject = resultInfo.AllMatchedTerms.ToList();
                    VisualElement resultItem = new() { name = "result-item", userData = resultObj };
                    resultItem.AddToClassList(SearchResultItemClass);
                    resultItem.AddToClassList(StyleConstants.ClickableClass);
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
                        _searchField.value = string.Empty;
                        evt.StopPropagation();
                    });

                    VisualElement mainInfoRow = new()
                    {
                        style =
                        {
                            flexDirection = FlexDirection.Row,
                            justifyContent = Justify.SpaceBetween,
                        },
                    };
                    mainInfoRow.AddToClassList(StyleConstants.ClickableClass);

                    Label nameLabel = CreateHighlightedLabel(
                        resultObj.name,
                        termsMatchingThisObject,
                        "result-name-label",
                        bindToContextHovers: true,
                        resultItem,
                        mainInfoRow
                    );
                    nameLabel.AddToClassList("search-result-name-label");
                    nameLabel.AddToClassList(StyleConstants.ClickableClass);

                    Label typeLabel = CreateHighlightedLabel(
                        resultObj.GetType().Name,
                        termsMatchingThisObject,
                        "result-type-label",
                        bindToContextHovers: true,
                        resultItem,
                        mainInfoRow
                    );
                    typeLabel.AddToClassList("search-result-type-label");
                    typeLabel.AddToClassList(StyleConstants.ClickableClass);

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
                                && mf.fieldName != MatchSource.Guid
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
                                "search-result-context-label",
                                bindToContextHovers: true,
                                resultItem,
                                mainInfoRow
                            );
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
                listContainer.Add(
                    new Label("No matching objects found.")
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
                _searchPopover.style.maxHeight = StyleKeyword.None;
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
                        new MatchDetail(term) { fieldName = MatchSource.Guid, matchedValue = guid }
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
            string baseStyleClass,
            bool bindToContextHovers = false,
            params VisualElement[] contexts
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

            label.text = GenerateContents(false);
            label.RegisterCallback<MouseOverEvent>(_ =>
            {
                label.text = GenerateContents(true);
            });
            label.RegisterCallback<MouseOutEvent>(_ =>
            {
                label.text = GenerateContents(false);
            });
            if (bindToContextHovers)
            {
                foreach (VisualElement context in contexts)
                {
                    context.RegisterCallback<MouseOverEvent>(_ =>
                    {
                        label.text = GenerateContents(true);
                    });
                    context.RegisterCallback<MouseOutEvent>(_ =>
                    {
                        label.text = GenerateContents(false);
                    });
                }
            }

            return label;

            string GenerateContents(bool hovering)
            {
                CachedStringBuilder.Clear();
                int currentIndex = 0;
                bool colorify = !hovering;
                foreach ((int startIndex, int length) in matches)
                {
                    if (startIndex < currentIndex)
                    {
                        continue;
                    }

                    CachedStringBuilder.Append(
                        EscapeRichText(fullText.Substring(currentIndex, startIndex - currentIndex))
                    );
                    if (colorify)
                    {
                        CachedStringBuilder.Append("<color=yellow>");
                    }

                    CachedStringBuilder.Append("<b>");
                    CachedStringBuilder.Append(
                        EscapeRichText(fullText.Substring(startIndex, length))
                    );
                    CachedStringBuilder.Append("</b>");
                    if (colorify)
                    {
                        CachedStringBuilder.Append("</color>");
                    }

                    currentIndex = startIndex + length;
                }

                if (currentIndex < fullText.Length)
                {
                    CachedStringBuilder.Append(EscapeRichText(fullText.Substring(currentIndex)));
                }

                return CachedStringBuilder.ToString();
            }
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
            bool typeChanged = _namespaceController.SelectedType != targetType;
            _namespaceController.SelectType(this, targetType);

            if (typeChanged)
            {
                LoadObjectTypes(targetType);
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

        private VisualElement CreatePopoverBase(string popoverName)
        {
            VisualElement popover = new() { name = popoverName };
            popover.AddToClassList("popover");

            VisualElement dragHandle = new() { name = $"{popoverName}-drag-handle" };
            dragHandle.AddToClassList("popover-drag-handle");
            dragHandle.RegisterCallback<PointerDownEvent>(OnPopoverDragHandlePointerDown);
            popover.Add(dragHandle);
            VisualElement contentWrapper = new()
            {
                name = $"{popoverName}-content-wrapper",
                style =
                {
                    flexGrow = 1,
                    paddingBottom = 5,
                    paddingLeft = 5,
                    paddingRight = 5,
                    paddingTop = 5,
                },
            };
            popover.Add(contentWrapper);
            return popover;
        }

        private void OnPopoverDragHandlePointerDown(PointerDownEvent evt)
        {
            VisualElement target = _activeNestedPopover ?? _activePopover;
            VisualElement handle = evt.currentTarget as VisualElement;
            VisualElement popover = handle?.parent;
            if (popover == null || popover != target)
            {
                return;
            }

            if (evt.button == 0)
            {
                _isDraggingPopover = true;
                _popoverDragStartMousePos = evt.position;
                _popoverDragStartPos = new Vector2(
                    popover.resolvedStyle.left,
                    popover.resolvedStyle.top
                );

                popover.CapturePointer(evt.pointerId);
                popover.RegisterCallback<PointerMoveEvent>(OnPopoverPointerMove);
                popover.RegisterCallback<PointerUpEvent>(OnPopoverPointerUp);
                popover.RegisterCallback<PointerCaptureOutEvent>(OnPopoverPointerCaptureOut);
                evt.StopPropagation();
            }
        }

        private void OnPopoverPointerMove(PointerMoveEvent evt)
        {
            VisualElement popover = _activeNestedPopover ?? _activePopover;
            if (!_isDraggingPopover || popover == null || !popover.HasPointerCapture(evt.pointerId))
            {
                return;
            }

            Vector2 mouseDelta = (Vector2)evt.position - _popoverDragStartMousePos;

            float targetX = _popoverDragStartPos.x + mouseDelta.x;
            float targetY = _popoverDragStartPos.y + mouseDelta.y;

            float popoverWidth = popover.resolvedStyle.width;
            float popoverHeight = popover.resolvedStyle.height;
            if (float.IsNaN(popoverWidth) || popoverWidth <= 0)
            {
                popoverWidth = 300f;
            }

            if (float.IsNaN(popoverHeight) || popoverHeight <= 0)
            {
                popoverHeight = 150f;
            }
            float windowWidth = rootVisualElement.resolvedStyle.width;
            float windowHeight = rootVisualElement.resolvedStyle.height;

            float clampedX = targetX,
                clampedY = targetY;
            if (
                !float.IsNaN(windowWidth)
                && !float.IsNaN(windowHeight)
                && windowWidth > 0
                && windowHeight > 0
            )
            {
                clampedX = Mathf.Max(0, targetX);
                clampedX = Mathf.Min(clampedX, windowWidth - popoverWidth);
                clampedX = Mathf.Max(0, clampedX);

                clampedY = Mathf.Max(0, targetY);
                clampedY = Mathf.Min(clampedY, windowHeight - popoverHeight);
                clampedY = Mathf.Max(0, clampedY);
            }

            popover.style.left = clampedX;
            popover.style.top = clampedY;
        }

        private void OnPopoverPointerUp(PointerUpEvent evt)
        {
            VisualElement popover = _activeNestedPopover ?? _activePopover;
            if (!_isDraggingPopover || popover == null || !popover.HasPointerCapture(evt.pointerId))
            {
                return;
            }

            _isDraggingPopover = false;
            popover.ReleasePointer(evt.pointerId);
            popover.UnregisterCallback<PointerMoveEvent>(OnPopoverPointerMove);
            popover.UnregisterCallback<PointerUpEvent>(OnPopoverPointerUp);
            popover.UnregisterCallback<PointerCaptureOutEvent>(OnPopoverPointerCaptureOut);
            evt.StopPropagation();
        }

        private void OnPopoverPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            VisualElement popover = _activeNestedPopover ?? _activePopover;
            if (_isDraggingPopover && popover != null)
            {
                _isDraggingPopover = false;
                popover.UnregisterCallback<PointerMoveEvent>(OnPopoverPointerMove);
                popover.UnregisterCallback<PointerUpEvent>(OnPopoverPointerUp);
                popover.UnregisterCallback<PointerCaptureOutEvent>(OnPopoverPointerCaptureOut);
            }
        }

        internal void BuildAndOpenConfirmationPopover(
            string message,
            string confirmText,
            Action onConfirm,
            VisualElement triggerElement
        )
        {
            if (_confirmActionPopover == null || onConfirm == null)
            {
                return;
            }

            _confirmActionPopover.Clear();

            Label messageLabel = new(message)
            {
                style = { whiteSpace = WhiteSpace.Normal, marginBottom = 15 },
            };
            _confirmActionPopover.Add(messageLabel);

            VisualElement buttonContainer = new();
            buttonContainer.AddToClassList("popover-button-container");
            _confirmActionPopover.Add(buttonContainer);

            Button cancelButton = new(CloseActivePopover) { text = "Cancel" };
            cancelButton.AddToClassList(StyleConstants.PopoverButtonClass);
            cancelButton.AddToClassList(StyleConstants.PopoverCancelButtonClass);
            cancelButton.AddToClassList(StyleConstants.ClickableClass);
            buttonContainer.Add(cancelButton);

            Button confirmButton = new(() =>
            {
                onConfirm();
                CloseActivePopover();
            })
            {
                text = confirmText,
            };
            confirmButton.AddToClassList(StyleConstants.PopoverButtonClass);
            confirmButton.AddToClassList("popover-delete-button");
            confirmButton.AddToClassList(StyleConstants.ClickableClass);
            buttonContainer.Add(confirmButton);

            OpenPopover(_confirmActionPopover, triggerElement);
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
                    Vector2 triggerPosInRoot = rootVisualElement.WorldToLocal(
                        triggerBounds.position
                    );

                    float popoverWidth = popover.resolvedStyle.width;
                    float popoverHeight = popover.resolvedStyle.height;

                    if (float.IsNaN(popoverWidth) || popoverWidth <= 0)
                    {
                        popoverWidth =
                            popover.style.width.keyword == StyleKeyword.Auto
                            || popover.style.width.value.value == 0
                                ? 350f
                                : popover.style.width.value.value;
                    }
                    if (float.IsNaN(popoverHeight) || popoverHeight <= 0)
                    {
                        popoverHeight =
                            popover.style.height.keyword == StyleKeyword.Auto
                            || popover.style.height.value.value == 0
                                ? 150f
                                : popover.style.height.value.value;
                    }

                    popoverWidth = Mathf.Min(
                        popoverWidth,
                        popover.resolvedStyle.maxWidth.value > 0
                            ? popover.resolvedStyle.maxWidth.value
                            : float.MaxValue
                    );
                    popoverHeight = Mathf.Min(
                        popoverHeight,
                        popover.resolvedStyle.maxHeight.value > 0
                            ? popover.resolvedStyle.maxHeight.value
                            : float.MaxValue
                    );
                    popoverWidth = Mathf.Max(
                        popoverWidth,
                        popover.resolvedStyle.minWidth.value > 0
                            ? popover.resolvedStyle.minWidth.value
                            : 50f
                    );
                    popoverHeight = Mathf.Max(
                        popoverHeight,
                        popover.resolvedStyle.minHeight.value > 0
                            ? popover.resolvedStyle.minHeight.value
                            : 30f
                    );

                    float targetX = triggerPosInRoot.x;
                    float targetY = triggerPosInRoot.y + triggerBounds.height + 2;
                    float windowWidth = rootVisualElement.resolvedStyle.width;
                    float windowHeight = rootVisualElement.resolvedStyle.height;

                    if (
                        float.IsNaN(windowWidth)
                        || float.IsNaN(windowHeight)
                        || windowWidth <= 0
                        || windowHeight <= 0
                    )
                    {
                        popover.style.left = targetX;
                        popover.style.top = targetY;
                    }
                    else
                    {
                        float clampedX = Mathf.Max(0, targetX);
                        clampedX = Mathf.Min(clampedX, windowWidth - popoverWidth);
                        clampedX = Mathf.Max(0, clampedX);
                        float clampedY = Mathf.Max(0, targetY);
                        clampedY = Mathf.Min(clampedY, windowHeight - popoverHeight);
                        clampedY = Mathf.Max(0, clampedY);

                        popover.style.left = clampedX;
                        popover.style.top = clampedY;
                    }
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
            if (_activePopover != null && _activePopover.style.display == DisplayStyle.Flex)
            {
                switch (evt.keyCode)
                {
                    case KeyCode.Escape:
                        CloseActivePopover();
                        evt.PreventDefault();
                        evt.StopPropagation();
                        return;

                    case KeyCode.DownArrow:
                    case KeyCode.UpArrow:
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        if (_lastActiveFocusArea == FocusArea.SearchResultsPopover)
                        {
                            _lastEnterPressed = Time.realtimeSinceStartup;
                            HandleSearchKeyDown(evt);
                            return;
                        }
                        if (_lastActiveFocusArea == FocusArea.AddTypePopover)
                        {
                            _lastEnterPressed = Time.realtimeSinceStartup;
                            HandleTypePopoverKeyDown(evt);
                            return;
                        }

                        break;
                }

                if (evt.keyCode == KeyCode.DownArrow || evt.keyCode == KeyCode.UpArrow)
                {
                    evt.PreventDefault();
                    evt.StopPropagation();
                    return;
                }
            }

            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                {
                    bool navigationHandled = false;
                    switch (_lastActiveFocusArea)
                    {
                        case FocusArea.TypeList:
                        {
                            navigationHandled = true;
                            _namespaceController.IncrementTypeSelection(this);
                            break;
                        }
                    }

                    if (navigationHandled)
                    {
                        evt.PreventDefault();
                        evt.StopPropagation();
                    }

                    break;
                }
                case KeyCode.UpArrow:
                {
                    bool navigationHandled = false;
                    switch (_lastActiveFocusArea)
                    {
                        case FocusArea.TypeList:
                        {
                            navigationHandled = true;
                            _namespaceController.DecrementTypeSelection(this);
                            break;
                        }
                    }

                    if (navigationHandled)
                    {
                        evt.PreventDefault();
                        evt.StopPropagation();
                    }

                    break;
                }
            }
        }

        private void HandleClickOutsidePopover(PointerDownEvent evt)
        {
            VisualElement target = evt.target as VisualElement;
            if (target == _addTypeButton)
            {
                _lastAddTypeClicked = Time.realtimeSinceStartup;
            }
            else if (target == _settingsButton)
            {
                _lastSettingsClicked = Time.realtimeSinceStartup;
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
            VisualElement dragHandle = _settingsPopover.Q(className: "popover-drag-handle");
            VisualElement contentWrapper = _settingsPopover.Q(
                name: $"{_settingsPopover.name}-content-wrapper"
            );
            if (dragHandle == null || contentWrapper == null)
            {
                return;
            }

            dragHandle.AddToClassList("settings");
            dragHandle.Clear();
            contentWrapper.Clear();

            // Add Title to Drag Handle
            dragHandle.Add(
                new Label("Settings")
                {
                    style = { unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 5 },
                }
            );

            Button closeButton = new(CloseActivePopover) { text = "X" };
            closeButton.AddToClassList("popover-close-button");
            closeButton.AddToClassList(StyleConstants.ClickableClass);
            dragHandle.Add(closeButton);

            DataVisualizerSettings settings = Settings;
            ActionButtonToggle prefsToggle = null;
            prefsToggle = new ActionButtonToggle(
                settings.persistStateInSettingsAsset
                    ? "Persist State in UserState:"
                    : "Persist State in Settings Asset:",
                value =>
                {
                    if (prefsToggle != null)
                    {
                        prefsToggle.Label = value
                            ? "Persist State in UserState:"
                            : "Persist State in Settings Asset:";
                    }
                }
            )
            {
                value = settings.persistStateInSettingsAsset,
            };
            prefsToggle.AddToClassList("settings-prefs-toggle");
            prefsToggle.RegisterValueChangedCallback(evt =>
            {
                bool newModeIsSettingsAsset = evt.newValue;
                bool previousModeWasSettingsAsset = Settings.persistStateInSettingsAsset;
                if (previousModeWasSettingsAsset == newModeIsSettingsAsset)
                {
                    return;
                }

                DataVisualizerSettings localSettings = Settings;
                localSettings.persistStateInSettingsAsset = newModeIsSettingsAsset;
                MigratePersistenceState(migrateToSettingsAsset: newModeIsSettingsAsset);
                AssetDatabase.SaveAssets();
                if (!newModeIsSettingsAsset)
                {
                    SaveUserStateToFile();
                }
            });
            contentWrapper.Add(prefsToggle);

            VisualElement dataFolderContainer = new()
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginTop = 10,
                },
            };
            Label dataFolderLabel = new("Data Folder:");
            dataFolderLabel.AddToClassList("settings-data-folder-label");

            dataFolderContainer.Add(dataFolderLabel);
            Label dataFolderPathDisplay = new()
            {
                text = Settings.DataFolderPath,
                name = "data-folder-display",
            };
            dataFolderPathDisplay.AddToClassList("settings-data-folder-path-display");
            dataFolderPathDisplay.AddToClassList(StyleConstants.ClickableClass);
            dataFolderPathDisplay.RegisterCallback<PointerDownEvent, DataVisualizerSettings>(
                (evt, context) =>
                {
                    Object dataFolderPath = AssetDatabase.LoadAssetAtPath<Object>(
                        context.DataFolderPath
                    );
                    if (dataFolderPath != null)
                    {
                        EditorGUIUtility.PingObject(dataFolderPath);
                    }
                },
                Settings
            );
            dataFolderContainer.Add(dataFolderPathDisplay);
            Button selectFolderButton = new(() => SelectDataFolderForPopover(dataFolderPathDisplay))
            {
                text = "Select",
            };
            selectFolderButton.AddToClassList("settings-data-folder-button");
            selectFolderButton.AddToClassList(StyleConstants.ClickableClass);
            dataFolderContainer.Add(selectFolderButton);
            contentWrapper.Add(dataFolderContainer);
        }

        private void SelectDataFolderForPopover(Label displayField)
        {
            if (displayField == null)
            {
                Debug.LogError("Cannot select data folder: Display field reference is null.");
                return;
            }

            string currentRelativePath = Settings.DataFolderPath;
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

            DataVisualizerSettings settings = Settings;
            if (settings.DataFolderPath == relativePath)
            {
                return;
            }

            Debug.Log($"Updating Data Folder from '{settings.DataFolderPath}' to '{relativePath}'");
            settings._dataFolderPath = relativePath;
            settings.MarkDirty();
            AssetDatabase.SaveAssets();
            displayField.text = settings.DataFolderPath;
        }

        private void OpenRenamePopover(VisualElement source, ScriptableObject dataObject)
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
            OpenPopover(_renamePopover, source, currentPath);
        }

        private void BuildCreatePopoverContent(Type type)
        {
            VisualElement dragHandle = _createPopover.Q(className: "popover-drag-handle");
            VisualElement contentWrapper = _createPopover.Q(
                name: $"{_createPopover.name}-content-wrapper"
            );
            if (dragHandle == null || contentWrapper == null)
            {
                return;
            }

            dragHandle.AddToClassList("create");
            dragHandle.Clear();
            contentWrapper.Clear();
            _createPopover.userData = type;

            dragHandle.Add(
                new Label("Create")
                {
                    style = { unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 5 },
                }
            );

            Button closeButton = new(CloseActivePopover) { text = "X" };
            closeButton.AddToClassList("popover-close-button");
            closeButton.AddToClassList(StyleConstants.ClickableClass);
            dragHandle.Add(closeButton);

            Label createLabel = new("Enter new name (without extension)");
            createLabel.AddToClassList("create-object-label");
            contentWrapper.Add(createLabel);
            TextField nameTextField = new()
            {
                value = Path.GetFileNameWithoutExtension(
                    NamespaceController.GetTypeDisplayName(type)
                ),
                name = "create-textfield",
            };
            nameTextField.AddToClassList("create-text-field");
            nameTextField.schedule.Execute(() => nameTextField.SelectAll()).ExecuteLater(50);
            contentWrapper.Add(nameTextField);
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
            contentWrapper.Add(errorLabel);
            VisualElement buttonContainer = new();
            buttonContainer.AddToClassList("popover-button-container");
            Button cancelButton = new(CloseActivePopover) { text = "Cancel" };
            cancelButton.AddToClassList(StyleConstants.PopoverButtonClass);
            cancelButton.AddToClassList(StyleConstants.PopoverCancelButtonClass);
            cancelButton.AddToClassList(StyleConstants.ClickableClass);
            Button createButton = new(() => HandleCreateConfirmed(type, nameTextField, errorLabel))
            {
                text = "Create",
            };
            createButton.AddToClassList(StyleConstants.PopoverButtonClass);
            createButton.AddToClassList("popover-create-button");
            createButton.AddToClassList(StyleConstants.ClickableClass);
            buttonContainer.Add(cancelButton);
            buttonContainer.Add(createButton);
            contentWrapper.Add(buttonContainer);
        }

        private void BuildRenamePopoverContent(string originalPath, string originalName)
        {
            VisualElement dragHandle = _renamePopover.Q(className: "popover-drag-handle");
            VisualElement contentWrapper = _renamePopover.Q(
                name: $"{_renamePopover.name}-content-wrapper"
            );
            if (dragHandle == null || contentWrapper == null)
            {
                return;
            }

            dragHandle.AddToClassList("rename");
            dragHandle.Clear();
            contentWrapper.Clear();
            _renamePopover.userData = originalPath;

            dragHandle.Add(
                new Label("Rename")
                {
                    style = { unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 5 },
                }
            );

            Button closeButton = new(CloseActivePopover) { text = "X" };
            closeButton.AddToClassList("popover-close-button");
            closeButton.AddToClassList(StyleConstants.ClickableClass);
            dragHandle.Add(closeButton);

            Label renameLabel = new("Enter new name (without extension)");
            renameLabel.AddToClassList("rename-object-label");
            contentWrapper.Add(renameLabel);
            TextField nameTextField = new()
            {
                value = Path.GetFileNameWithoutExtension(originalName),
                name = "rename-textfield",
            };
            nameTextField.AddToClassList("rename-text-field");
            nameTextField.schedule.Execute(() => nameTextField.SelectAll()).ExecuteLater(50);
            contentWrapper.Add(nameTextField);
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
            contentWrapper.Add(errorLabel);
            VisualElement buttonContainer = new();
            buttonContainer.AddToClassList("popover-button-container");
            Button cancelButton = new(CloseActivePopover) { text = "Cancel" };
            cancelButton.AddToClassList(StyleConstants.PopoverButtonClass);
            cancelButton.AddToClassList(StyleConstants.PopoverCancelButtonClass);
            cancelButton.AddToClassList(StyleConstants.ClickableClass);
            Button renameButton = new(() => HandleRenameConfirmed(nameTextField, errorLabel))
            {
                text = "Rename",
            };
            renameButton.AddToClassList(StyleConstants.PopoverButtonClass);
            renameButton.AddToClassList("popover-rename-button");
            renameButton.AddToClassList(StyleConstants.ClickableClass);
            buttonContainer.Add(cancelButton);
            buttonContainer.Add(renameButton);
            contentWrapper.Add(buttonContainer);
        }

        private void HandleCreateConfirmed(Type type, TextField nameField, Label errorLabel)
        {
            errorLabel.style.display = DisplayStyle.None;
            string newName = nameField.value;

            if (
                string.IsNullOrWhiteSpace(newName)
                || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            )
            {
                errorLabel.text = "Invalid name.";
                errorLabel.style.display = DisplayStyle.Flex;
                return;
            }

            string directory = Settings.DataFolderPath;

            string proposedName = $"{newName}.asset";
            string proposedPath = Path.Combine(directory, proposedName).SanitizePath();
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(proposedPath);
            if (!string.Equals(proposedPath, uniquePath, StringComparison.Ordinal))
            {
                errorLabel.text = "Name is not unique.";
                errorLabel.style.display = DisplayStyle.Flex;
                return;
            }

            ScriptableObject instance = CreateInstance(type);
            AssetDatabase.CreateAsset(instance, uniquePath);
            CloseActivePopover();
            ScheduleRefresh();
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

        private void OpenConfirmDeletePopover(VisualElement source, ScriptableObject dataObject)
        {
            if (dataObject == null)
            {
                return;
            }

            BuildConfirmDeletePopoverContent(dataObject);
            OpenPopover(_confirmDeletePopover, source, dataObject);
        }

        private void BuildConfirmDeletePopoverContent(ScriptableObject objectToDelete)
        {
            VisualElement dragHandle = _confirmDeletePopover.Q(className: "popover-drag-handle");
            VisualElement contentWrapper = _confirmDeletePopover.Q(
                name: $"{_confirmDeletePopover.name}-content-wrapper"
            );
            if (dragHandle == null || contentWrapper == null)
            {
                return;
            }

            _confirmDeletePopover.userData = objectToDelete;

            dragHandle.AddToClassList("confirm-delete");
            dragHandle.Clear();
            contentWrapper.Clear();

            dragHandle.Add(
                new Label("Confirm Delete")
                {
                    style = { unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 5 },
                }
            );
            Button closeButton = new(CloseActivePopover) { text = "X" };
            closeButton.AddToClassList("popover-close-button");
            closeButton.AddToClassList(StyleConstants.ClickableClass);
            dragHandle.Add(closeButton);

            contentWrapper.Add(
                new Label(
                    $"Delete '<color=yellow><i>{objectToDelete.name}</i></color>'?\nThis cannot be undone."
                )
                {
                    // TODO: CLEAN UP STYLE
                    style = { whiteSpace = WhiteSpace.Normal, marginBottom = 15 },
                }
            );
            VisualElement buttonContainer = new();
            buttonContainer.AddToClassList("popover-button-container");
            Button cancelButton = new(CloseActivePopover) { text = "Cancel" };
            cancelButton.AddToClassList(StyleConstants.PopoverCancelButtonClass);
            cancelButton.AddToClassList(StyleConstants.PopoverButtonClass);
            cancelButton.AddToClassList(StyleConstants.ClickableClass);
            Button deleteButton = new(HandleDeleteConfirmed) { text = "Delete" };
            deleteButton.AddToClassList(StyleConstants.PopoverButtonClass);
            deleteButton.AddToClassList("popover-delete-button");
            deleteButton.AddToClassList(StyleConstants.ClickableClass);
            buttonContainer.Add(cancelButton);
            buttonContainer.Add(deleteButton);
            contentWrapper.Add(buttonContainer);
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
                    if (Time.realtimeSinceStartup <= _lastSettingsClicked + 0.5f)
                    {
                        return;
                    }

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
            _addTypeButton.AddToClassList(StyleConstants.ClickableClass);
            nsHeader.Add(_addTypeButton);
            namespaceColumn.Add(nsHeader);

            ScrollView namespaceScrollView = new(ScrollViewMode.Vertical)
            {
                name = "namespace-scrollview",
            };
            namespaceScrollView.AddToClassList("namespace-scrollview");
            _namespaceListContainer ??= new VisualElement { name = "namespace-list" };
            namespaceScrollView.Add(_namespaceListContainer);
            namespaceColumn.Add(namespaceScrollView);
            return namespaceColumn;
        }

        private void BuildTypeAddList(string filter = null)
        {
            if (string.Equals(SearchPlaceholder, filter, StringComparison.Ordinal))
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
                HashSet<string> managedTypeFullNames = _namespaceController
                    .GetAllManagedTypeNames()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                IOrderedEnumerable<IGrouping<string, Type>> groupedTypes = allObjectTypes
                    .GroupBy(NamespaceController.GetNamespaceKey)
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

                        addableTypes.Add(type);
                    }

                    if (addableTypes.Count > 0)
                    {
                        foundMatches = true;

                        VisualElement namespaceGroupContainer = new()
                        {
                            name = $"ns-group-container-{group.Key}",
                        };
                        VisualElement header = new() { name = $"ns-header-{group.Key}" };
                        header.AddToClassList(PopoverNamespaceHeaderClassName);

                        bool startCollapsed = !isFiltering;
                        if (!startCollapsed)
                        {
                            header.AddToClassList(StyleConstants.ExpandedClass);
                        }

                        foreach (Type type in group.OrderBy(t => t.Name))
                        {
                            string typeName = type.Name;
                            bool typeMatchesSearch =
                                !isFiltering
                                || namespaceMatchesAll
                                || searchTerms.All(term =>
                                    typeName.Contains(term, StringComparison.OrdinalIgnoreCase)
                                    || namespaceKey.Contains(
                                        term,
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                );

                            if (!typeMatchesSearch)
                            {
                                continue;
                            }

                            bool isManaged = managedTypeFullNames.Contains(type.FullName);
                            Label typeLabel = CreateHighlightedLabel(
                                $"{type.Name}",
                                searchTerms,
                                PopoverListItemClassName,
                                bindToContextHovers: false,
                                header
                            );
                            typeLabel.AddToClassList(PopoverListItemClassName);
                            typeLabel.AddToClassList(StyleConstants.ClickableClass);

                            if (isManaged)
                            {
                                typeLabel.SetEnabled(false);
                                typeLabel.AddToClassList(PopoverListItemDisabledClassName);
                            }
                            else
                            {
                                typeLabel.RegisterCallback<PointerDownEvent, Type>(
                                    (evt, typeContext) =>
                                        HandleTypeSelectionFromPopover(
                                            evt,
                                            typeContext,
                                            namespaceKey
                                        ),
                                    type
                                );
                            }

                            typesToShowInGroup.Add(typeLabel);
                        }

                        Label indicator = new(
                            startCollapsed
                                ? StyleConstants.ArrowCollapsed
                                : StyleConstants.ArrowExpanded
                        )
                        {
                            name = $"ns-indicator-{group.Key}",
                        };
                        indicator.AddToClassList(PopoverNamespaceIndicatorClassName);
                        indicator.AddToClassList(StyleConstants.ClickableClass);

                        Label namespaceLabel = CreateHighlightedLabel(
                            group.Key,
                            searchTerms,
                            PopoverListNamespaceClassName
                        );
                        namespaceLabel.AddToClassList(PopoverListNamespaceClassName);

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
                            namespaceLabel.AddToClassList(StyleConstants.ClickableClass);

                            // ReSharper disable once HeapView.CanAvoidClosure
                            namespaceLabel.RegisterCallback<PointerDownEvent>(evt =>
                            {
                                if (evt.button != 0)
                                {
                                    return;
                                }

                                string clickedNamespace = clickContext["NamespaceKey"] as string;
                                List<Type> typesToAdd = clickContext["AddableTypes"] as List<Type>;
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
                                    ? StyleConstants.ArrowExpanded
                                    : StyleConstants.ArrowCollapsed;
                                header.EnableInClassList(
                                    StyleConstants.ExpandedClass,
                                    !nowCollapsed
                                );
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
                    _typeAddPopover.style.maxHeight = StyleKeyword.None;
                }
                else
                {
                    _typeAddPopover.style.maxHeight = StyleKeyword.Null;
                }
            }
            finally
            {
                _lastTypeAddSearchTerm = filter;
            }
        }

        private void HandleTypePopoverKeyDown(KeyDownEvent evt)
        {
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
                HandleTypeSelectionFromPopover(
                    null,
                    selectedType,
                    NamespaceController.GetNamespaceKey(selectedType)
                );
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

        private void HandleTypeSelectionFromPopover(
            PointerDownEvent evt,
            Type selectedType,
            string namespaceKey
        )
        {
            if (selectedType != null)
            {
                List<string> currentManagedList = _namespaceController.GetManagedTypeNames(
                    namespaceKey
                );
                if (!currentManagedList.Contains(selectedType.FullName))
                {
                    if (!_scriptableObjectTypes.TryGetValue(namespaceKey, out List<Type> types))
                    {
                        types = new List<Type>();
                        _scriptableObjectTypes[namespaceKey] = types;
                        _namespaceOrder[NamespaceController.GetNamespaceKey(selectedType)] =
                            _namespaceOrder.Count;
                    }

                    types.Add(selectedType);
                    SyncNamespaceAndTypeOrders();
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
            Button createButton = null;
            createButton = new Button(() =>
            {
                BuildCreatePopoverContent(_namespaceController.SelectedType);
                OpenPopover(_createPopover, createButton);
            })
            {
                text = "+",
                tooltip = "Create New Object",
                name = "create-object-button",
            };
            createButton.AddToClassList("create-button");
            createButton.AddToClassList("icon-button");
            createButton.AddToClassList(StyleConstants.ClickableClass);
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

            VisualElement dragHandle = _confirmNamespaceAddPopover.Q(
                className: "popover-drag-handle"
            );
            VisualElement contentWrapper = _confirmNamespaceAddPopover.Q(
                name: $"{_confirmNamespaceAddPopover.name}-content-wrapper"
            );
            if (dragHandle == null || contentWrapper == null)
            {
                return;
            }

            dragHandle.AddToClassList("namespace-add");
            dragHandle.Clear();
            contentWrapper.Clear();

            dragHandle.Add(
                new Label("Confirm Namespace Add")
                {
                    style = { unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 5 },
                }
            );

            Button closeButton = new(CloseNestedPopover) { text = "X" };
            closeButton.AddToClassList("popover-close-button");
            closeButton.AddToClassList(StyleConstants.ClickableClass);
            dragHandle.Add(closeButton);

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
                $"Add {countToAdd} type{(countToAdd > 1 ? "s" : "")} from namespace '<color=yellow><i>{namespaceKey}</i></color>' to Data Visualizer?";
            Label messageLabel = new(message)
            {
                style = { whiteSpace = WhiteSpace.Normal, marginBottom = 15 },
            };
            contentWrapper.Add(messageLabel);

            VisualElement buttonContainer = new();
            buttonContainer.AddToClassList("popover-button-container");
            contentWrapper.Add(buttonContainer);

            Button cancelButton = new(CloseNestedPopover)
            {
                text = "Cancel",
                style = { marginRight = 5 },
            };
            cancelButton.AddToClassList(StyleConstants.ClickableClass);
            cancelButton.AddToClassList(StyleConstants.PopoverButtonClass);
            cancelButton.AddToClassList(StyleConstants.PopoverCancelButtonClass);
            buttonContainer.Add(cancelButton);

            Button confirmButton = new(() =>
            {
                bool stateChanged = false;
                HashSet<string> currentManagedList = _namespaceController
                    .GetManagedTypeNames(namespaceKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (Type typeToAdd in typesToAdd)
                {
                    if (!currentManagedList.Add(typeToAdd.FullName))
                    {
                        continue;
                    }

                    if (!_scriptableObjectTypes.TryGetValue(namespaceKey, out List<Type> types))
                    {
                        types = new List<Type>();
                        _scriptableObjectTypes[namespaceKey] = types;
                        _namespaceOrder[namespaceKey] = _namespaceOrder.Count;
                    }

                    types.Add(typeToAdd);
                    stateChanged = true;
                }

                if (stateChanged)
                {
                    SyncNamespaceAndTypeOrders();
                }

                CloseActivePopover();
                if (stateChanged)
                {
                    ScheduleRefresh();
                }
            })
            {
                text = "Add",
            };
            confirmButton.AddToClassList(StyleConstants.ClickableClass);
            confirmButton.AddToClassList(StyleConstants.PopoverButtonClass);
            confirmButton.AddToClassList("popover-confirm-button");
            buttonContainer.Add(confirmButton);
        }

        private void BuildNamespaceView()
        {
            _namespaceController.Build(this, ref _namespaceListContainer);
        }

        internal void BuildObjectsView()
        {
            if (_objectListContainer == null)
            {
                return;
            }

            _objectListContainer.Clear();
            _objectVisualElementMap.Clear();
            _objectScrollView.scrollOffset = Vector2.zero;

            Type selectedType = _namespaceController.SelectedType;
            if (selectedType != null && _selectedObjects.Count == 0)
            {
                Label emptyLabel = new(
                    $"No objects of type '{selectedType.Name}' found.\nUse the '+' button above to create one."
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
                objectItemRow.AddToClassList(StyleConstants.ClickableClass);
                objectItemRow.style.flexDirection = FlexDirection.Row;
                objectItemRow.style.alignItems = Align.Center;
                objectItemRow.userData = dataObject;
                objectItemRow.RegisterCallback<PointerDownEvent>(OnObjectPointerDown);

                VisualElement contentArea = new() { name = "content" };
                contentArea.AddToClassList(ObjectItemContentClass);
                contentArea.AddToClassList(StyleConstants.ClickableClass);
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
                titleLabel.AddToClassList(StyleConstants.ClickableClass);
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
                cloneButton.AddToClassList(StyleConstants.ActionButtonClass);
                cloneButton.AddToClassList("clone-button");
                actionsArea.Add(cloneButton);

                Button renameButton = null;
                renameButton = new Button(() => OpenRenamePopover(renameButton, dataObject))
                {
                    text = "✎",
                    tooltip = "Rename Object",
                };
                renameButton.AddToClassList(StyleConstants.ActionButtonClass);
                renameButton.AddToClassList("rename-button");
                actionsArea.Add(renameButton);

                Button deleteButton = null;
                deleteButton = new Button(() => OpenConfirmDeletePopover(deleteButton, dataObject))
                {
                    text = "X",
                    tooltip = "Delete Object",
                };
                deleteButton.AddToClassList(StyleConstants.ActionButtonClass);
                deleteButton.AddToClassList("delete-button");
                actionsArea.Add(deleteButton);

                _objectVisualElementMap[dataObject] = objectItemRow;
                _objectListContainer.Add(objectItemRow);

                if (_selectedObject == dataObject)
                {
                    objectItemRow.AddToClassList(StyleConstants.SelectedClass);
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

            if (_selectedElement != null)
            {
                try
                {
                    TextField assetNameField = new("Asset Name:")
                    {
                        value = _selectedObject.name,
                        isReadOnly = true,
                        name = "inspector-asset-name-field",
                    };

                    assetNameField
                        .Q<TextInputBaseField<string>>(TextField.textInputUssName)
                        ?.SetEnabled(false);

                    assetNameField.AddToClassList("readonly-display-field");
                    _inspectorContainer.Add(assetNameField);

                    VisualElement separator = new()
                    {
                        style =
                        {
                            height = 1,
                            backgroundColor = new Color(0.3f, 0.3f, 0.3f),
                            marginTop = 3,
                            marginBottom = 8,
                            flexShrink = 0,
                        },
                    };
                    _inspectorContainer.Add(separator);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error creating asset name display field: {ex}");
                }
            }

            // ReSharper disable once RedundantAssignment
            bool useOdinInspector = false;
            Type objectType = _selectedObject.GetType();
#if ODIN_INSPECTOR
            if (
                objectType.IsAttributeDefined(
                    out CustomDataVisualizationAttribute customVisualization
                )
            )
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
                            () => _odinPropertyTree?.Draw()
                        )
                        {
                            name = "odin-inspector",
                            style = { flexGrow = 1 },
                        };
                    }
                    else
                    {
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
            MethodInfo[] availableMethods = objectType
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

                    // TODO: EXPAND TO TAKE IN MY CUSTOM TYPE
                    return parameters[0].ParameterType == typeof(SerializedObject);
                })
                .OrderBy(method => method.Name)
                .ToArray();
            MethodInfo visualMethod = availableMethods.FirstOrDefault(
                ReflectionHelpers.IsAttributeDefined<CustomVisualProviderAttribute>
            );
            if (visualMethod == null)
            {
                visualMethod = availableMethods.FirstOrDefault();
            }

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
                            object parameterInstance = Activator.CreateInstance(
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

        internal ScriptableObject DetermineObjectToAutoSelect()
        {
            Type selectedType = _namespaceController.SelectedType;
            if (selectedType == null || _selectedObjects == null || _selectedObjects.Count == 0)
            {
                return null;
            }

            ScriptableObject objectToSelect = null;
            string savedObjectGuid = GetLastSelectedObjectGuidForType(selectedType.Name);
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
            if (indicator == null || typesContainer == null)
            {
                return;
            }

            indicator.text = collapsed
                ? StyleConstants.ArrowCollapsed
                : StyleConstants.ArrowExpanded;
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

        internal void LoadObjectTypes(Type type)
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
            HashSet<string> managedTypeFullNames;
            DataVisualizerSettings settings = Settings;
            if (settings.persistStateInSettingsAsset)
            {
                managedTypeFullNames =
                    settings
                        .typeOrders?.SelectMany(order => order.typeNames)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)
                    ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                managedTypeFullNames =
                    UserState
                        .typeOrders?.SelectMany(order => order.typeNames)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)
                    ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            List<Type> allObjectTypes = LoadRelevantScriptableObjectTypes();

            List<Type> typesToDisplay = allObjectTypes
                .Where(t =>
                    managedTypeFullNames.Contains(t.FullName)
                    || typeof(BaseDataObject).IsAssignableFrom(t)
                )
                .ToList();

            IEnumerable<(string key, List<Type> types)> groups = typesToDisplay
                .GroupBy(NamespaceController.GetNamespaceKey)
                .Select(g => (key: g.Key, types: g.ToList()));

            List<(string key, List<Type> types)> orderedTypes = groups.ToList();

            List<string> customNamespaceOrder = GetNamespaceOrder();
            orderedTypes.Sort(
                (lhs, rhs) => CompareUsingCustomOrder(lhs.key, rhs.key, customNamespaceOrder)
            );

            foreach ((string key, List<Type> types) in orderedTypes)
            {
                List<string> customTypeNameOrder = GetTypeOrderForNamespace(key);
                types.Sort(
                    (lhs, rhs) => CompareUsingCustomOrder(lhs.Name, rhs.Name, customTypeNameOrder)
                );
            }

            _scriptableObjectTypes.Clear();
            _namespaceOrder.Clear();
            for (int i = 0; i < orderedTypes.Count; ++i)
            {
                (string key, List<Type> types) = orderedTypes[i];
                _scriptableObjectTypes[key] = types;
                _namespaceOrder[key] = i;
            }
        }

        private List<Type> LoadRelevantScriptableObjectTypes()
        {
            return _relevantScriptableObjectTypes ??= TypeCache
                .GetTypesDerivedFrom<ScriptableObject>()
                .Where(t => !typeof(Editor).IsAssignableFrom(t))
                .Where(t => !t.IsAbstract && !t.IsGenericType)
                .ToList();
        }

        internal void SelectObject(ScriptableObject dataObject)
        {
            if (_selectedObject == dataObject)
            {
                return;
            }

            _selectedElement?.RemoveFromClassList(StyleConstants.SelectedClass);
            foreach (
                VisualElement child in _selectedElement?.IterateChildrenRecursively()
                    ?? Enumerable.Empty<VisualElement>()
            )
            {
                child.EnableInClassList(StyleConstants.ClickableClass, true);
            }
            _selectedObject = dataObject;

            _selectedElement = null;

            Type selectedType = _namespaceController.SelectedType;
            if (
                _selectedObject != null
                && _objectVisualElementMap.TryGetValue(
                    _selectedObject,
                    out VisualElement newSelectedElement
                )
            )
            {
                _selectedElement = newSelectedElement;
                _selectedElement.AddToClassList(StyleConstants.SelectedClass);
                foreach (VisualElement child in _selectedElement.IterateChildrenRecursively())
                {
                    child.EnableInClassList(StyleConstants.ClickableClass, false);
                }
                Selection.activeObject = _selectedObject;
                _objectScrollView
                    .schedule.Execute(() =>
                    {
                        _objectScrollView?.ScrollTo(_selectedElement);
                    })
                    .ExecuteLater(1);
                try
                {
                    if (selectedType != null)
                    {
                        string namespaceKey = NamespaceController.GetNamespaceKey(selectedType);
                        string typeName = selectedType.Name;
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
                if (selectedType != null)
                {
                    string typeName = selectedType.Name;
                    SetLastSelectedObjectGuidForType(typeName, null);
                }
            }

            _currentInspectorScriptableObject?.Dispose();
            _currentInspectorScriptableObject =
                dataObject != null ? new SerializedObject(dataObject) : null;

            if (dataObject != null)
            {
                _namespaceController.SelectType(this, dataObject.GetType());
            }
            // Backup trigger, we have some delay issues
            rootVisualElement
                .schedule.Execute(
                    () => _namespaceController.SelectType(this, _selectedObject?.GetType())
                )
                .ExecuteLater(1);

            BuildInspectorView();
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
                        Type type => NamespaceController.GetTypeDisplayName(type),
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

            int oldDataIndex = _namespaceOrder.GetValueOrDefault(draggedKey, -1);
            if (0 > oldDataIndex)
            {
                return;
            }

            if (oldDataIndex < targetIndex)
            {
                foreach (KeyValuePair<string, int> entry in _namespaceOrder.ToArray())
                {
                    if (oldDataIndex < entry.Value && entry.Value <= targetIndex)
                    {
                        _namespaceOrder[entry.Key] = entry.Value - 1;
                    }
                }
            }
            else if (targetIndex < oldDataIndex)
            {
                foreach (KeyValuePair<string, int> entry in _namespaceOrder.ToArray())
                {
                    if (targetIndex <= entry.Value && entry.Value < oldDataIndex)
                    {
                        _namespaceOrder[entry.Key] = entry.Value + 1;
                    }
                }
            }
            else
            {
                return;
            }

            _namespaceOrder[draggedKey] = Mathf.Clamp(targetIndex, 0, _namespaceOrder.Count - 1);
            UpdateAndSaveNamespaceOrder();
        }

        private void UpdateAndSaveNamespaceOrder()
        {
            List<string> newNamespaceOrder = _namespaceOrder
                .OrderBy(kvp => kvp.Value)
                .Select(kvp => kvp.Key)
                .ToList();
            SetNamespaceOrder(newNamespaceOrder);
        }

        internal void OnNamespacePointerDown(PointerDownEvent evt)
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

        internal void OnTypePointerDown(VisualElement namespaceHeader, PointerDownEvent evt)
        {
            // TODO IMPLEMENT NEW HANDLER
            if (evt.currentTarget is not VisualElement { userData: Type type } targetElement)
            {
                return;
            }

            if (evt.button == 0)
            {
                _lastActiveFocusArea = FocusArea.TypeList;
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

            int namespaceIndex = _namespaceOrder.GetValueOrDefault(namespaceKey, -1);
            if (0 <= namespaceIndex)
            {
                List<Type> typesList = _scriptableObjectTypes.GetValueOrDefault(namespaceKey, null);
                int? oldDataIndex = typesList?.IndexOf(draggedType);
                if (0 <= oldDataIndex)
                {
                    typesList.RemoveAt(oldDataIndex.Value);
                    int dataInsertIndex = targetIndex;
                    dataInsertIndex = Mathf.Clamp(dataInsertIndex, 0, typesList.Count);
                    typesList.Insert(dataInsertIndex, draggedType);
                    UpdateAndSaveTypeOrder(namespaceKey, typesList);
                }
            }
        }

        private void UpdateAndSaveTypeOrder(string namespaceKey, List<Type> orderedTypes)
        {
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
            Type selectedType = _namespaceController.SelectedType;
            if (selectedType != null)
            {
                UpdateAndSaveObjectOrderList(selectedType, _selectedObjects);
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
                        Label ghostLabel = new(dragText);
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

        [Obsolete("Should not be used internally except by UserState")]
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
            try
            {
                string json = JsonUtility.ToJson(UserState, true);
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
            DataVisualizerSettings settings = Settings;
            if (settings.persistStateInSettingsAsset)
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
            try
            {
                DataVisualizerUserState userState = UserState;
                DataVisualizerSettings settings = Settings;
                if (migrateToSettingsAsset)
                {
                    settings.HydrateFrom(userState);
                    settings.MarkDirty();
                    AssetDatabase.SaveAssets();
                    Debug.Log("Migration to Settings Object complete.");
                }
                else
                {
                    userState.HydrateFrom(settings);
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
            DataVisualizerSettings settings = Settings;

            return settings.persistStateInSettingsAsset
                ? settings.lastSelectedNamespaceKey
                : UserState.lastSelectedNamespaceKey;
        }

        private List<string> GetObjectOrderForType(string typeFullName)
        {
            if (string.IsNullOrWhiteSpace(typeFullName))
            {
                return new List<string>();
            }

            DataVisualizerSettings settings = Settings;

            if (settings.persistStateInSettingsAsset)
            {
                TypeObjectOrder entry = settings.objectOrders?.Find(o =>
                    string.Equals(o.TypeFullName, typeFullName, StringComparison.Ordinal)
                );
                return entry?.ObjectGuids?.ToList() ?? new List<string>();
            }
            else
            {
                TypeObjectOrder entry = UserState.objectOrders?.Find(o =>
                    string.Equals(o.TypeFullName, typeFullName, StringComparison.Ordinal)
                );
                return entry?.ObjectGuids?.ToList() ?? new List<string>();
            }
        }

        private void SetObjectOrderForType(string typeFullName, List<string> objectGuids)
        {
            if (string.IsNullOrWhiteSpace(typeFullName) || objectGuids == null)
            {
                return;
            }

            PersistSettings(
                settings =>
                {
                    List<string> entryList = settings.GetOrCreateObjectOrderList(typeFullName);
                    if (entryList.SequenceEqual(objectGuids))
                    {
                        return false;
                    }

                    entryList.Clear();
                    entryList.AddRange(objectGuids);
                    return true;
                },
                userState =>
                {
                    List<string> entryList = userState.GetOrCreateObjectOrderList(typeFullName);
                    if (entryList.SequenceEqual(objectGuids))
                    {
                        return false;
                    }

                    entryList.Clear();
                    entryList.AddRange(objectGuids);
                    return true;
                }
            );
        }

        private void SetLastSelectedNamespaceKey(string value)
        {
            PersistSettings(
                settings =>
                {
                    if (
                        string.Equals(
                            settings.lastSelectedNamespaceKey,
                            value,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return false;
                    }

                    settings.lastSelectedNamespaceKey = value;
                    return true;
                },
                userState =>
                {
                    if (
                        string.Equals(
                            userState.lastSelectedNamespaceKey,
                            value,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return false;
                    }

                    userState.lastSelectedNamespaceKey = value;
                    return true;
                }
            );
        }

        private string GetLastSelectedTypeName()
        {
            DataVisualizerSettings settings = Settings;

            return settings.persistStateInSettingsAsset
                ? settings.lastSelectedTypeName
                : UserState.lastSelectedTypeName;
        }

        private void SetLastSelectedTypeName(string value)
        {
            PersistSettings(
                settings =>
                {
                    if (
                        string.Equals(
                            settings.lastSelectedTypeName,
                            value,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return false;
                    }

                    settings.lastSelectedTypeName = value;
                    return true;
                },
                userState =>
                {
                    if (
                        string.Equals(
                            userState.lastSelectedTypeName,
                            value,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return false;
                    }

                    userState.lastSelectedTypeName = value;
                    return true;
                }
            );
        }

        private string GetLastSelectedObjectGuidForType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            DataVisualizerSettings settings = Settings;
            return settings.persistStateInSettingsAsset
                ? settings.GetLastObjectForType(typeName)
                : UserState.GetLastObjectForType(typeName);
        }

        internal void SetLastSelectedObjectGuidForType(string typeName, string objectGuid)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return;
            }

            PersistSettings(
                settings =>
                {
                    settings.SetLastObjectForType(typeName, objectGuid);
                    return true;
                },
                userState =>
                {
                    userState.SetLastObjectForType(typeName, objectGuid);
                    return true;
                }
            );
        }

        private List<string> GetNamespaceOrder()
        {
            DataVisualizerSettings settings = Settings;
            if (settings.persistStateInSettingsAsset)
            {
                return settings.namespaceOrder?.ToList() ?? new List<string>();
            }

            return UserState.namespaceOrder?.ToList() ?? new List<string>();
        }

        private void SetNamespaceOrder(List<string> value)
        {
            if (value == null)
            {
                return;
            }

            PersistSettings(
                settings =>
                {
                    if (
                        settings.namespaceOrder != null
                        && settings.namespaceOrder.SequenceEqual(value)
                    )
                    {
                        return false;
                    }

                    settings.namespaceOrder = new List<string>(value);
                    return true;
                },
                userState =>
                {
                    if (
                        userState.namespaceOrder != null
                        && userState.namespaceOrder.SequenceEqual(value)
                    )
                    {
                        return false;
                    }

                    userState.namespaceOrder = new List<string>(value);
                    return true;
                }
            );
        }

        private List<string> GetTypeOrderForNamespace(string namespaceKey)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return new List<string>();
            }

            DataVisualizerSettings settings = Settings;
            if (settings.persistStateInSettingsAsset)
            {
                NamespaceTypeOrder entry = settings.typeOrders?.Find(o =>
                    string.Equals(o.namespaceKey, namespaceKey, StringComparison.Ordinal)
                );
                return entry?.typeNames?.ToList() ?? new List<string>();
            }
            else
            {
                NamespaceTypeOrder entry = UserState.typeOrders?.Find(o =>
                    string.Equals(o.namespaceKey, namespaceKey, StringComparison.Ordinal)
                );
                return entry?.typeNames?.ToList() ?? new List<string>();
            }
        }

        private void SetTypeOrderForNamespace(string namespaceKey, List<string> typeNames)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey) || typeNames == null)
            {
                return;
            }

            PersistSettings(
                settings =>
                {
                    List<string> entryList = settings.GetOrCreateTypeOrderList(namespaceKey);
                    if (entryList.SequenceEqual(typeNames))
                    {
                        return false;
                    }

                    entryList.Clear();
                    entryList.AddRange(typeNames);
                    return true;
                },
                userState =>
                {
                    List<string> entryList = userState.GetOrCreateTypeOrderList(namespaceKey);
                    if (entryList.SequenceEqual(typeNames))
                    {
                        return false;
                    }

                    entryList.Clear();
                    entryList.AddRange(typeNames);
                    return true;
                }
            );
        }

        private void SetIsNamespaceCollapsed(string namespaceKey, bool isCollapsed)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return;
            }

            PersistSettings(
                settings =>
                {
                    NamespaceCollapseState entry = settings.GetOrCreateCollapseState(namespaceKey);
                    if (entry.isCollapsed == isCollapsed)
                    {
                        return false;
                    }

                    entry.isCollapsed = isCollapsed;
                    return true;
                },
                userState =>
                {
                    NamespaceCollapseState entry = userState.GetOrCreateCollapseState(namespaceKey);
                    if (entry.isCollapsed == isCollapsed)
                    {
                        return false;
                    }

                    entry.isCollapsed = isCollapsed;
                    return true;
                }
            );
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
    }
#endif
}
