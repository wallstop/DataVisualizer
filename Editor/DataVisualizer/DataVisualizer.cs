// ReSharper disable AccessToModifiedClosure
// ReSharper disable AccessToDisposedClosure
// ReSharper disable HeapView.CanAvoidClosure
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
    using Debug = UnityEngine.Debug;
    using Object = UnityEngine.Object;

    public sealed class DataVisualizer : EditorWindow
    {
        private const string PackageId = "com.wallstop-studios.data-visualizer";
        private const string PrefsPrefix = "WallstopStudios.Editor.DataVisualizer.";

        private const string PrefsSplitterOuterKey = PrefsPrefix + "SplitterOuterFixedPaneWidth";
        private const string PrefsSplitterInnerKey = PrefsPrefix + "SplitterInnerFixedPaneWidth";
        private const string PrefsInitialSizeAppliedKey = PrefsPrefix + "InitialSizeApplied";

        private const string SettingsDefaultPath = "Assets/Editor/DataVisualizerSettings.asset";
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
        private const float DefaultOuterSplitWidth = 200f;
        private const float DefaultInnerSplitWidth = 250f;

        private enum DragType
        {
            [Obsolete("Please use a valid value")]
            None = 0,
            Object = 1,
            Namespace = 2,
            Type = 3,
        }

        private enum FocusArea
        {
            [Obsolete("Please use a valid value")]
            None = 0,
            TypeList = 1,
            AddTypePopover = 2,
            SearchResultsPopover = 3,
        }

        private enum LabelFilterSection
        {
            [Obsolete("Please use a valid value")]
            None = 0,
            Available = 1,
            AND = 2,
            OR = 3,
        }

        internal static DataVisualizer Instance;

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

        private int HiddenNamespaces
        {
#pragma warning disable CS0618 // Type or member is obsolete
            get => _hiddenNamespaces;
            set
            {
                _hiddenNamespaces = value;
                if (_namespaceColumnLabel != null)
                {
                    _namespaceColumnLabel.text =
                        value <= 0 ? "Namespaces"
                        : value <= 15 ? $"Namespaces (<b><color=yellow>{value}</color></b> hidden)"
                        : $"Namespaces (<b><color=red>{value}</color></b> hidden)";
                }
            }
#pragma warning restore CS0618 // Type or member is obsolete
        }

        internal readonly Dictionary<string, List<Type>> _scriptableObjectTypes = new(
            StringComparer.Ordinal
        );

        private readonly Dictionary<string, int> _namespaceOrder = new(StringComparer.Ordinal);

        private readonly Dictionary<ScriptableObject, VisualElement> _objectVisualElementMap =
            new();

        [Obsolete("Use HiddenNamespaces property instead")]
        private int _hiddenNamespaces;

        private readonly List<ScriptableObject> _selectedObjects = new();
        private readonly List<ScriptableObject> _allManagedObjectsCache = new();
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
        private Label _namespaceColumnLabel;
        private TextField _assetNameTextField;
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

        private HorizontalToggle _andOrToggle;
        private VisualElement _labelFilterSelectionRoot;
        private VisualElement _availableLabelsContainer;
        private VisualElement _andLabelsContainer;
        private VisualElement _orLabelsContainer;
        private Label _filterStatusLabel;

        private readonly List<string> _currentUniqueLabelsForType = new();
        private TypeLabelFilterConfig _currentTypeLabelFilterConfig;
        private readonly List<ScriptableObject> _filteredObjects = new();
        private readonly Dictionary<string, Color> _labelColorCache = new();
        private readonly List<Color> _predefinedLabelColors = new()
        {
            new Color(0.32f, 0.55f, 0.78f),
            new Color(0.90f, 0.42f, 0.32f),
            new Color(0.45f, 0.70f, 0.40f),
            new Color(0.82f, 0.60f, 0.28f),
            new Color(0.50f, 0.48f, 0.70f),
            new Color(0.75f, 0.45f, 0.60f),
            new Color(0.30f, 0.65f, 0.65f),
            new Color(0.65f, 0.65f, 0.35f),
        };
        private int _nextColorIndex;

        private VisualElement _inspectorLabelsSection;
        private VisualElement _inspectorCurrentLabelsContainer;
        private TextField _inspectorNewLabelInput;

        private string _draggedLabelText;
        private LabelFilterSection _dragSourceSection;

        private VisualElement _inspectorLabelSuggestionsPopover;
        private readonly List<string> _projectUniqueLabelsCache = new();
        private bool _isLabelCachePopulated;
        private readonly List<VisualElement> _currentLabelSuggestionItems = new();
        private int _labelSuggestionHighlightIndex = -1;

        private VisualElement _processorAreaElement;
        private VisualElement _processorListContainer;
        private Button _processorToggleCollapseButton;
        private Label _processorHeaderLabel;
        private readonly List<IDataProcessor> _allDataProcessors = new();
        private readonly List<IDataProcessor> _compatibleDataProcessors = new();
        private bool _isProcessorColumnContentCollapsed = true;

        private const string PrefsProcessorColumnCollapsedKey =
            PrefsPrefix + "ProcessorColumnCollapsed";

        private const string LabelSuggestionItemClass = "label-suggestion-item";

        private TextField _searchField;
        private VisualElement _searchPopover;
        private bool _isSearchCachePopulated;
        private string _lastSearchString;

        private Button _addTypeButton;
        private Label _emptyObjectLabel;
        private Button _addTypesFromScriptFolderButton;
        private Button _addTypesFromDataFolderButton;
        private Button _createObjectButton;
        private Button _settingsButton;
        private TextField _typeAddSearchField;
        private VisualElement _typePopoverListContainer;

        private TextField _typeSearchField;

        private float _lastSavedOuterWidth = -1f;
        private float _lastSavedInnerWidth = -1f;
        private IVisualElementScheduledItem _saveWidthsTask;

        private int _searchHighlightIndex = -1;
        private int _typePopoverHighlightIndex = -1;
        private string _lastTypeAddSearchTerm;
#pragma warning disable CS0618 // Type or member is obsolete
        private FocusArea _lastActiveFocusArea = FocusArea.None;
        private DragType _activeDragType = DragType.None;
#pragma warning restore CS0618 // Type or member is obsolete
        private object _draggedData;
        private VisualElement _inPlaceGhost;
        private int _lastGhostInsertIndex = -1;
        private VisualElement _lastGhostParent;
        private VisualElement _draggedElement;
        private VisualElement _dragGhost;
        internal bool _isDragging;
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
        private bool _needsRefresh;

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
            _nextColorIndex = 0;
            Instance = this;
            _isSearchCachePopulated = false;
            _objectVisualElementMap.Clear();
            _selectedObject = null;
            _selectedElement = null;
            _selectedObjects.Clear();
#if ODIN_INSPECTOR
            _odinPropertyTree = null;
#endif
            _userStateFilePath = Path.Combine(Application.persistentDataPath, UserStateFileName);

            _allDataProcessors.Clear();
            IEnumerable<Type> processorTypes = TypeCache
                .GetTypesDerivedFrom<IDataProcessor>()
                .Where(t => !t.IsAbstract && !t.IsInterface && !t.IsGenericTypeDefinition);
            foreach (Type type in processorTypes)
            {
                try
                {
                    if (Activator.CreateInstance(type) is IDataProcessor instance)
                    {
                        _allDataProcessors.Add(instance);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"Failed to create instance of IDataProcessor '{type.FullName}': {ex.Message}"
                    );
                }
            }
            Debug.Log($"Discovered {_allDataProcessors.Count} Data Processors.");
            // --- End Discovery ---

            // Load processor column collapse state
            _isProcessorColumnContentCollapsed = EditorPrefs.GetBool(
                PrefsProcessorColumnCollapsedKey,
                true
            ); // Default to collapsed

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
            if (Instance == this)
            {
                Instance = null;
            }
            _selectedElement = null;
            _selectedObject = null;
            _scriptableObjectTypes.Clear();
            _namespaceOrder.Clear();
            _namespaceController.Clear();
            _allManagedObjectsCache.Clear();
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

        internal void PopulateSearchCache()
        {
            _allManagedObjectsCache.Clear();
            List<Type> managedTypes = _scriptableObjectTypes
                .SelectMany(tuple => tuple.Value)
                .ToList();
            HashSet<string> uniqueGuids = new(StringComparer.OrdinalIgnoreCase);
            foreach (Type type in managedTypes)
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
                            _allManagedObjectsCache.Add(obj);
                        }
                    }
                }
            }

            _allManagedObjectsCache.Sort(
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
            DataVisualizer window = Instance;
            if (window != null)
            {
                window.ScheduleRefresh();
            }
        }

        private void ScheduleRefresh()
        {
            if (_needsRefresh)
            {
                return;
            }

            _needsRefresh = true;
            rootVisualElement.schedule.Execute(RefreshAllViews).ExecuteLater(1);
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
                        selectedType = typesInNamespace.Find(t =>
                            string.Equals(t.Name, previousTypeName, StringComparison.Ordinal)
                        );
                    }

                    selectedType ??= typesInNamespace[0];
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

            if (selectedObject == null)
            {
                selectedObject = _selectedObjects.FirstOrDefault();
            }

            PopulateSearchCache();
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
            _namespaceController.SelectType(this, selectedType);
            _needsRefresh = false;
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

        private static DataVisualizerSettings LoadOrCreateSettings()
        {
            DataVisualizerSettings settings = null;

            DataVisualizerSettings[] foundSettings = AssetDatabase
                .FindAssets($"t:{nameof(DataVisualizerSettings)}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<DataVisualizerSettings>)
                .Where(s => s != null)
                .ToArray();

            if (0 < foundSettings.Length)
            {
                if (1 < foundSettings.Length)
                {
                    Debug.LogWarning(
                        $"Multiple DataVisualizerSettings assets found ({foundSettings.Length}). Using the first one."
                    );
                }

                settings = foundSettings[0];
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

            DirectoryHelper.EnsureDirectoryExists(settings.DataFolderPath.SanitizePath());
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
                savedObjectGuid = GetLastSelectedObjectGuidForType(selectedType.FullName);
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
            if (objectToSelect == null)
            {
                _namespaceController.SelectType(this, selectedType);
            }
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

            _searchField = new TextField { name = "global-search-field" };
            _searchField.AddToClassList("global-search-field");
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
                        context.OpenPopover(
                            context._searchPopover,
                            context._searchField,
                            shouldFocus: false
                        );
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
            _processorAreaElement = CreateProcessorColumn();
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

            _inspectorLabelSuggestionsPopover = CreatePopoverBase(
                "inspector-label-suggestions-popover"
            );
            root.Add(_inspectorLabelSuggestionsPopover);
            _inspectorLabelSuggestionsPopover.style.width = StyleKeyword.Auto;

            _typeAddSearchField = new TextField { name = "type-add-search-field" };
            _typeAddSearchField.AddToClassList("type-add-search-field");
            _typeAddSearchField.SetPlaceholderText(SearchPlaceholder);
            _typeAddSearchField.RegisterValueChangedCallback(evt => BuildTypeAddList(evt.newValue));
            _typeAddSearchField.RegisterCallback<KeyDownEvent>(HandleTypePopoverKeyDown);
            _typeAddPopover.Add(_typeAddSearchField);

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
            BuildProcessorColumnView();
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
                unityRelativeStyleSheetPath = unityRelativeStyleSheetPath.SanitizePath();

                const string packageCache = "PackageCache/";
                int packageCacheIndex;
                if (!string.IsNullOrWhiteSpace(unityRelativeStyleSheetPath))
                {
                    styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                        unityRelativeStyleSheetPath
                    );
                }

                if (styleSheet == null && !string.IsNullOrWhiteSpace(unityRelativeStyleSheetPath))
                {
                    packageCacheIndex = unityRelativeStyleSheetPath.IndexOf(
                        packageCache,
                        StringComparison.OrdinalIgnoreCase
                    );
                    if (0 <= packageCacheIndex)
                    {
                        unityRelativeStyleSheetPath = unityRelativeStyleSheetPath[
                            (packageCacheIndex + packageCache.Length)..
                        ];
                        int forwardIndex = unityRelativeStyleSheetPath.IndexOf(
                            "/",
                            StringComparison.Ordinal
                        );
                        if (0 <= forwardIndex)
                        {
                            unityRelativeStyleSheetPath = unityRelativeStyleSheetPath.Substring(
                                forwardIndex
                            );
                            unityRelativeStyleSheetPath =
                                "Packages/" + PackageId + "/" + unityRelativeStyleSheetPath;
                        }
                        else
                        {
                            unityRelativeStyleSheetPath = "Packages/" + unityRelativeStyleSheetPath;
                        }
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
                }

                string fontPath =
                    $"{packageRoot}{pathSeparator}Editor{pathSeparator}Fonts{pathSeparator}IBMPlexMono-Regular.ttf";
                string unityRelativeFontPath = DirectoryHelper.AbsoluteToUnityRelativePath(
                    fontPath
                );

                font = AssetDatabase.LoadAssetAtPath<Font>(unityRelativeFontPath);
                if (font == null)
                {
                    packageCacheIndex = unityRelativeFontPath.IndexOf(
                        packageCache,
                        StringComparison.OrdinalIgnoreCase
                    );
                    if (0 <= packageCacheIndex)
                    {
                        unityRelativeFontPath = unityRelativeFontPath[
                            (packageCacheIndex + packageCache.Length)..
                        ];
                        int forwardIndex = unityRelativeFontPath.IndexOf(
                            "/",
                            StringComparison.Ordinal
                        );
                        if (0 <= forwardIndex)
                        {
                            unityRelativeFontPath = unityRelativeFontPath.Substring(forwardIndex);
                            unityRelativeFontPath =
                                "Packages/" + PackageId + "/" + unityRelativeFontPath;
                        }
                        else
                        {
                            unityRelativeFontPath = "Packages/" + unityRelativeFontPath;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(unityRelativeFontPath))
                    {
                        font = AssetDatabase.LoadAssetAtPath<Font>(unityRelativeFontPath);
                    }
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

        // Add these methods to DataVisualizer.cs

        private VisualElement CreateProcessorColumn()
        {
            _processorAreaElement = new VisualElement
            {
                name = "processor-column",
                style =
                {
                    width = 180, // Initial fixed width, can be made resizable later
                    minWidth = 30, // Minimum for collapsed state
                    flexDirection = FlexDirection.Column, // Stack header and list vertically
                    borderRightWidth = 1,
                    borderRightColor = Color.gray,
                    paddingLeft = 0,
                    paddingBottom = 0,
                    paddingRight = 0,
                    paddingTop = 0,
                },
            };

            // Header with Toggle Button
            VisualElement header = new()
            {
                name = "processor-column-header",
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    justifyContent = Justify.SpaceBetween,
                    paddingLeft = 3,
                    paddingBottom = 3,
                    paddingRight = 3,
                    paddingTop = 3,
                    height = 24, // Consistent header height
                    flexShrink = 0,
                    borderBottomWidth = 1,
                    borderBottomColor = Color.gray,
                },
            };
            _processorHeaderLabel = new Label("Processors")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold },
            };
            _processorToggleCollapseButton = new Button(ToggleProcessorContentCollapse)
            {
                text = "V", /* Default */
            };
            _processorToggleCollapseButton.AddToClassList("icon-button"); // Small button style
            _processorToggleCollapseButton.style.width = 20;
            _processorToggleCollapseButton.style.height = 20;

            header.Add(_processorHeaderLabel);
            header.Add(_processorToggleCollapseButton);
            _processorAreaElement.Add(header);

            // ScrollView for processor list
            ScrollView scrollView = new(ScrollViewMode.Vertical)
            {
                name = "processor-list-scrollview",
                style = { flexGrow = 1 }, // Takes remaining space
            };
            _processorListContainer = new VisualElement { name = "processor-list-container" };
            scrollView.Add(_processorListContainer);
            _processorAreaElement.Add(scrollView);

            return _processorAreaElement;
        }

        // Call this when _selectedType changes, or when collapse state toggles
        internal void BuildProcessorColumnView()
        {
            if (_processorListContainer == null || _processorAreaElement == null)
            {
                return;
            }

            _processorListContainer.Clear();
            _compatibleDataProcessors.Clear();

            const string ArrowCollapsed = "►";
            const string ArrowExpanded = "▼";

            if (_namespaceController.SelectedType != null)
            {
                _compatibleDataProcessors.AddRange(
                    _allDataProcessors.Where(p =>
                        p.Accepts != null && p.Accepts.Contains(_namespaceController.SelectedType)
                    )
                );
            }

            if (_compatibleDataProcessors.Count == 0)
            {
                _processorAreaElement.style.display = DisplayStyle.None; // Hide whole column
                // OR: Show column but with a "No processors" message
                // _processorHeaderLabel.text = "Processors";
                // _processorToggleCollapseButton.SetEnabled(false);
                // _processorToggleCollapseButton.text = "-";
                // _processorListContainer.Add(new Label("No processors for this type.") {style = {color = Color.gray, unityTextAlign = TextAnchor.MiddleCenter, marginTop = 10}});
                // _processorListContainer.parent.style.display = DisplayStyle.Flex; // Ensure scrollview is visible
                return;
            }

            // If we reach here, there are compatible processors
            _processorAreaElement.style.display = DisplayStyle.Flex; // Ensure column is visible
            _processorToggleCollapseButton.SetEnabled(true);

            // Update toggle button and header text
            if (_isProcessorColumnContentCollapsed)
            {
                _processorToggleCollapseButton.text = ArrowCollapsed;
                _processorHeaderLabel.text = $"Processors ({_compatibleDataProcessors.Count})"; // Show count when collapsed
                _processorListContainer.parent.style.display = DisplayStyle.None; // Hide ScrollView
            }
            else // Expanded
            {
                _processorToggleCollapseButton.text = ArrowExpanded;
                _processorHeaderLabel.text = "Processors";
                _processorListContainer.parent.style.display = DisplayStyle.Flex; // Show ScrollView

                // Populate processor buttons
                foreach (IDataProcessor processor in _compatibleDataProcessors)
                {
                    Button procButton = new(() => RunDataProcessor(processor))
                    {
                        text = processor.Name, // From IDataProcessor
                        tooltip = processor.Description,
                        style = { marginTop = 2, marginBottom = 2 }, // From IDataProcessor
                    };
                    _processorListContainer.Add(procButton);
                }
            }
        }

        private void ToggleProcessorContentCollapse()
        {
            _isProcessorColumnContentCollapsed = !_isProcessorColumnContentCollapsed;
            EditorPrefs.SetBool(
                PrefsProcessorColumnCollapsedKey,
                _isProcessorColumnContentCollapsed
            );
            BuildProcessorColumnView(); // Rebuild to show/hide content
        }

        private void RunDataProcessor(IDataProcessor processor)
        {
            if (
                processor == null
                || _namespaceController.SelectedType == null
                || _selectedObjects == null
            )
            {
                return;
            }

            string message =
                $"Run processor '{processor.Name}' on {_selectedObjects.Count} "
                + $"object{(_selectedObjects.Count != 1 ? "s" : "")} "
                + $"of type '{NamespaceController.GetTypeDisplayName(_namespaceController.SelectedType)}'?";

            // Use existing generic confirmation popover
            BuildAndOpenConfirmationPopover(
                message,
                "Run",
                () =>
                { // OnConfirm action
                    try
                    {
                        Debug.Log($"Running processor '{processor.Name}'...");
                        processor.Process(_namespaceController.SelectedType, _selectedObjects); // Pass ALL selected objects for the type
                        Debug.Log($"Processor '{processor.Name}' finished.");

                        // Assume processor might have changed asset data
                        AssetDatabase.SaveAssets(); // Save any changes
                        AssetDatabase.Refresh(); // Refresh asset database
                        ScheduleRefresh(); // Full refresh of DataVisualizer
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error running processor '{processor.Name}': {ex}");
                        EditorUtility.DisplayDialog(
                            "Processor Error",
                            $"An error occurred while running '{processor.Name}':\n{ex.Message}",
                            "OK"
                        );
                    }
                },
                _processorAreaElement.Q<Button>() // Trigger element for popover positioning (e.g., the run button itself or column header)
            // This might need specific trigger later if run button is deep.
            // For now, using any button in the column as placeholder.
            );
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

        private void PerformTypeSearch(string searchText)
        {
            HashSet<VisualElement> typeElements =
                _namespaceController._namespaceCache.Values.ToHashSet();
            try
            {
                if (
                    string.IsNullOrWhiteSpace(searchText)
                    || string.Equals(SearchPlaceholder, searchText, StringComparison.Ordinal)
                )
                {
                    foreach (VisualElement typeItem in _namespaceController._namespaceCache.Values)
                    {
                        typeItem.style.display = DisplayStyle.Flex;
                    }
                    return;
                }

                string[] searchTerms = searchText.Split(
                    new[] { ' ' },
                    StringSplitOptions.RemoveEmptyEntries
                );

                foreach (
                    KeyValuePair<Type, VisualElement> entry in _namespaceController._namespaceCache
                )
                {
                    typeElements.Add(entry.Value);
                    string typeDisplayName = NamespaceController.GetTypeDisplayName(entry.Key);
                    bool shouldDisplay = Array.TrueForAll(
                        searchTerms,
                        term => typeDisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    );
                    entry.Value.style.display = shouldDisplay
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                }
            }
            finally
            {
                int hiddenNamespaces = 0;
                foreach (
                    VisualElement parent in _namespaceController
                        ._namespaceCache.Values.Select(value => value.parent?.parent)
                        .Where(parent => parent != null)
                        .Distinct()
                )
                {
                    bool allInvisible = parent
                        .IterateChildrenRecursively()
                        .Where(typeElements.Contains)
                        .All(child => child.style.display == DisplayStyle.None);
                    parent.style.display = allInvisible ? DisplayStyle.None : DisplayStyle.Flex;
                    if (allInvisible)
                    {
                        hiddenNamespaces++;
                    }
                }

                HiddenNamespaces = hiddenNamespaces;
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
            foreach (ScriptableObject obj in _allManagedObjectsCache)
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
                    OpenPopover(_searchPopover, _searchField, shouldFocus: false);
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

        private static string EscapeRichText(string input)
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
                        ) && !typeof(Object).IsAssignableFrom(field.FieldType)
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
        }

        private VisualElement CreatePopoverBase(string popoverName)
        {
            VisualElement popover = new() { name = popoverName, focusable = true };
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
            dragHandle.RegisterCallback<KeyDownEvent>(HandlePopoverKeyDown);
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
                popover.Focus();
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

            VisualElement dragHandle = _confirmActionPopover.Q(className: "popover-drag-handle");
            VisualElement contentWrapper = _confirmActionPopover.Q(
                name: $"{_confirmActionPopover.name}-content-wrapper"
            );
            if (dragHandle == null || contentWrapper == null)
            {
                return;
            }

            dragHandle.AddToClassList("delete-button");
            dragHandle.Clear();
            contentWrapper.Clear();

            dragHandle.Add(
                new Label("Remove") { style = { unityFontStyleAndWeight = FontStyle.Bold } }
            );

            Button closeButton = new(CloseActivePopover) { text = "X" };
            closeButton.AddToClassList("popover-close-button");
            closeButton.AddToClassList(StyleConstants.ClickableClass);
            dragHandle.Add(closeButton);

            Label messageLabel = new(message)
            {
                style = { whiteSpace = WhiteSpace.Normal, marginBottom = 15 },
            };
            contentWrapper.Add(messageLabel);

            VisualElement buttonContainer = new();
            buttonContainer.AddToClassList("popover-button-container");
            contentWrapper.Add(buttonContainer);

            Button cancelButton = new(CloseActivePopover) { text = "Cancel" };
            cancelButton.AddToClassList(StyleConstants.PopoverButtonClass);
            cancelButton.AddToClassList(StyleConstants.PopoverCancelButtonClass);
            cancelButton.AddToClassList(StyleConstants.ClickableClass);
            buttonContainer.Add(cancelButton);

            Button confirmButton = new(Confirm) { text = confirmText, userData = (Action)Confirm };
            confirmButton.AddToClassList(StyleConstants.PopoverPrimaryActionClass);
            confirmButton.AddToClassList(StyleConstants.PopoverButtonClass);
            confirmButton.AddToClassList("popover-delete-button");
            confirmButton.AddToClassList(StyleConstants.ClickableClass);
            buttonContainer.Add(confirmButton);

            OpenPopover(_confirmActionPopover, triggerElement);
            return;

            void Confirm()
            {
                onConfirm();
                CloseActivePopover();
            }
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
            bool isNested = false,
            bool shouldFocus = true
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
                                    if (shouldFocus)
                                    {
                                        popover.Focus();
                                    }
                                    rootVisualElement.RegisterCallback<PointerDownEvent>(
                                        HandleClickOutsidePopover,
                                        TrickleDown.TrickleDown
                                    );
                                }
                            })
                            .ExecuteLater(10);
                    }
                    else if (_activeNestedPopover == popover)
                    {
                        if (shouldFocus)
                        {
                            popover.Focus();
                        }
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
                _typeAddSearchField?.SetValueWithoutNotify("");
            }
            else if (_activePopover == _inspectorLabelSuggestionsPopover)
            {
                _currentLabelSuggestionItems.Clear();
                _labelSuggestionHighlightIndex = -1;
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
            VisualElement activePopover = _activeNestedPopover ?? _activePopover;
            if (activePopover != null && activePopover.style.display == DisplayStyle.Flex)
            {
                switch (evt.keyCode)
                {
                    case KeyCode.Escape:
                    {
                        CloseActivePopover();
                        evt.PreventDefault();
                        evt.StopPropagation();
                        return;
                    }
                    case KeyCode.DownArrow:
                    case KeyCode.UpArrow:
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                    {
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

                        if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                        {
                            Button primaryButton = activePopover
                                .IterateChildrenRecursively()
                                .Where(child =>
                                    child.ClassListContains(
                                        StyleConstants.PopoverPrimaryActionClass
                                    )
                                )
                                .OfType<Button>()
                                .FirstOrDefault();

                            if (primaryButton?.userData is Action action)
                            {
                                action.Invoke();
                            }
                        }

                        break;
                    }
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

        private void HandlePopoverKeyDown(KeyDownEvent evt)
        {
            VisualElement activePopover = _activeNestedPopover ?? _activePopover;
            if (activePopover == null || activePopover.style.display != DisplayStyle.Flex)
            {
                return;
            }

            switch (evt.keyCode)
            {
                case KeyCode.Escape:
                {
                    CloseActivePopover();
                    evt.PreventDefault();
                    evt.StopPropagation();
                    return;
                }
                case KeyCode.None when evt.character is '\n' or '\r':
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                {
                    Button primaryButton = activePopover
                        .IterateChildrenRecursively()
                        .Where(child =>
                            child.ClassListContains(StyleConstants.PopoverPrimaryActionClass)
                        )
                        .OfType<Button>()
                        .FirstOrDefault();

                    if (primaryButton?.userData is Action action)
                    {
                        action.Invoke();
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
                    ? "Persist State in UserState: "
                    : "Persist State in Settings Asset: ",
                value =>
                {
                    if (prefsToggle != null)
                    {
                        prefsToggle.Label = value
                            ? "Persist State in UserState: "
                            : "Persist State in Settings Asset: ";
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

            ActionButtonToggle selectionToggle = null;
            selectionToggle = new ActionButtonToggle(
                settings.selectActiveObject
                    ? "Don't Select Active Object: "
                    : "Select Active Object: ",
                value =>
                {
                    if (selectionToggle != null)
                    {
                        selectionToggle.Label = value
                            ? "Don't Select Active Object: "
                            : "Select Active Object: ";
                    }
                }
            )
            {
                value = settings.selectActiveObject,
            };
            selectionToggle.AddToClassList("settings-prefs-toggle");
            selectionToggle.RegisterValueChangedCallback(evt =>
            {
                bool newSelectActiveObject = evt.newValue;
                bool previousSelectActiveObject = Settings.selectActiveObject;
                if (newSelectActiveObject == previousSelectActiveObject)
                {
                    return;
                }

                DataVisualizerSettings localSettings = Settings;
                localSettings.selectActiveObject = newSelectActiveObject;
                AssetDatabase.SaveAssets();
            });
            contentWrapper.Add(selectionToggle);

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
                (_, context) =>
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

        private void OpenRenamePopover(
            Label titleLabel,
            VisualElement source,
            ScriptableObject dataObject
        )
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

            BuildRenamePopoverContent(titleLabel, currentPath, dataObject.name);
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
            Button createButton = new(Create) { text = "Create", userData = (Action)Create };

            createButton.AddToClassList(StyleConstants.PopoverPrimaryActionClass);
            createButton.AddToClassList(StyleConstants.PopoverButtonClass);
            createButton.AddToClassList("popover-create-button");
            createButton.AddToClassList(StyleConstants.ClickableClass);
            buttonContainer.Add(cancelButton);
            buttonContainer.Add(createButton);
            contentWrapper.Add(buttonContainer);
            return;

            void Create()
            {
                HandleCreateConfirmed(type, nameTextField, errorLabel);
            }
        }

        private void BuildRenamePopoverContent(
            Label titleLabel,
            string originalPath,
            string originalName
        )
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
            Button renameButton = new(Rename) { text = "Rename", userData = (Action)Rename };
            renameButton.AddToClassList(StyleConstants.PopoverPrimaryActionClass);
            renameButton.AddToClassList(StyleConstants.PopoverButtonClass);
            renameButton.AddToClassList("popover-rename-button");
            renameButton.AddToClassList(StyleConstants.ClickableClass);
            buttonContainer.Add(cancelButton);
            buttonContainer.Add(renameButton);
            contentWrapper.Add(buttonContainer);
            return;

            void Rename()
            {
                HandleRenameConfirmed(titleLabel, nameTextField, errorLabel);
            }
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
            string typedDirectory = Path.Combine(
                    directory,
                    (type.FullName ?? type.Name).Replace(
                        ".",
                        Path.DirectorySeparatorChar.ToString()
                    )
                )
                .SanitizePath();
            DirectoryHelper.EnsureDirectoryExists(typedDirectory);

            string proposedName = $"{newName}.asset";
            string proposedPath = Path.Combine(typedDirectory, proposedName).SanitizePath();
            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(proposedPath);
            if (!string.Equals(proposedPath, uniquePath, StringComparison.Ordinal))
            {
                errorLabel.text = "Name is not unique.";
                errorLabel.style.display = DisplayStyle.Flex;
                return;
            }

            ScriptableObject instance = CreateInstance(type);
            if (instance is ICreatable creatable)
            {
                creatable.BeforeCreate();
            }
            else
            {
                creatable = null;
            }
            AssetDatabase.CreateAsset(instance, uniquePath);
            AssetDatabase.SaveAssets();
            creatable?.AfterCreate();

            CloseActivePopover();
            if (type == _namespaceController.SelectedType)
            {
                _selectedObjects.Add(instance);
                BuildObjectRow(instance, _objectListContainer.childCount);
                foreach (VisualElement child in _objectListContainer.Children())
                {
                    NamespaceController.RecalibrateVisualElements(child, offset: 1);
                }
            }
        }

        private void HandleRenameConfirmed(Label titleLabel, TextField nameField, Label errorLabel)
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

            string newFullName = newName + Path.GetExtension(originalPath);
            string newPath = Path.Combine(directory, newFullName).SanitizePath();
            string validationError = AssetDatabase.ValidateMoveAsset(originalPath, newPath);

            if (!string.IsNullOrWhiteSpace(validationError))
            {
                errorLabel.text = $"Invalid: {validationError}";
                errorLabel.style.display = DisplayStyle.Flex;
                return;
            }

            ScriptableObject original = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                originalPath
            );
            if (original is IRenamable renamable)
            {
                renamable.BeforeRename(newName);
            }
            else
            {
                renamable = null;
            }

            string error = AssetDatabase.RenameAsset(originalPath, newName);
            if (string.IsNullOrWhiteSpace(error))
            {
                AssetDatabase.SaveAssets();
                renamable?.AfterRename(newName);
                Debug.Log($"Asset renamed successfully to: {newName}");
                CloseActivePopover();
                if (_selectedObject == original && _assetNameTextField != null)
                {
                    _assetNameTextField.value = newName;
                }

                if (titleLabel != null)
                {
                    if (original is IDisplayable displayable)
                    {
                        titleLabel.text = displayable.Title;
                    }
                    else
                    {
                        titleLabel.text = newName;
                    }
                }
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
            Button deleteButton = new(HandleDeleteConfirmed)
            {
                text = "Delete",
                userData = (Action)HandleDeleteConfirmed,
            };
            deleteButton.AddToClassList(StyleConstants.PopoverPrimaryActionClass);
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

            int index = _selectedObjects.IndexOf(objectToDelete);
            _selectedObjects.Remove(objectToDelete);
            _selectedObjects.RemoveAll(obj => obj == null);
            _objectVisualElementMap.Remove(objectToDelete, out VisualElement visualElement);
            int targetIndex = _selectedObject == objectToDelete ? Mathf.Max(0, index - 1) : 0;

            bool deleted = AssetDatabase.DeleteAsset(path);
            if (deleted)
            {
                Debug.Log($"Asset '{path}' deleted successfully.");
                AssetDatabase.Refresh();
                VisualElement parent = visualElement?.parent;
                visualElement?.RemoveFromHierarchy();
                if (parent != null)
                {
                    foreach (VisualElement child in parent.Children())
                    {
                        NamespaceController.RecalibrateVisualElements(child, offset: 1);
                    }
                }
                if (targetIndex < _selectedObjects.Count)
                {
                    SelectObject(_selectedObjects[targetIndex]);
                }
                else if (0 < _selectedObjects.Count)
                {
                    SelectObject(_selectedObjects[0]);
                }
                else
                {
                    _emptyObjectLabel.style.display = DisplayStyle.Flex;
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
            _namespaceColumnLabel = new Label("Namespaces")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, paddingLeft = 2 },
            };
            nsHeader.Add(_namespaceColumnLabel);
            nsHeader.AddToClassList(NamespaceGroupHeaderClass);

            VisualElement addButtonHeader = new()
            {
                style = { flexDirection = FlexDirection.Row, alignItems = Align.FlexEnd },
            };
            nsHeader.Add(addButtonHeader);

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

            _addTypesFromDataFolderButton = new Button(() =>
            {
                string selectedAbsolutePath = EditorUtility.OpenFolderPanel(
                    title: "Select Data Object Type Load Folder (Must be inside Assets)",
                    folder: "Assets",
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
                if (
                    selectedAbsolutePath.Equals(
                        projectAssetsPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    relativePath = "Assets";
                }
                else
                {
                    relativePath =
                        "Assets" + selectedAbsolutePath.Substring(projectAssetsPath.Length);
                    relativePath = relativePath.Replace("//", "/");
                }

                HashSet<Type> currentlyManagedTypes = _scriptableObjectTypes
                    .SelectMany(x => x.Value)
                    .ToHashSet();
                List<Type> scriptableObjectTypes = AssetDatabase
                    .FindAssets($"t:{nameof(ScriptableObject)}", new[] { relativePath })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<ScriptableObject>)
                    .Where(so => so != null)
                    .Select(so => so.GetType())
                    .Where(IsLoadableType)
                    .Where(type => !currentlyManagedTypes.Contains(type))
                    .Distinct()
                    .ToList();

                bool stateChanged = false;
                foreach (Type typeToAdd in scriptableObjectTypes)
                {
                    string namespaceKey = NamespaceController.GetNamespaceKey(typeToAdd);
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
                    SyncNamespaceChanges();
                }
            })
            {
                text = "+",
                tooltip = "Load Types from Data Folder (Scriptable Object Instances)",
            };
            _addTypesFromDataFolderButton.AddToClassList("load-from-data-folder-button");
            _addTypesFromDataFolderButton.AddToClassList("icon-button");
            _addTypesFromDataFolderButton.AddToClassList(StyleConstants.ClickableClass);

            _addTypesFromScriptFolderButton = new Button(() =>
            {
                string selectedAbsolutePath = EditorUtility.OpenFolderPanel(
                    title: "Select Script Load Folder (Must be inside Assets)",
                    folder: "Assets",
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
                if (
                    selectedAbsolutePath.Equals(
                        projectAssetsPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    relativePath = "Assets";
                }
                else
                {
                    relativePath =
                        "Assets" + selectedAbsolutePath.Substring(projectAssetsPath.Length);
                    relativePath = relativePath.Replace("//", "/");
                }

                HashSet<Type> currentlyManagedTypes = _scriptableObjectTypes
                    .SelectMany(x => x.Value)
                    .ToHashSet();
                List<Type> scriptableObjectTypes = AssetDatabase
                    .FindAssets("t:Monoscript", new[] { relativePath })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<MonoScript>)
                    .Where(script => script != null)
                    .Select(script => script.GetClass())
                    .Where(IsLoadableType)
                    .Where(type => !currentlyManagedTypes.Contains(type))
                    .Distinct()
                    .ToList();

                bool stateChanged = false;
                foreach (Type typeToAdd in scriptableObjectTypes)
                {
                    string namespaceKey = NamespaceController.GetNamespaceKey(typeToAdd);
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
                    SyncNamespaceChanges();
                }
            })
            {
                text = "+",
                tooltip = "Load Types from Scripts (Scripts Folder)",
            };
            _addTypesFromScriptFolderButton.AddToClassList("load-from-script-folder-button");
            _addTypesFromScriptFolderButton.AddToClassList("icon-button");
            _addTypesFromScriptFolderButton.AddToClassList(StyleConstants.ClickableClass);

            addButtonHeader.Add(_addTypeButton);
            addButtonHeader.Add(_addTypesFromDataFolderButton);
            addButtonHeader.Add(_addTypesFromScriptFolderButton);
            namespaceColumn.Add(nsHeader);

            _typeSearchField = new TextField { name = "type-search-field" };
            _typeSearchField.AddToClassList("type-search-field");
            _typeSearchField.SetPlaceholderText(SearchPlaceholder, changeValueOnFocus: false);
            _typeSearchField.RegisterValueChangedCallback(evt => PerformTypeSearch(evt.newValue));
            namespaceColumn.Add(_typeSearchField);

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

                HashSet<string> managedTypeFullNames = _namespaceController
                    .GetAllManagedTypeNames()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                IOrderedEnumerable<IGrouping<string, Type>> groupedTypes =
                    LoadRelevantScriptableObjectTypes()
                        .Except(_scriptableObjectTypes.Values.SelectMany(x => x))
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
                    SyncNamespaceChanges();
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
            _createObjectButton = null;
            _createObjectButton = new Button(() =>
            {
                if (_namespaceController.SelectedType == null)
                {
                    return;
                }
                BuildCreatePopoverContent(_namespaceController.SelectedType);
                OpenPopover(_createPopover, _createObjectButton);
            })
            {
                text = "+",
                tooltip = "Create New Object",
                name = "create-object-button",
            };
            _createObjectButton.AddToClassList("create-button");
            _createObjectButton.AddToClassList("icon-button");
            _createObjectButton.AddToClassList(StyleConstants.ClickableClass);
            UpdateCreateObjectButtonStyle();
            objectHeader.Add(_createObjectButton);
            objectColumn.Add(objectHeader);

            objectColumn.Add(_processorAreaElement);

            _labelFilterSelectionRoot = new VisualElement
            {
                name = "label-filter-section-root",
                style =
                {
                    marginBottom = 5,
                    paddingBottom = new StyleLength(new Length(2, LengthUnit.Pixel)),
                    paddingLeft = new StyleLength(new Length(2, LengthUnit.Pixel)),
                    paddingRight = new StyleLength(new Length(2, LengthUnit.Pixel)),
                    paddingTop = new StyleLength(new Length(2, LengthUnit.Pixel)),
                },
            };
            objectColumn.Add(_labelFilterSelectionRoot);

            _availableLabelsContainer = new VisualElement { name = "available-labels-container" };
            _availableLabelsContainer.AddToClassList("label-pill-container");
            VisualElement availableRow = new() { name = "available-row" };
            availableRow.AddToClassList("label-row-container");
            availableRow.AddToClassList("label-row-container--available");
            Label availableLabel = new("Labels:")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginRight = 5,
                    minWidth = 60,
                },
            };
            availableLabel.AddToClassList("label-header");
            availableRow.Add(availableLabel);
            availableRow.Add(_availableLabelsContainer);
            _labelFilterSelectionRoot.Add(availableRow);

            VisualElement logicalGrouping = new() { name = "label-logical-grouping" };
            logicalGrouping.AddToClassList("label-logical-grouping");
            _labelFilterSelectionRoot.Add(logicalGrouping);

            VisualElement andRow = new() { name = "and-filter-row" };
            andRow.AddToClassList("label-row-container");
            Label andLabel = new("AND:")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginRight = 5,
                    minWidth = 60,
                },
            };
            andLabel.AddToClassList("label-header");
            andRow.Add(andLabel);
            _andLabelsContainer = new VisualElement { name = "and-labels-container" };
            _andLabelsContainer.AddToClassList("label-pill-container");
            andRow.Add(_andLabelsContainer);
            logicalGrouping.Add(andRow);

            _andOrToggle = new HorizontalToggle()
            {
                name = "and-or-toggle",
                LeftText = "AND &&",
                RightText = "OR ||",
            };
            _andOrToggle.OnLeftSelected += () =>
            {
                _andOrToggle.Indicator.style.backgroundColor = new Color(0, 0.392f, 0);
                _andOrToggle.LeftLabel.EnableInClassList(StyleConstants.ClickableClass, false);
                _andOrToggle.RightLabel.EnableInClassList(StyleConstants.ClickableClass, true);
                if (
                    _currentTypeLabelFilterConfig != null
                    && _currentTypeLabelFilterConfig.combinationType != LabelCombinationType.And
                )
                {
                    _currentTypeLabelFilterConfig.combinationType = LabelCombinationType.And;
                    SaveLabelFilterConfig(_currentTypeLabelFilterConfig);
                    UpdateLabelAreaAndFilter();
                }
            };
            _andOrToggle.OnRightSelected += () =>
            {
                _andOrToggle.Indicator.style.backgroundColor = new Color(1f, 0.5f, 0.3137254902f);
                _andOrToggle.LeftLabel.EnableInClassList(StyleConstants.ClickableClass, true);
                _andOrToggle.RightLabel.EnableInClassList(StyleConstants.ClickableClass, false);
                if (
                    _currentTypeLabelFilterConfig != null
                    && _currentTypeLabelFilterConfig.combinationType != LabelCombinationType.Or
                )
                {
                    _currentTypeLabelFilterConfig.combinationType = LabelCombinationType.Or;
                    SaveLabelFilterConfig(_currentTypeLabelFilterConfig);
                    UpdateLabelAreaAndFilter();
                }
            };
            switch (_currentTypeLabelFilterConfig?.combinationType)
            {
                case LabelCombinationType.And:
                {
                    _andOrToggle.SelectLeft(force: true);
                    break;
                }
                case LabelCombinationType.Or:
                {
                    _andOrToggle.SelectRight(force: true);
                    break;
                }
            }
            logicalGrouping.Add(_andOrToggle);

            VisualElement orRow = new() { name = "or-filter-row" };
            orRow.AddToClassList("label-row-container");
            Label orLabel = new("OR:")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    marginRight = 5,
                    minWidth = 60,
                },
            };
            orLabel.AddToClassList("label-header");
            orRow.Add(orLabel);
            _orLabelsContainer = new VisualElement { name = "or-labels-container" };
            _orLabelsContainer.AddToClassList("label-pill-container");

            orRow.Add(_orLabelsContainer);
            logicalGrouping.Add(orRow);

            _filterStatusLabel = new Label("")
            {
                name = "filter-status-label",
                style =
                {
                    color = Color.gray,
                    alignSelf = Align.Center,
                    marginTop = 3,
                    minHeight = 12,
                },
            };
            _labelFilterSelectionRoot.Add(_filterStatusLabel);

            SetupDropTarget(_availableLabelsContainer, LabelFilterSection.Available);
            SetupDropTarget(_andLabelsContainer, LabelFilterSection.AND);
            SetupDropTarget(_orLabelsContainer, LabelFilterSection.OR);

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

        private void ListenForPropertyChange(InspectorElement inspectorElement)
        {
            if (_currentInspectorScriptableObject == null)
            {
                return;
            }

            SerializedProperty property = _currentInspectorScriptableObject.FindProperty(
                nameof(BaseDataObject._title)
            );
            if (property == null)
            {
                property = _currentInspectorScriptableObject.FindProperty(
                    nameof(IDisplayable.Title)
                );
                if (property == null)
                {
                    return;
                }
            }
            inspectorElement.TrackPropertyValue(
                property,
                _ =>
                {
                    rootVisualElement
                        .schedule.Execute(() => RefreshSelectedElementVisuals(_selectedObject))
                        .ExecuteLater(1);
                }
            );
        }

        private void SetupDropTarget(VisualElement container, LabelFilterSection targetSection)
        {
            container.RegisterCallback<DragEnterEvent>(evt =>
            {
                string draggedText = DragAndDrop.GetGenericData("DraggedLabelText") as string;
                if (!string.IsNullOrWhiteSpace(draggedText))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                    container.AddToClassList("drop-target-hover");
                }
                evt.StopPropagation();
            });

            container.RegisterCallback<DragLeaveEvent>(evt =>
            {
                container.RemoveFromClassList("drop-target-hover");
                evt.StopPropagation();
            });

            container.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                string draggedText = DragAndDrop.GetGenericData("DraggedLabelText") as string;
                if (!string.IsNullOrWhiteSpace(draggedText))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                }
                evt.StopPropagation();
            });

            container.RegisterCallback<DragPerformEvent>(evt =>
            {
                string draggedLabelText = DragAndDrop.GetGenericData("DraggedLabelText") as string;
                string sourceSectionString = DragAndDrop.GetGenericData("SourceSection") as string;

                if (
                    !string.IsNullOrWhiteSpace(draggedLabelText)
                    && !string.IsNullOrWhiteSpace(sourceSectionString)
                    && Enum.TryParse(sourceSectionString, out LabelFilterSection sourceSection)
                )
                {
                    DragAndDrop.AcceptDrag();

                    bool changed = false;
                    if (_currentTypeLabelFilterConfig != null)
                    {
                        switch (targetSection)
                        {
                            case LabelFilterSection.AND:
                            {
                                if (
                                    !_currentTypeLabelFilterConfig.andLabels.Contains(
                                        draggedLabelText
                                    )
                                )
                                {
                                    _currentTypeLabelFilterConfig.andLabels.Add(draggedLabelText);
                                    changed = true;
                                    if (sourceSection == LabelFilterSection.OR)
                                    {
                                        _currentTypeLabelFilterConfig.orLabels.Remove(
                                            draggedLabelText
                                        );
                                    }
                                }

                                break;
                            }
                            case LabelFilterSection.OR:
                            {
                                if (
                                    !_currentTypeLabelFilterConfig.orLabels.Contains(
                                        draggedLabelText
                                    )
                                )
                                {
                                    _currentTypeLabelFilterConfig.orLabels.Add(draggedLabelText);
                                    changed = true;
                                    if (sourceSection == LabelFilterSection.AND)
                                    {
                                        _currentTypeLabelFilterConfig.andLabels.Remove(
                                            draggedLabelText
                                        );
                                    }
                                }

                                break;
                            }
                            case LabelFilterSection.Available:
                            {
                                switch (sourceSection)
                                {
                                    case LabelFilterSection.AND:
                                    {
                                        changed = _currentTypeLabelFilterConfig.andLabels.Remove(
                                            draggedLabelText
                                        );
                                        break;
                                    }
                                    case LabelFilterSection.OR:
                                    {
                                        changed = _currentTypeLabelFilterConfig.orLabels.Remove(
                                            draggedLabelText
                                        );
                                        break;
                                    }
                                }

                                break;
                            }
                        }

                        if (changed)
                        {
                            SaveLabelFilterConfig(_currentTypeLabelFilterConfig);
                            PopulateLabelPillContainers();
                            ApplyLabelFilter();
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("DragPerform: Invalid drag data received.");
                }

                _draggedLabelText = null;
                container.RemoveFromClassList("drop-target-hover");
                evt.StopPropagation();
            });
        }

        internal void UpdateLabelAreaAndFilter()
        {
            ClearLabelFilterUI();
            _currentTypeLabelFilterConfig = null;

            if (_namespaceController.SelectedType == null || _selectedObjects == null)
            {
                if (_filterStatusLabel != null)
                {
                    _filterStatusLabel.text = "";
                }

                if (_labelFilterSelectionRoot != null)
                {
                    _labelFilterSelectionRoot.style.display = DisplayStyle.None;
                }

                ApplyLabelFilter();
                return;
            }

            _currentUniqueLabelsForType.Clear();
            SortedSet<string> labelSet = new(StringComparer.OrdinalIgnoreCase);
            foreach (ScriptableObject obj in _selectedObjects)
            {
                if (obj == null)
                {
                    continue;
                }

                string[] labels = AssetDatabase.GetLabels(obj);
                foreach (string label in labels)
                {
                    labelSet.Add(label);
                }
            }

            foreach (string label in labelSet)
            {
                _currentUniqueLabelsForType.Add(label);
            }

            _currentTypeLabelFilterConfig = LoadOrCreateLabelFilterConfig(
                _namespaceController.SelectedType
            );

            bool configChanged = false;
            int removedAnd = _currentTypeLabelFilterConfig.andLabels.RemoveAll(label =>
                !_currentUniqueLabelsForType.Contains(label)
            );
            int removedOr = _currentTypeLabelFilterConfig.orLabels.RemoveAll(label =>
                !_currentUniqueLabelsForType.Contains(label)
            );

            if (removedAnd > 0 || removedOr > 0)
            {
                configChanged = true;
            }

            if (configChanged)
            {
                SaveLabelFilterConfig(_currentTypeLabelFilterConfig);
            }

            PopulateLabelPillContainers();
            ApplyLabelFilter();
        }

        private void ClearLabelFilterUI()
        {
            _availableLabelsContainer?.Clear();
            _andLabelsContainer?.Clear();
            _orLabelsContainer?.Clear();
            _currentUniqueLabelsForType.Clear();
        }

        private List<string> GetCurrentlyAvailableLabels()
        {
            if (_currentTypeLabelFilterConfig == null)
            {
                return _currentUniqueLabelsForType.ToList();
            }

            return _currentUniqueLabelsForType
                .Where(l =>
                    !(
                        _currentTypeLabelFilterConfig.andLabels.Contains(l)
                        && _currentTypeLabelFilterConfig.orLabels.Contains(l)
                    )
                )
                .ToList();
        }

        private void PopulateLabelPillContainers()
        {
            List<string> availableLabels = GetCurrentlyAvailableLabels();
            PopulateSingleLabelContainer(
                _availableLabelsContainer,
                availableLabels,
                LabelFilterSection.Available
            );
            PopulateSingleLabelContainer(
                _andLabelsContainer,
                _currentTypeLabelFilterConfig.andLabels,
                LabelFilterSection.AND
            );
            PopulateSingleLabelContainer(
                _orLabelsContainer,
                _currentTypeLabelFilterConfig.orLabels,
                LabelFilterSection.OR
            );

            if (_labelFilterSelectionRoot != null)
            {
                _labelFilterSelectionRoot.style.display =
                    availableLabels.Count != 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void PopulateSingleLabelContainer(
            VisualElement container,
            List<string> labels,
            LabelFilterSection section
        )
        {
            if (container == null)
            {
                return;
            }
            container.Clear();
            if (labels == null)
            {
                return;
            }

            foreach (string labelText in labels.OrderBy(label => label))
            {
                container.Add(CreateLabelPill(labelText, section));
            }
        }

        private VisualElement CreateLabelPill(string labelText, LabelFilterSection currentSection)
        {
            Color labelColor = GetColorForLabel(labelText);
            VisualElement pillContainer = new()
            {
                name = $"label-pill-container-{labelText.Replace(" ", "-").ToLowerInvariant()}",
                style = { backgroundColor = labelColor },
                userData = labelText,
            };
            pillContainer.AddToClassList("label-pill");

            Label labelElement = new(labelText)
            {
                style =
                {
                    color = IsColorDark(labelColor) ? Color.white : Color.black,
                    marginRight =
                        currentSection == LabelFilterSection.AND
                        || currentSection == LabelFilterSection.OR
                            ? 2
                            : 0,
                },
            };
            labelElement.AddToClassList("label-pill-text");
            labelElement.pickingMode = PickingMode.Ignore;
            pillContainer.Add(labelElement);

            pillContainer.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    _draggedLabelText = labelText;
                    _dragSourceSection = currentSection;

                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.SetGenericData("DraggedLabelText", _draggedLabelText);
                    DragAndDrop.SetGenericData("SourceSection", _dragSourceSection.ToString());
                    DragAndDrop.StartDrag(labelText);
                    evt.StopPropagation();
                }
            });

            switch (currentSection)
            {
                case LabelFilterSection.Available:
                {
                    pillContainer.tooltip = $"Drag '{labelText}' to an AND/OR filter section.";
                    break;
                }
                case LabelFilterSection.AND:
                case LabelFilterSection.OR:
                {
                    // Add "X" button for removal
                    Button removeButton = new(() => RemoveLabelFromFilter(labelText, currentSection)
                    )
                    {
                        text = "x",
                        name = $"remove-label-{labelText.Replace(" ", "-")}",
                        tooltip = $"Remove '{labelText}' from filter",
                        style = { color = labelElement.style.color.value },
                    };
                    removeButton.AddToClassList("label-pill-remove-button");
                    removeButton.AddToClassList(StyleConstants.ClickableClass);
                    pillContainer.Add(removeButton);
                    break;
                }
                default:
                {
                    throw new InvalidEnumArgumentException(
                        nameof(currentSection),
                        (int)currentSection,
                        typeof(LabelFilterSection)
                    );
                }
            }
            return pillContainer;
        }

        private void RemoveLabelFromFilter(string labelText, LabelFilterSection sectionItWasIn)
        {
            if (_currentTypeLabelFilterConfig == null)
            {
                return;
            }

            bool changed = false;

            switch (sectionItWasIn)
            {
                case LabelFilterSection.AND:
                {
                    changed = _currentTypeLabelFilterConfig.andLabels.Remove(labelText);
                    break;
                }
                case LabelFilterSection.OR:
                {
                    changed = _currentTypeLabelFilterConfig.orLabels.Remove(labelText);
                    break;
                }
            }

            if (changed)
            {
                SaveLabelFilterConfig(_currentTypeLabelFilterConfig);
                PopulateLabelPillContainers();
                ApplyLabelFilter();
            }
        }

        private void ApplyLabelFilter()
        {
            if (_namespaceController.SelectedType == null || _selectedObjects == null)
            {
                _filteredObjects.Clear();
                if (_filterStatusLabel != null)
                {
                    _filterStatusLabel.text = "Select a type to see objects.";
                }

                BuildObjectsView();
                return;
            }
            if (_currentTypeLabelFilterConfig == null)
            {
                _currentTypeLabelFilterConfig = LoadOrCreateLabelFilterConfig(
                    _namespaceController.SelectedType
                );
            }

            switch (_currentTypeLabelFilterConfig.combinationType)
            {
                case LabelCombinationType.And:
                {
                    _andOrToggle?.SelectLeft(force: true);
                    break;
                }
                case LabelCombinationType.Or:
                {
                    _andOrToggle?.SelectRight(force: true);
                    break;
                }
            }

            _filteredObjects.Clear();
            List<string> andLabels = _currentTypeLabelFilterConfig.andLabels;
            List<string> orLabels = _currentTypeLabelFilterConfig.orLabels;

            bool noAndFilter = andLabels == null || andLabels.Count == 0;
            bool noOrFilter = orLabels == null || orLabels.Count == 0;

            if (noAndFilter && noOrFilter)
            {
                foreach (ScriptableObject selectedObject in _selectedObjects)
                {
                    _filteredObjects.Add(selectedObject);
                }
            }
            else
            {
                foreach (ScriptableObject obj in _selectedObjects)
                {
                    if (obj == null)
                    {
                        continue;
                    }

                    string[] objLabelsArray = AssetDatabase.GetLabels(obj);
                    HashSet<string> objLabelsSet = new(objLabelsArray, StringComparer.Ordinal);

                    bool matchesAnd =
                        noAndFilter
                        || andLabels.TrueForAll(filterLabel => objLabelsSet.Contains(filterLabel));
                    bool matchesOr =
                        noOrFilter
                        || orLabels.Exists(filterLabel => objLabelsSet.Contains(filterLabel));

                    switch (
                        _currentTypeLabelFilterConfig?.combinationType ?? LabelCombinationType.And
                    )
                    {
                        case LabelCombinationType.And:
                        {
                            if (matchesAnd && matchesOr)
                            {
                                _filteredObjects.Add(obj);
                            }

                            break;
                        }
                        case LabelCombinationType.Or:
                        {
                            if (matchesAnd || matchesOr)
                            {
                                _filteredObjects.Add(obj);
                            }

                            break;
                        }
                    }
                }
            }

            int totalCount = _selectedObjects.Count;
            int shownCount = _filteredObjects.Count;
            if (_filterStatusLabel != null)
            {
                if (shownCount == totalCount)
                {
                    _filterStatusLabel.style.display = DisplayStyle.None;
                }
                else
                {
                    _filterStatusLabel.style.display = DisplayStyle.Flex;
                    int hidden = totalCount - shownCount;
                    _filterStatusLabel.text =
                        hidden < 20 && hidden != totalCount
                            ? $"<b><color=yellow>{hidden}</color></b> objects hidden by label filter."
                            : $"<b><color=red>{hidden}</color></b> objects hidden by label filter.";
                }
            }

            BuildObjectsView();
        }

        private TypeLabelFilterConfig LoadOrCreateLabelFilterConfig(Type type)
        {
            TypeLabelFilterConfig config = null;
            PersistSettings(
                settings =>
                {
                    bool dirty = false;
                    if (settings.labelFilterConfigs == null)
                    {
                        settings.labelFilterConfigs = new List<TypeLabelFilterConfig>();
                        dirty = true;
                    }
                    config = settings.labelFilterConfigs.Find(existingConfig =>
                        string.Equals(
                            existingConfig.typeFullName,
                            type.FullName,
                            StringComparison.Ordinal
                        )
                    );
                    if (config == null)
                    {
                        config = new TypeLabelFilterConfig { typeFullName = type.FullName };
                        settings.labelFilterConfigs.Add(config);
                        dirty = true;
                    }
                    return dirty;
                },
                userState =>
                {
                    bool dirty = false;
                    if (userState.labelFilterConfigs == null)
                    {
                        userState.labelFilterConfigs = new List<TypeLabelFilterConfig>();
                        dirty = true;
                    }
                    config = userState.labelFilterConfigs.Find(existingConfig =>
                        string.Equals(
                            existingConfig.typeFullName,
                            type.FullName,
                            StringComparison.Ordinal
                        )
                    );
                    if (config == null)
                    {
                        config = new TypeLabelFilterConfig { typeFullName = type.FullName };
                        userState.labelFilterConfigs.Add(config);
                        dirty = true;
                    }
                    return dirty;
                }
            );
            return config;
        }

        private void SaveLabelFilterConfig(TypeLabelFilterConfig config)
        {
            PersistSettings(
                settings =>
                {
                    settings.labelFilterConfigs ??= new List<TypeLabelFilterConfig>();
                    TypeLabelFilterConfig existing = settings.labelFilterConfigs.Find(
                        existingConfig =>
                            string.Equals(
                                existingConfig.typeFullName,
                                config.typeFullName,
                                StringComparison.Ordinal
                            )
                    );
                    if (existing == null)
                    {
                        settings.labelFilterConfigs.Add(config);
                    }
                    else
                    {
                        settings.labelFilterConfigs.Remove(existing);
                        settings.labelFilterConfigs.Add(config);
                    }
                    return true;
                },
                userState =>
                {
                    userState.labelFilterConfigs ??= new List<TypeLabelFilterConfig>();
                    TypeLabelFilterConfig existing = userState.labelFilterConfigs.Find(
                        existingConfig =>
                            string.Equals(
                                existingConfig.typeFullName,
                                config.typeFullName,
                                StringComparison.Ordinal
                            )
                    );
                    if (existing == null)
                    {
                        userState.labelFilterConfigs.Add(config);
                    }
                    else
                    {
                        userState.labelFilterConfigs.Remove(existing);
                        userState.labelFilterConfigs.Add(config);
                    }
                    return true;
                }
            );
        }

        // --- Color Helpers for Labels ---
        private Color GetColorForLabel(string labelText)
        {
            if (string.IsNullOrWhiteSpace(labelText))
            {
                return Color.gray;
            }

            if (_labelColorCache.TryGetValue(labelText, out Color color))
            {
                return color;
            }

            if (_nextColorIndex < _predefinedLabelColors.Count)
            {
                color = _predefinedLabelColors[_nextColorIndex++];
            }
            else
            {
                // Generate a consistent color from hash for additional labels
                float hue = Mathf.Abs(labelText.GetHashCode() % 256) / 255f;
                color = Color.HSVToRGB(hue, 0.65f, 0.90f); // Ensure good saturation/value
            }
            _labelColorCache[labelText] = color;
            return color;
        }

        private bool IsColorDark(Color c)
        {
            return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b < 0.5f; // Luminance check
        }

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

            Button confirmButton = new(Confirm) { text = "Add", userData = (Action)Confirm };
            confirmButton.AddToClassList(StyleConstants.PopoverPrimaryActionClass);
            confirmButton.AddToClassList(StyleConstants.ClickableClass);
            confirmButton.AddToClassList(StyleConstants.PopoverButtonClass);
            confirmButton.AddToClassList("popover-confirm-button");
            buttonContainer.Add(confirmButton);
            return;

            void Confirm()
            {
                bool stateChanged = false;
                HashSet<Type> currentManagedList = _scriptableObjectTypes
                    .SelectMany(x => x.Value)
                    .ToHashSet();
                foreach (Type typeToAdd in typesToAdd)
                {
                    if (!currentManagedList.Add(typeToAdd))
                    {
                        continue;
                    }

                    string typeNamespace = NamespaceController.GetNamespaceKey(typeToAdd);
                    if (!_scriptableObjectTypes.TryGetValue(typeNamespace, out List<Type> types))
                    {
                        types = new List<Type>();
                        _scriptableObjectTypes[typeNamespace] = types;
                        _namespaceOrder[typeNamespace] = _namespaceOrder.Count;
                    }

                    types.Add(typeToAdd);
                    stateChanged = true;
                }

                CloseActivePopover();

                if (stateChanged)
                {
                    SyncNamespaceChanges();
                }
            }
        }

        private void SyncNamespaceChanges()
        {
            SyncNamespaceAndTypeOrders();
            LoadScriptableObjectTypes();
            BuildNamespaceView();
            PopulateSearchCache();
        }

        private void BuildNamespaceView()
        {
            _namespaceController.Build(this, ref _namespaceListContainer);
        }

        internal void BuildObjectsView()
        {
            _selectedObjects.RemoveAll(obj => obj == null);
            if (_objectListContainer == null)
            {
                return;
            }

            _objectListContainer.Clear();
            _objectVisualElementMap.Clear();
            _objectScrollView.scrollOffset = Vector2.zero;

            Type selectedType = _namespaceController.SelectedType;
            _emptyObjectLabel = new Label(
                $"No objects of type '{selectedType?.Name}' found.\nUse the '+' button above to create one."
            )
            {
                name = "empty-object-list-label",
                style = { alignSelf = Align.Center },
            };
            _emptyObjectLabel.AddToClassList("empty-object-list-label");
            _objectListContainer.Add(_emptyObjectLabel);
            if (selectedType != null && _selectedObjects.Count == 0)
            {
                _emptyObjectLabel.style.display = DisplayStyle.Flex;
                return;
            }

            _emptyObjectLabel.style.display = DisplayStyle.None;
            if (_filteredObjects == null || !_filteredObjects.Any())
            {
                // If _selectedObjects has items, then filter is active and hiding all
                if (_selectedObjects != null && _selectedObjects.Any())
                {
                    Label noMatchLabel = new(
                        $"No objects of type '{NamespaceController.GetTypeDisplayName(_namespaceController.SelectedType)}' match the current label filter."
                    )
                    {
                        style = { alignSelf = Align.Center },
                    };
                    noMatchLabel.AddToClassList("empty-object-list-label");
                    _objectListContainer.Add(noMatchLabel);
                }
                else
                {
                    Label noMatchLabel = new(
                        $"No objects of type '{NamespaceController.GetTypeDisplayName(_namespaceController.SelectedType)}' found."
                    )
                    {
                        style = { alignSelf = Align.Center },
                    };
                    noMatchLabel.AddToClassList("empty-object-list-label");
                    _objectListContainer.Add(noMatchLabel);
                }
                return;
            }

            for (int i = 0; i < _filteredObjects.Count; i++)
            {
                BuildObjectRow(_filteredObjects[i], i);
            }
        }

        private void BuildObjectRow(ScriptableObject dataObject, int index)
        {
            if (dataObject == null)
            {
                return;
            }

            string dataObjectName;
            if (dataObject is IDisplayable displayable)
            {
                dataObjectName = displayable.Title;
            }
            else
            {
                dataObjectName = dataObject.name;
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

            Button goUpButton = new(() =>
            {
                _objectListContainer.Remove(objectItemRow);
                _objectListContainer.Insert(1, objectItemRow);
                foreach (VisualElement child in _objectListContainer.Children())
                {
                    NamespaceController.RecalibrateVisualElements(child, offset: 1);
                }
            })
            {
                name = "go-up-button",
                text = "↑",
                tooltip = $"Move {dataObjectName} to top",
            };
            if (_selectedObjects.Count == 1 || index == 0)
            {
                goUpButton.AddToClassList("go-button-disabled");
            }
            else
            {
                goUpButton.AddToClassList(StyleConstants.ActionButtonClass);
                goUpButton.AddToClassList("go-button");
            }

            objectItemRow.Add(goUpButton);

            Button goDownButton = new(() =>
            {
                _objectListContainer.Remove(objectItemRow);
                _objectListContainer.Insert(_objectListContainer.childCount, objectItemRow);
                foreach (VisualElement child in _objectListContainer.Children())
                {
                    NamespaceController.RecalibrateVisualElements(child, offset: 1);
                }
            })
            {
                name = "go-down-button",
                text = "↓",
                tooltip = $"Move {dataObjectName} to bottom",
            };
            if (_selectedObjects.Count == 1 || index == _selectedObjects.Count - 1)
            {
                goDownButton.AddToClassList("go-button-disabled");
            }
            else
            {
                goDownButton.AddToClassList(StyleConstants.ActionButtonClass);
                goDownButton.AddToClassList("go-button");
            }

            objectItemRow.Add(goDownButton);

            VisualElement contentArea = new() { name = "content" };
            contentArea.AddToClassList(ObjectItemContentClass);
            contentArea.AddToClassList(StyleConstants.ClickableClass);
            objectItemRow.Add(contentArea);

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
            renameButton = new Button(() => OpenRenamePopover(titleLabel, renameButton, dataObject))
            {
                text = "@",
                tooltip = "Rename Object",
            };
            renameButton.AddToClassList(StyleConstants.ActionButtonClass);
            renameButton.AddToClassList("rename-button");
            actionsArea.Add(renameButton);

            Button moveButton = new(() =>
            {
                if (dataObject == null)
                {
                    return;
                }

                string assetPath = AssetDatabase.GetAssetPath(dataObject);
                string startDirectory = Path.GetDirectoryName(assetPath) ?? string.Empty;
                string selectedAbsolutePath = EditorUtility.OpenFolderPanel(
                    title: "Select New Location (Must be inside Assets)",
                    folder: startDirectory,
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
                if (
                    selectedAbsolutePath.Equals(
                        projectAssetsPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    relativePath = "Assets";
                }
                else
                {
                    relativePath =
                        "Assets" + selectedAbsolutePath.Substring(projectAssetsPath.Length);
                    relativePath = relativePath.Replace("//", "/");
                }

                string targetPath = $"{relativePath}/{dataObject.name}.asset";
                if (string.Equals(assetPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    // Ignore same path operation
                    return;
                }

                string errorMessage = AssetDatabase.MoveAsset(assetPath, targetPath);
                AssetDatabase.SaveAssets();
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    Debug.LogError(
                        $"Error moving asset {dataObject.name} from '{assetPath}' to '{targetPath}': {errorMessage}"
                    );
                    EditorUtility.DisplayDialog("Invalid Move Operation", errorMessage, "OK");
                }
            })
            {
                text = "➔",
                tooltip = "Move Object",
            };
            moveButton.AddToClassList(StyleConstants.ActionButtonClass);
            moveButton.AddToClassList("move-button");
            actionsArea.Add(moveButton);

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
                    _assetNameTextField = new TextField("Asset Name:")
                    {
                        value = _selectedObject.name,
                        isReadOnly = true,
                        name = "inspector-asset-name-field",
                    };

                    _assetNameTextField
                        .Q<TextInputBaseField<string>>(TextField.textInputUssName)
                        ?.SetEnabled(false);

                    _assetNameTextField.AddToClassList("readonly-display-field");
                    _inspectorContainer.Add(_assetNameTextField);

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

                _inspectorLabelsSection = new VisualElement
                {
                    name = "inspector-labels-section",
                    style = { marginTop = 5, marginBottom = 10 },
                };

                Label sectionHeader = new("Asset Labels:")
                {
                    style =
                    {
                        unityFontStyleAndWeight = FontStyle.Bold,
                        marginBottom = 4,
                        marginLeft = 6,
                    },
                };
                _inspectorLabelsSection.Add(sectionHeader);

                _inspectorCurrentLabelsContainer = new VisualElement
                {
                    name = "inspector-current-labels",
                    style = { marginRight = 6, marginLeft = 6 },
                };
                _inspectorCurrentLabelsContainer.AddToClassList("label-pill-container");
                _inspectorLabelsSection.Add(_inspectorCurrentLabelsContainer);

                VisualElement addLabelRow = new()
                {
                    style =
                    {
                        flexDirection = FlexDirection.Row,
                        alignItems = Align.Center,
                        position = Position.Relative,
                    },
                };
                _inspectorNewLabelInput = new TextField
                {
                    name = "inspector-new-label-input",
                    style =
                    {
                        flexGrow = 1,
                        flexShrink = 1,
                        marginTop = 6,
                        marginRight = 6,
                        marginLeft = 6,
                    },
                };
                _inspectorNewLabelInput.RegisterValueChangedCallback(evt =>
                    UpdateLabelSuggestions(evt.newValue)
                );
                _inspectorNewLabelInput.RegisterCallback<FocusInEvent>(_ => OnNewLabelInputFocus());
                _inspectorNewLabelInput.RegisterCallback<KeyDownEvent>(HandleNewLabelInputKeyDown);
                _inspectorNewLabelInput.RegisterCallback<FocusOutEvent>(OnNewLabelInputBlur);

                addLabelRow.Add(_inspectorNewLabelInput);
                Button addLabelButton = new(AddLabelToSelectedAsset) { text = "Add" };
                addLabelButton.AddToClassList(StyleConstants.ClickableClass);
                addLabelButton.AddToClassList("add-label-button");
                addLabelRow.Add(addLabelButton);
                _inspectorLabelsSection.Add(addLabelRow);

                _inspectorContainer.Add(_inspectorLabelsSection);
                PopulateInspectorLabelsUI();
            }

            // ReSharper disable once RedundantAssignment
            bool useOdinInspector = false;
#if ODIN_INSPECTOR
            Type objectType = _selectedObject.GetType();
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
                        _odinInspectorContainer = new IMGUIContainer(() => _odinPropertyTree?.Draw()
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
                    if (
                        _currentInspectorScriptableObject == null
                        || _currentInspectorScriptableObject.targetObject != _selectedObject
                    )
                    {
                        _currentInspectorScriptableObject?.Dispose();
                        _currentInspectorScriptableObject = new SerializedObject(_selectedObject);
                    }
                    else
                    {
                        _currentInspectorScriptableObject.UpdateIfRequiredOrScript();
                    }

                    InspectorElement inspectorElement = new(_selectedObject);
                    ListenForPropertyChange(inspectorElement);
                    _inspectorContainer.Add(inspectorElement);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error creating standard inspector with InspectorElement: {e}");
                    _inspectorContainer.Add(
                        new Label($"Standard Inspector Element Error: {e.Message}")
                    );
                }
            }

            VisualElement customElement = TryGetCustomVisualElement();
            if (customElement != null)
            {
                _inspectorContainer.Add(customElement);
            }
        }

        private VisualElement TryGetCustomVisualElement()
        {
            if (_selectedObject is IGUIProvider guiProvider)
            {
                return guiProvider.BuildGUI(
                    new DataVisualizerGUIContext(_currentInspectorScriptableObject)
                );
            }

            return null;
        }

        private void OnNewLabelInputFocus()
        {
            if (!_isLabelCachePopulated)
            {
                PopulateProjectUniqueLabelsCache();
            }
            if (!string.IsNullOrWhiteSpace(_inspectorNewLabelInput.value) && _isLabelCachePopulated)
            {
                UpdateLabelSuggestions(_inspectorNewLabelInput.value);
            }
        }

        private void OnNewLabelInputBlur(FocusOutEvent evt)
        {
            if (
                _inspectorLabelSuggestionsPopover != null
                && evt.relatedTarget is VisualElement focusedElement
            )
            {
                VisualElement current = focusedElement;
                while (current != null)
                {
                    if (current == _inspectorLabelSuggestionsPopover)
                    {
                        return;
                    }

                    current = current.parent;
                }
            }
            if (_activePopover == _inspectorLabelSuggestionsPopover)
            {
                rootVisualElement
                    .schedule.Execute(() =>
                    {
                        if (
                            _activePopover?.focusController?.focusedElement
                                != _inspectorNewLabelInput
                            && (
                                _activePopover?.focusController?.focusedElement as VisualElement
                            )?.FindCommonAncestor(_inspectorLabelSuggestionsPopover)
                                != _inspectorLabelSuggestionsPopover
                            && _activePopover == _inspectorLabelSuggestionsPopover
                        )
                        {
                            CloseActivePopover();
                        }
                    })
                    .ExecuteLater(100);
            }
        }

        private void PopulateProjectUniqueLabelsCache(bool force = false)
        {
            if (!force && _isLabelCachePopulated && _projectUniqueLabelsCache.Count > 0)
            {
                return;
            }

            _projectUniqueLabelsCache.Clear();
            SortedSet<string> allLabelsSet = new(StringComparer.Ordinal);
            foreach (ScriptableObject dataObject in _allManagedObjectsCache)
            {
                string[] labels = AssetDatabase.GetLabels(dataObject);
                foreach (string label in labels)
                {
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        allLabelsSet.Add(label.Trim());
                    }
                }
            }

            foreach (string label in allLabelsSet)
            {
                _projectUniqueLabelsCache.Add(label);
            }
            _isLabelCachePopulated = true;
        }

        private void UpdateLabelSuggestions(string currentInput)
        {
            if (_inspectorLabelSuggestionsPopover == null)
            {
                return;
            }

            if (!_isLabelCachePopulated)
            {
                PopulateProjectUniqueLabelsCache();
            }

            if (_activePopover == null && _projectUniqueLabelsCache.Count > 0)
            {
                OpenPopover(
                    _inspectorLabelSuggestionsPopover,
                    _inspectorNewLabelInput,
                    shouldFocus: false
                );
            }

            _currentLabelSuggestionItems.Clear();
            _labelSuggestionHighlightIndex = -1;
            _inspectorLabelSuggestionsPopover.Clear();

            string[] currentAssetLabelsArray =
                _selectedObject != null
                    ? AssetDatabase.GetLabels(_selectedObject)
                    : Array.Empty<string>();
            HashSet<string> currentAssetLabelsSet = new(
                currentAssetLabelsArray,
                StringComparer.Ordinal
            );

            List<string> suggestions = _projectUniqueLabelsCache
                .Where(label =>
                    string.IsNullOrWhiteSpace(currentInput)
                    || (
                        label.Contains(currentInput, StringComparison.OrdinalIgnoreCase)
                        && !currentAssetLabelsSet.Contains(label)
                    )
                )
                .Take(10)
                .ToList();

            if (suggestions.Count > 0)
            {
                foreach (string suggestionText in suggestions)
                {
                    Label suggestionItem = CreateHighlightedLabel(
                        suggestionText,
                        new List<string> { currentInput },
                        LabelSuggestionItemClass
                    );
                    suggestionItem.userData = suggestionText;
                    suggestionItem.RegisterCallback<PointerUpEvent>(evt =>
                    {
                        if (evt.button == 0 && suggestionItem.userData is string clickedSuggestion)
                        {
                            _inspectorNewLabelInput.SetValueWithoutNotify(clickedSuggestion);
                            _inspectorNewLabelInput.Focus();
                            AddLabelToSelectedAsset();
                            CloseActivePopover();
                            evt.StopPropagation();
                        }
                    });
                    suggestionItem.RegisterCallback<MouseEnterEvent>(evt =>
                        suggestionItem.style.backgroundColor = new Color(0.35f, 0.35f, 0.35f)
                    );
                    suggestionItem.RegisterCallback<MouseLeaveEvent>(evt =>
                        suggestionItem.style.backgroundColor = Color.clear
                    );
                    _inspectorLabelSuggestionsPopover.Add(suggestionItem);
                    _currentLabelSuggestionItems.Add(suggestionItem);
                }
            }
            else
            {
                if (_activePopover == _inspectorLabelSuggestionsPopover)
                {
                    CloseActivePopover();
                }
            }
        }

        private void HandleNewLabelInputKeyDown(KeyDownEvent evt)
        {
            if (
                _activePopover != _inspectorLabelSuggestionsPopover
                || _currentLabelSuggestionItems.Count == 0
            )
            {
                if (evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                {
                    AddLabelToSelectedAsset();
                    evt.PreventDefault();
                    evt.StopPropagation();
                }
                return;
            }

            bool highlightChanged = false;
            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                {
                    _labelSuggestionHighlightIndex++;
                    if (_labelSuggestionHighlightIndex >= _currentLabelSuggestionItems.Count)
                    {
                        _labelSuggestionHighlightIndex = 0;
                    }

                    highlightChanged = true;
                    break;
                }
                case KeyCode.UpArrow:
                {
                    _labelSuggestionHighlightIndex--;
                    if (_labelSuggestionHighlightIndex < 0)
                    {
                        _labelSuggestionHighlightIndex = _currentLabelSuggestionItems.Count - 1;
                    }

                    highlightChanged = true;
                    break;
                }
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                {
                    if (
                        _labelSuggestionHighlightIndex >= 0
                        && _labelSuggestionHighlightIndex < _currentLabelSuggestionItems.Count
                    )
                    {
                        if (
                            _currentLabelSuggestionItems[_labelSuggestionHighlightIndex].userData
                            is string selectedSuggestion
                        )
                        {
                            _inspectorNewLabelInput.SetValueWithoutNotify(selectedSuggestion);
                            AddLabelToSelectedAsset();
                            CloseActivePopover();
                        }
                    }
                    else
                    {
                        AddLabelToSelectedAsset();
                        CloseActivePopover();
                    }

                    evt.PreventDefault();
                    evt.StopPropagation();
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
                UpdateLabelSuggestionHighlight();
                evt.PreventDefault();
                evt.StopPropagation();
            }
        }

        private void UpdateLabelSuggestionHighlight()
        {
            if (_currentLabelSuggestionItems == null || _inspectorLabelSuggestionsPopover == null)
            {
                return;
            }

            ScrollView scrollView = _inspectorLabelSuggestionsPopover.Q<ScrollView>();

            for (int i = 0; i < _currentLabelSuggestionItems.Count; i++)
            {
                VisualElement item = _currentLabelSuggestionItems[i];
                if (item == null)
                {
                    continue;
                }

                bool shouldHighlight = i == _labelSuggestionHighlightIndex;
                item.EnableInClassList(PopoverHighlightClass, shouldHighlight);
                if (shouldHighlight && scrollView != null)
                {
                    item.schedule.Execute(() =>
                        {
                            if (item.panel != null)
                            {
                                scrollView.ScrollTo(item);
                            }
                        })
                        .ExecuteLater(1);
                }
            }
        }

        private void PopulateInspectorLabelsUI()
        {
            if (_inspectorCurrentLabelsContainer == null || _selectedObject == null)
            {
                _inspectorCurrentLabelsContainer?.Clear();
                return;
            }
            _inspectorCurrentLabelsContainer.Clear();

            string[] currentLabels = AssetDatabase.GetLabels(_selectedObject);
            Array.Sort(currentLabels);

            if (currentLabels.Length == 0)
            {
                _inspectorCurrentLabelsContainer.Add(
                    new Label("No labels assigned.")
                    {
                        style =
                        {
                            color = Color.gray,
                            fontSize = 10,
                            unityFontStyleAndWeight = FontStyle.Italic,
                        },
                    }
                );
                return;
            }

            foreach (string labelText in currentLabels)
            {
                Color backgroundColor = GetColorForLabel(labelText);
                VisualElement pillContainer = new()
                {
                    name = $"inspector-label-pill-{labelText.Replace(" ", "-").ToLowerInvariant()}",
                    style = { backgroundColor = backgroundColor },
                };
                pillContainer.AddToClassList("label-pill");

                Label labelElement = new(labelText)
                {
                    style = { color = IsColorDark(backgroundColor) ? Color.white : Color.black },
                };
                labelElement.AddToClassList("label-pill-text");
                pillContainer.Add(labelElement);

                Button removeButton = new(() => RemoveLabelFromSelectedAsset(labelText))
                {
                    text = "x",
                    tooltip = $"Remove label '{labelText}'",
                };
                removeButton.AddToClassList(StyleConstants.ClickableClass);
                removeButton.AddToClassList("label-pill-remove-button");
                removeButton.style.color = labelElement.style.color.value;
                pillContainer.Add(removeButton);
                _inspectorCurrentLabelsContainer.Add(pillContainer);
            }
        }

        private void AddLabelToSelectedAsset()
        {
            if (_selectedObject == null || _inspectorNewLabelInput == null)
            {
                return;
            }

            string newLabelText = _inspectorNewLabelInput.value?.Trim();
            if (string.IsNullOrWhiteSpace(newLabelText))
            {
                _inspectorNewLabelInput.SetValueWithoutNotify("");
                return;
            }

            string[] currentLabels = AssetDatabase.GetLabels(_selectedObject);
            if (
                Array.Exists(
                    currentLabels,
                    label => label.Equals(newLabelText, StringComparison.Ordinal)
                )
            )
            {
                _inspectorNewLabelInput.SetValueWithoutNotify("");
                _inspectorNewLabelInput.Focus();
                return;
            }

            List<string> updatedLabels = currentLabels.ToList();
            updatedLabels.Add(newLabelText);

            try
            {
                AssetDatabase.SetLabels(_selectedObject, updatedLabels.ToArray());
                EditorUtility.SetDirty(_selectedObject);
                AssetDatabase.SaveAssets();
                PopulateProjectUniqueLabelsCache();
                _inspectorNewLabelInput.SetValueWithoutNotify("");
                PopulateInspectorLabelsUI();
                UpdateLabelAreaAndFilter();
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"Error adding label '{newLabelText}' to asset '{_selectedObject.name}': {ex}"
                );
            }
            _inspectorNewLabelInput.Focus();
        }

        private void RemoveLabelFromSelectedAsset(string labelToRemove)
        {
            if (_selectedObject == null || string.IsNullOrWhiteSpace(labelToRemove))
            {
                return;
            }

            string[] currentLabels = AssetDatabase.GetLabels(_selectedObject);
            List<string> updatedLabels = currentLabels
                .Where(label => !label.Equals(labelToRemove, StringComparison.Ordinal))
                .ToList();

            if (updatedLabels.Count < currentLabels.Length)
            {
                try
                {
                    AssetDatabase.SetLabels(_selectedObject, updatedLabels.ToArray());
                    EditorUtility.SetDirty(_selectedObject);
                    AssetDatabase.SaveAssets();
                    PopulateProjectUniqueLabelsCache();
                    PopulateInspectorLabelsUI();
                    UpdateLabelAreaAndFilter();
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"Error removing label '{labelToRemove}' from asset '{_selectedObject.name}': {ex}"
                    );
                }
            }
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
                if (cloneInstance is IDuplicable duplicable)
                {
                    duplicable.BeforeClone(originalObject);
                }
                AssetDatabase.CreateAsset(cloneInstance, uniquePath);
                AssetDatabase.SaveAssets();

                ScriptableObject cloneAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                    uniquePath
                );
                if (cloneAsset != null)
                {
                    if (cloneAsset is IDuplicable cloneDataObject)
                    {
                        cloneDataObject.AfterClone(originalObject);
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
            string savedObjectGuid = GetLastSelectedObjectGuidForType(selectedType.FullName);
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

        private static void UpdateObjectTitleRepresentation(
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
            if (dataObject is IDisplayable displayable)
            {
                currentTitle = displayable.Title;
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
                .Where(type =>
                    managedTypeFullNames.Contains(type.FullName)
                    || !NamespaceController.IsTypeRemovable(type)
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
                .Where(IsLoadableType)
                .ToList();
        }

        internal static bool IsLoadableType(Type type)
        {
            bool allowed =
                type != typeof(ScriptableObject)
                && !type.IsAbstract
                && !type.IsGenericType
                && !IsSubclassOf(type, typeof(Editor))
                && !IsSubclassOf(type, typeof(EditorWindow))
                && !IsSubclassOf(type, typeof(ScriptableSingleton<>))
                && type.Namespace?.StartsWith("UnityEditor", StringComparison.Ordinal) != true
                && type.Namespace?.StartsWith("UnityEngine", StringComparison.Ordinal) != true;
            if (!allowed)
            {
                return false;
            }

            ScriptableObject instance = CreateInstance(type);
            try
            {
                using SerializedObject serializedObject = new(instance);
                using SerializedProperty scriptProperty = serializedObject.FindProperty("m_Script");
                if (scriptProperty == null)
                {
                    return false;
                }

                return scriptProperty.objectReferenceValue != null;
            }
            finally
            {
                if (instance != null)
                {
                    DestroyImmediate(instance);
                }
            }
        }

        private static bool IsSubclassOf(Type typeToCheck, Type baseClass)
        {
            if (typeToCheck == null)
            {
                return false;
            }

            Type currentType = typeToCheck;

            while (currentType != null && currentType != typeof(object))
            {
                Type typeToCheckAgainst = currentType.IsGenericType
                    ? currentType.GetGenericTypeDefinition()
                    : currentType;
                if (typeToCheckAgainst == baseClass)
                {
                    return true;
                }
                currentType = currentType.BaseType;
            }
            return false;
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

                if (Settings.selectActiveObject)
                {
                    Selection.activeObject = _selectedObject;
                }

                Rect targetElementWorldBound = newSelectedElement.worldBound;
                Rect scrollViewContentViewportWorldBound = _objectScrollView
                    .contentViewport
                    .worldBound;
                bool isElementInView = targetElementWorldBound.Overlaps(
                    scrollViewContentViewportWorldBound
                );

                if (!isElementInView)
                {
                    _objectScrollView
                        .schedule.Execute(() =>
                        {
                            _objectScrollView?.ScrollTo(_selectedElement);
                        })
                        .ExecuteLater(1);
                }
            }

            try
            {
                if (_selectedObject != null)
                {
                    string typeName = _selectedObject.GetType().FullName;
                    string assetPath = AssetDatabase.GetAssetPath(_selectedObject);
                    string objectGuid = null;
                    if (!string.IsNullOrWhiteSpace(assetPath))
                    {
                        objectGuid = AssetDatabase.AssetPathToGUID(assetPath);
                    }

                    SetLastSelectedObjectGuidForType(typeName, objectGuid);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error saving selection state. {e}");
            }

            _currentInspectorScriptableObject?.Dispose();
            _currentInspectorScriptableObject =
                dataObject != null ? new SerializedObject(dataObject) : null;

            if (dataObject != null)
            {
                _namespaceController.SelectType(this, dataObject.GetType());
                rootVisualElement
                    .schedule.Execute(() =>
                    {
                        if (_selectedObject == dataObject)
                        {
                            _namespaceController.SelectType(this, dataObject.GetType());
                        }
                    })
                    .ExecuteLater(1);
            }
            // Backup trigger, we have some delay issues
            BuildInspectorView();
        }

        internal void UpdateCreateObjectButtonStyle()
        {
            if (_createObjectButton != null)
            {
                _createObjectButton.style.display =
                    _namespaceController.SelectedType != null
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
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
                _lastActiveFocusArea = FocusArea.None;
                _draggedElement = targetElement;
                _draggedData = clickedObject;
                _activeDragType = DragType.Object;
                _isDragging = false;
                targetElement.CapturePointer(evt.pointerId);
                targetElement.Focus();
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

            if (!_isDragging)
            {
                _isDragging = true;
                string dragText = _draggedData switch
                {
                    IDisplayable displayable => displayable.Title,
                    Object dataObj => dataObj.name,
                    string nsKey => nsKey,
                    Type type => NamespaceController.GetTypeDisplayName(type),
                    _ => "Dragging Item",
                };
                StartDragVisuals(evt.position, dragText);
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
            foreach (VisualElement child in _namespaceListContainer.Children())
            {
                NamespaceController.RecalibrateVisualElements(child);
            }

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

            if (targetIndex < 0)
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
            foreach (VisualElement child in typesContainer.Children())
            {
                NamespaceController.RecalibrateVisualElements(child);
            }

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
            List<string> newTypeNameOrder = orderedTypes.Select(t => t.FullName).ToList();
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
            foreach (VisualElement child in _objectListContainer.Children())
            {
                NamespaceController.RecalibrateVisualElements(child, offset: 1);
            }

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
            _dragGhost.style.left = currentPosition.x - _dragGhost.resolvedStyle.width / 2;
            _dragGhost.style.top = currentPosition.y - _dragGhost.resolvedStyle.height;
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
            _draggedElement.style.display = DisplayStyle.None;
            _draggedElement.style.opacity = 0.5f;
        }

        private void UpdateInPlaceGhostPosition(Vector2 pointerPosition)
        {
            VisualElement container = null;
            switch (_activeDragType)
            {
                case DragType.Object:
                {
                    container = _objectListContainer.contentContainer;
                    break;
                }
                case DragType.Namespace:
                {
                    container = _namespaceListContainer;
                    break;
                }
                case DragType.Type:
                {
                    container = _draggedElement?.parent;
                    break;
                }
            }

            if (container == null || _draggedElement == null || _inPlaceGhost == null)
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
            int targetIndex;
            Vector2 localPointerPos = container.WorldToLocal(pointerPosition);

            if (_activeDragType != DragType.Namespace)
            {
                targetIndex = -1;
                for (int i = 0; i < childCount; ++i)
                {
                    VisualElement child = container.ElementAt(i);
                    float midpoint = child.layout.yMin + child.layout.height / 2f;
                    if (localPointerPos.y < midpoint)
                    {
                        targetIndex = i;
                        break;
                    }
                }

                if (targetIndex < 0)
                {
                    targetIndex = childCount;
                }
            }
            else
            {
                targetIndex = 0;
                if (0 <= localPointerPos.y)
                {
                    bool seenInPlaceGhost = false;
                    for (int i = 0; i < childCount; ++i)
                    {
                        VisualElement child = container.ElementAt(i);
                        if (child == _inPlaceGhost)
                        {
                            seenInPlaceGhost = true;
                        }
                        float yMax = child.layout.yMax;
                        if (localPointerPos.y < yMax)
                        {
                            if (seenInPlaceGhost)
                            {
                                targetIndex = i;
                            }
                            else
                            {
                                targetIndex = i + 1;
                            }

                            break;
                        }
                    }
                }
            }
            targetIndex = Mathf.Max(0, targetIndex);
            targetIndex = Mathf.Min(targetIndex, childCount);

            bool targetIndexValid = true;
            int maxIndex = childCount;

            if (_inPlaceGhost.parent == container)
            {
                maxIndex--;
            }

            maxIndex = Math.Max(0, maxIndex);
            targetIndex = Mathf.Clamp(targetIndex, 0, maxIndex + 1);

            if (targetIndex != _lastGhostInsertIndex || container != _lastGhostParent)
            {
                if (_inPlaceGhost.parent != null && _inPlaceGhost.parent != container)
                {
                    _inPlaceGhost.RemoveFromHierarchy();
                    container.Add(_inPlaceGhost);
                }
                else if (0 <= targetIndex && targetIndex <= container.childCount)
                {
                    _inPlaceGhost.RemoveFromHierarchy();
                    if (container.childCount < targetIndex)
                    {
                        container.Add(_inPlaceGhost);
                    }
                    else
                    {
                        container.Insert(targetIndex, _inPlaceGhost);
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
                _lastGhostParent = container;
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

        private string GetLastSelectedTypeName()
        {
            DataVisualizerSettings settings = Settings;
            return settings.persistStateInSettingsAsset
                ? settings.lastSelectedTypeName
                : UserState.lastSelectedTypeName;
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
