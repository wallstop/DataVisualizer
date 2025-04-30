namespace WallstopStudios.Editor.DataVisualizer
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Components;
    using Data;
    using Extensions;
    using Helper;
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
        private const string NamespaceIndicatorClass = "namespace-indicator";
        private const string NamespaceLabelClass = "namespace-item__label";
        private const string TypeItemClass = "type-item";
        private const string TypeLabelClass = "type-item__label";
        private const string ObjectItemClass = "object-item";
        private const string ObjectItemContentClass = "object-item-content";
        private const string ObjectItemActionsClass = "object-item-actions";
        private const string ActionButtonClass = "action-button";
        private const string ListNamespaceClassName = "type-selection-list-namespace";
        private const string ListItemClassName = "type-selection-list-item";
        private const string ListItemDisabledClassName = "type-selection-list-item--disabled";
        private const string PopoverListItemClassName = "type-selection-list-item";
        private const string PopoverListItemDisabledClassName =
            "type-selection-list-item--disabled";
        private const string PopoverListNamespaceClassName = "type-selection-list-namespace";
        private const string PopoverNamespaceHeaderClassName = "popover-namespace-header"; // For the header row
        private const string PopoverNamespaceIndicatorClassName = "popover-namespace-indicator"; // For the arrow

        private const string ArrowCollapsed = "►";
        private const string ArrowExpanded = "▼";

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

        private readonly List<(string key, List<Type> types)> _scriptableObjectTypes = new();
        private readonly Dictionary<ScriptableObject, VisualElement> _objectVisualElementMap =
            new();
        private readonly List<ScriptableObject> _selectedObjects = new();
        private ScriptableObject _selectedObject;
        private VisualElement _selectedElement;

        private VisualElement _namespaceListContainer;
        private VisualElement _objectListContainer;
        private VisualElement _inspectorContainer;
        private ScrollView _objectScrollView;
        private ScrollView _inspectorScrollView;

        private TwoPaneSplitView _outerSplitView;
        private TwoPaneSplitView _innerSplitView;
        private VisualElement _namespaceColumnElement;
        private VisualElement _objectColumnElement;

        private VisualElement _typeAddPopover; // Changed from _typeAddPopup
        private Button _addTypeButton; // Need reference to the button for positioning
        private TextField _typeSearchField; // <-- ADD Search Field Reference
        private VisualElement _typePopoverListContainer; // <-- ADD Container reference
        private bool _isTypePopoverOpen = false; // Track state

        private float _lastSavedOuterWidth = -1f;
        private float _lastSavedInnerWidth = -1f;
        private IVisualElementScheduledItem _saveWidthsTask;

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

        //private VisualElement _settingsPopup;
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
            rootVisualElement
                .schedule.Execute(() =>
                {
                    RestorePreviousSelection();
                    StartPeriodicWidthSave();
                })
                .ExecuteLater(10);
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            Cleanup();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            CloseAddTypePopover();
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

            Button settingsButton = new(() =>
            {
                if (_settings == null)
                {
                    _settings = LoadOrCreateSettings();
                }

                bool wasSettingsAssetMode = _settings.persistStateInSettingsAsset;
                DataVisualizerSettingsPopup popupWindow =
                    DataVisualizerSettingsPopup.CreateAndConfigureInstance(
                        _settings,
                        () => HandleSettingsPopupClosed(wasSettingsAssetMode)
                    );
                popupWindow.ShowModal();
            })
            {
                text = "…",
                name = "settings-button",
            };
            settingsButton.AddToClassList("icon-button");
            settingsButton.AddToClassList("clickable");
            headerRow.Add(settingsButton);

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

            _typeAddPopover = new VisualElement
            {
                name = "type-add-popover",
                style =
                {
                    position = Position.Absolute,
                    width = 250, // Or auto? Let's try fixed first
                    maxHeight = 400, // Prevent excessive height
                    backgroundColor = new Color(0.22f, 0.22f, 0.22f, 1f), // Slightly different bg
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderBottomColor = Color.black,
                    borderLeftColor = Color.black,
                    borderRightColor = Color.black,
                    borderTopColor = Color.black,
                    display = DisplayStyle.None, // Start hidden
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
            _typeSearchField.RegisterValueChangedCallback(evt => BuildTypeAddList(evt.newValue)); // Filter on change
            _typeAddPopover.Add(_typeSearchField);

            var typePopoverScrollView = new ScrollView(ScrollViewMode.Vertical)
            {
                style = { flexGrow = 1 },
            };
            _typeAddPopover.Add(typePopoverScrollView);
            // Add content container for list items inside scrollview
            _typePopoverListContainer = new VisualElement { name = "type-add-list-content" };
            typePopoverScrollView.Add(_typePopoverListContainer);

            root.Add(_typeAddPopover);

            // _settingsPopup = new VisualElement
            // {
            //     name = "settings-popup",
            //     style =
            //     {
            //         position = Position.Absolute,
            //         top = 30,
            //         left = 10,
            //         width = 350,
            //         backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.95f),
            //         borderLeftWidth = 1,
            //         borderRightWidth = 1,
            //         borderTopWidth = 1,
            //         borderBottomWidth = 1,
            //         borderBottomColor = Color.gray,
            //         borderLeftColor = Color.gray,
            //         borderRightColor = Color.gray,
            //         borderTopColor = Color.gray,
            //         borderBottomLeftRadius = 5,
            //         borderBottomRightRadius = 5,
            //         borderTopLeftRadius = 5,
            //         borderTopRightRadius = 5,
            //         paddingBottom = 10,
            //         paddingLeft = 10,
            //         paddingRight = 10,
            //         paddingTop = 10,
            //         display = DisplayStyle.None,
            //     },
            // };
            // root.Add(_settingsPopup);
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

        private void HandleSettingsPopupClosed(bool previousModeWasSettingsAsset)
        {
            if (_settings == null)
            {
                _settings = LoadOrCreateSettings();
            }

            if (_settings != null && EditorUtility.IsDirty(_settings))
            {
                AssetDatabase.SaveAssets();
            }

            bool currentModeIsSettingsAsset = _settings.persistStateInSettingsAsset;
            bool migrationNeeded = previousModeWasSettingsAsset != currentModeIsSettingsAsset;

            if (migrationNeeded)
            {
                MigratePersistenceState(migrateToSettingsAsset: currentModeIsSettingsAsset);
                if (currentModeIsSettingsAsset)
                {
                    if (EditorUtility.IsDirty(_settings))
                    {
                        AssetDatabase.SaveAssets();
                    }
                }
                else
                {
                    SaveUserStateToFile();
                }
            }

            ScheduleRefresh();
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

            var nsHeader = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.SpaceBetween,
                    alignItems = Align.Center,
                    paddingBottom = 3,
                    paddingLeft = 3,
                    paddingRight = 3,
                    paddingTop = 3,
                    borderBottomWidth = 1,
                    borderBottomColor = Color.gray,
                    height = 24,
                    flexShrink = 0,
                },
            };
            nsHeader.Add(
                new Label("Namespaces")
                {
                    style = { unityFontStyleAndWeight = FontStyle.Bold, paddingLeft = 2 },
                }
            );
            // Add Type Button
            _addTypeButton = new Button(ToggleAddTypePopover)
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

        private void ToggleAddTypePopover()
        {
            if (_isTypePopoverOpen)
            {
                CloseAddTypePopover();
            }
            else
            {
                OpenAddTypePopover();
            }
        }

        private void OpenAddTypePopover()
        {
            if (
                _isTypePopoverOpen
                || _typeAddPopover == null
                || _addTypeButton == null
                || _settings == null
            )
                return;

            BuildTypeAddList();

            // 5. Position the Popover below the Add button
            // Use geometry only available after layout pass - schedule positioning
            _addTypeButton
                .schedule.Execute(() =>
                {
                    if (_addTypeButton == null || _typeAddPopover == null)
                        return; // Check if elements still exist

                    // Convert button's bottom-left corner to the root's coordinate space
                    Vector2 buttonPos = _addTypeButton.worldBound.position;
                    Vector2 buttonSize = _addTypeButton.worldBound.size;
                    // Position relative to rootVisualElement which is the popover's parent
                    Vector2 localPos = rootVisualElement.WorldToLocal(buttonPos);

                    _typeAddPopover.style.left = localPos.x;
                    _typeAddPopover.style.top = localPos.y + buttonSize.y + 2; // Position below button + small gap

                    // Optional: Ensure it doesn't go off-screen (basic clamp)
                    float maxWidth = rootVisualElement.resolvedStyle.width - localPos.x - 10;
                    _typeAddPopover.style.maxWidth = Mathf.Max(100, maxWidth); // Ensure min width
                    float maxHeight =
                        rootVisualElement.resolvedStyle.height
                        - (localPos.y + buttonSize.y + 2)
                        - 10;
                    _typeAddPopover.style.maxHeight = Mathf.Max(100, maxHeight);

                    // 6. Show the Popover
                    _typeAddPopover.style.display = DisplayStyle.Flex;
                    _isTypePopoverOpen = true;

                    // Debug.Log("Opened Type Popover");

                    // 7. Register Capture handler to detect clicks outside
                    // Use TrickleDown to catch event early, Capture to ensure this element gets it.
                    rootVisualElement.RegisterCallback<PointerDownEvent>(
                        HandleClickOutsidePopover,
                        TrickleDown.TrickleDown
                    );
                })
                .ExecuteLater(1); // Delay slightly for button layout

            _typeAddPopover.style.display = DisplayStyle.Flex;
            _isTypePopoverOpen = true;
            // 7. Register Capture handler to detect clicks outside
            rootVisualElement.RegisterCallback<PointerDownEvent>(
                HandleClickOutsidePopover,
                TrickleDown.TrickleDown
            );
        }

        private void BuildTypeAddList(string filter = null)
        {
            if (string.Equals("Search...", filter, StringComparison.Ordinal))
            {
                filter = null;
            }
            if (_typePopoverListContainer == null)
            {
                return;
            }

            _typePopoverListContainer.Clear(); // Clear previous items

            List<string> searchTerms = string.IsNullOrWhiteSpace(filter)
                ? new List<string>()
                : filter.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            bool isFiltering = searchTerms.Count > 0;

            List<Type> allObjectTypes = LoadRelevantScriptableObjectTypes();
            List<string> managedTypeFullNames = GetManagedTypeNames();
            HashSet<string> managedSet = new(managedTypeFullNames);

            // 3. Group Types by Namespace
            var groupedTypes = allObjectTypes
                .GroupBy(GetNamespaceKey) // Use static helper
                .OrderBy(grouping => grouping.Key);

            // 4. Iterate Groups and Build UI
            bool foundMatches = false;
            foreach (IGrouping<string, Type> group in groupedTypes)
            {
                string namespaceKey = group.Key;
                List<Type> addableTypes = new List<Type>();
                var typesToShowInGroup = new List<VisualElement>(); // VEs for types passing filter

                // Check if namespace itself matches filter terms (for potential highlighting/expansion)
                bool namespaceMatchesAll =
                    isFiltering
                    && searchTerms.All(term =>
                        namespaceKey.Contains(term, StringComparison.OrdinalIgnoreCase)
                    );

                // Filter types within the group
                foreach (Type type in group.OrderBy(t => t.Name))
                {
                    string typeName = type.Name;
                    bool typeMatchesSearch =
                        !isFiltering
                        || searchTerms.All(term =>
                            typeName.Contains(term, StringComparison.OrdinalIgnoreCase)
                            || namespaceKey.Contains(term, StringComparison.OrdinalIgnoreCase) // Term must be in Type OR Namespace
                        );

                    if (typeMatchesSearch)
                    {
                        bool isManaged = managedSet.Contains(type.FullName);
                        var typeLabel = new Label($"  {type.Name}");
                        typeLabel.style.paddingTop = 1;
                        typeLabel.style.paddingBottom = 1;
                        typeLabel.style.marginLeft = 10;
                        typeLabel.AddToClassList(PopoverListItemClassName); // Apply base style

                        if (isManaged)
                        {
                            typeLabel.SetEnabled(false);
                            typeLabel.AddToClassList(PopoverListItemDisabledClassName);
                        }
                        else
                        {
                            // Add click handler for individual type selection
                            typeLabel.RegisterCallback<PointerDownEvent, Type>(
                                HandleTypeSelectionFromPopover,
                                type
                            );
                            typeLabel.RegisterCallback<MouseEnterEvent>(evt =>
                                typeLabel.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f)
                            );
                            typeLabel.RegisterCallback<MouseLeaveEvent>(evt =>
                                typeLabel.style.backgroundColor = Color.clear
                            );
                        }

                        addableTypes.Add(type);
                        typesToShowInGroup.Add(typeLabel); // Add VE to list for this group
                    }
                } // End foreach Type

                // 5. Add Namespace Group to UI if it's relevant
                if (typesToShowInGroup.Count > 0) // Only add group if it contains visible types
                {
                    foundMatches = true; // Mark that we found something

                    // --- Create Namespace Group Elements ---
                    var namespaceGroupContainer = new VisualElement()
                    {
                        name = $"ns-group-container-{group.Key}",
                    };
                    var header = new VisualElement { name = $"ns-header-{group.Key}" };
                    header.AddToClassList(PopoverNamespaceHeaderClassName); // Use specific class

                    // Force expand if filtering is active, otherwise default collapsed
                    bool startCollapsed = !isFiltering;
                    var indicator = new Label(startCollapsed ? ArrowCollapsed : ArrowExpanded)
                    {
                        name = $"ns-indicator-{group.Key}",
                    };
                    indicator.AddToClassList(PopoverNamespaceIndicatorClassName);
                    indicator.AddToClassList("clickable");

                    var nsLabel = new Label(group.Key) { name = $"ns-name-{group.Key}" };
                    nsLabel.AddToClassList(PopoverListNamespaceClassName); // Reuse existing class for style
                    nsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                    nsLabel.style.flexGrow = 1;

                    var clickContext = new
                    {
                        NamespaceKey = group.Key,
                        AddableTypes = addableTypes,
                    };

                    if (typesToShowInGroup.Count > 1)
                    {
                        nsLabel.AddToClassList("type-selection-list-namespace--not-empty"); // Apply base style
                        nsLabel.AddToClassList("clickable"); // Add a class for potential hover styles
                        nsLabel.RegisterCallback<MouseEnterEvent>(evt =>
                            nsLabel.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f)
                        );
                        nsLabel.RegisterCallback<MouseLeaveEvent>(evt =>
                            nsLabel.style.backgroundColor = Color.clear
                        );

                        nsLabel.RegisterCallback<PointerDownEvent>(evt =>
                        {
                            if (evt.button == 0) // Left click
                            {
                                // Access context captured by the lambda
                                string clickedNamespace = clickContext.NamespaceKey;
                                List<Type> typesToAdd = clickContext.AddableTypes;
                                int countToAdd = typesToAdd.Count;

                                if (countToAdd == 0)
                                    return; // Nothing to add

                                // --- Confirmation Dialog ---
                                string message =
                                    $"Add {countToAdd} type{(countToAdd > 1 ? "s" : "")} from namespace '{clickedNamespace}' to Data Visualizer?";
                                var confirmPopup = ConfirmActionPopup.CreateAndConfigureInstance(
                                    title: "Add Namespace Types",
                                    message: message,
                                    confirmButtonText: "Add",
                                    cancelButtonText: "Cancel",
                                    parentPosition: this.position, // Center on DataVisualizer
                                    onComplete: (confirmed) =>
                                    {
                                        if (confirmed)
                                        {
                                            bool stateChanged = false;
                                            List<string> currentManagedList = GetManagedTypeNames(); // Get list ref
                                            foreach (Type typeToAdd in typesToAdd)
                                            {
                                                // Double check it wasn't added somehow while dialog was open
                                                if (
                                                    !currentManagedList.Contains(typeToAdd.FullName)
                                                )
                                                {
                                                    currentManagedList.Add(typeToAdd.FullName);
                                                    stateChanged = true;
                                                }
                                            }

                                            if (stateChanged)
                                            {
                                                // Mark appropriate backend dirty and save state
                                                if (_settings.persistStateInSettingsAsset)
                                                {
                                                    MarkSettingsDirty();
                                                    AssetDatabase.SaveAssets();
                                                }
                                                else
                                                {
                                                    MarkUserStateDirty();
                                                } // Triggers file save

                                                Debug.Log(
                                                    $"Added {countToAdd} types from namespace '{clickedNamespace}'."
                                                );

                                                // Close the type add popover first
                                                CloseAddTypePopover();
                                                // Refresh the main window UI completely
                                                ScheduleRefresh(); // Use full refresh to ensure everything updates
                                            }
                                            else
                                            {
                                                // Close popover even if nothing ended up being added
                                                CloseAddTypePopover();
                                            }
                                        }
                                        // If not confirmed, do nothing, popup closes automatically via callback
                                    }
                                ); // End CreateAndConfigureInstance
                                confirmPopup.ShowModal(); // Show confirmation modally
                                // --- End Confirmation ---

                                evt.StopPropagation(); // Stop this click from closing the popover immediately
                            }
                        }); // End Namespace Label Click Handler
                    }
                    else
                    {
                        nsLabel.AddToClassList("type-selection-list-namespace--empty"); // Apply base style
                    }

                    header.Add(indicator);
                    header.Add(nsLabel);

                    var typesSubContainer = new VisualElement()
                    {
                        name = $"types-subcontainer-{group.Key}",
                    };
                    typesSubContainer.style.marginLeft = 15; // Indent types
                    // Set initial display based on filtering
                    typesSubContainer.style.display = startCollapsed
                        ? DisplayStyle.None
                        : DisplayStyle.Flex;

                    // Add the filtered type VEs to the sub-container
                    foreach (var typeVE in typesToShowInGroup)
                    {
                        typesSubContainer.Add(typeVE);
                    }

                    // Add header and sub-container to the group container
                    namespaceGroupContainer.Add(header);
                    namespaceGroupContainer.Add(typesSubContainer);

                    // Add toggle handler to header
                    indicator.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button == 0)
                        {
                            var currentIndicator = header.Q<Label>(
                                className: PopoverNamespaceIndicatorClassName
                            );
                            var currentTypesContainer = header.parent.Q<VisualElement>(
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
                            evt.StopPropagation(); // Prevent click outside handler closing popover
                        }
                    });

                    // Add the whole group to the main list container in the popover
                    _typePopoverListContainer.Add(namespaceGroupContainer);
                } // End if (typesToShowInGroup.Count > 0)
            } // End foreach namespace group

            // Optional: Display "No results" message if filtering yields nothing
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

        // Handler attached to ROOT to close popover on outside click
        private void HandleClickOutsidePopover(PointerDownEvent evt)
        {
            if (!_isTypePopoverOpen || _typeAddPopover == null)
                return;

            // Check if the click target is the popover itself or one of its children
            VisualElement target = evt.target as VisualElement;
            bool clickInside = false;
            while (target != null)
            {
                if (target == _typeAddPopover || target == _addTypeButton)
                { // Allow clicking button again to close
                    clickInside = true;
                    break;
                }
                target = target.parent;
            }

            // If the click was outside, close the popover
            if (!clickInside)
            {
                // Debug.Log("Clicked outside popover.");
                CloseAddTypePopover();
                // Important: Stop propagation so the outside click doesn't trigger something else unintentionally
                evt.StopPropagation();
            }
            // If click was inside, do nothing here, let the item click handler work.
        }

        private void HandleTypeSelectionFromPopover(PointerDownEvent evt, Type selectedType)
        {
            if (selectedType != null)
            {
                Debug.Log($"User selected type to add: {selectedType.FullName}");
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
                    } // Triggers file save

                    // Refresh UI
                    LoadScriptableObjectTypes();
                    BuildNamespaceView();
                }
            }
            CloseAddTypePopover(); // Close after selection
            evt.StopPropagation(); // Consume the click
        }

        private void CloseAddTypePopover()
        {
            if (!_isTypePopoverOpen || _typeAddPopover == null)
                return;

            _typeAddPopover.style.display = DisplayStyle.None; // Hide it
            _isTypePopoverOpen = false;
            // Unregister the global click handler
            rootVisualElement.UnregisterCallback<PointerDownEvent>(
                HandleClickOutsidePopover,
                TrickleDown.TrickleDown
            );
            // Debug.Log("Closed Type Popover");
        }

        private VisualElement CreateObjectColumn()
        {
            VisualElement objectColumn = new()
            {
                name = "object-column",
                style =
                {
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

        private void BuildNamespaceView()
        {
            if (_namespaceListContainer == null)
            {
                return;
            }

            _namespaceListContainer.Clear();

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
                ;
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

                foreach (Type type in types)
                {
                    VisualElement typeItem = new()
                    {
                        name = $"type-item-{type.Name}",
                        userData = type,
                        pickingMode = PickingMode.Position,
                        focusable = true,
                    };

                    typeItem.AddToClassList(TypeItemClass);
                    Label typeLabel = new(type.Name) { name = "type-item-label" };
                    typeLabel.AddToClassList(TypeLabelClass);
                    typeLabel.AddToClassList("clickable");
                    typeItem.Add(typeLabel);

                    typeItem.RegisterCallback<PointerDownEvent>(OnTypePointerDown);
                    // ReSharper disable once HeapView.CanAvoidClosure
                    typeItem.RegisterCallback<PointerUpEvent>(evt =>
                    {
                        if (_isDragging || evt.button != 0)
                        {
                            return;
                        }

                        if (typeItem.userData is not Type clickedType)
                        {
                            return;
                        }

                        _selectedTypeElement?.parent?.parent?.RemoveFromClassList("selected");
                        _selectedTypeElement?.RemoveFromClassList("selected");
                        _selectedType = clickedType;
                        _selectedTypeElement = typeItem;
                        _selectedTypeElement.AddToClassList("selected");
                        _selectedTypeElement.parent?.parent?.AddToClassList("selected");
                        SaveNamespaceAndTypeSelectionState(
                            GetNamespaceKey(_selectedType),
                            _selectedType.Name
                        );

                        LoadObjectTypes(clickedType);
                        ScriptableObject objectToSelect = DetermineObjectToAutoSelect();
                        BuildObjectsView();
                        SelectObject(objectToSelect);
                        evt.StopPropagation();
                    });
                    typesContainer.Add(typeItem);
                }
            }
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

                string title;
                if (dataObject is BaseDataObject baseDataObject)
                {
                    title = baseDataObject.Title;
                }
                else
                {
                    title = dataObject.name;
                }
                Label titleLabel = new(title) { name = "object-item-label" };
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

                Button renameButton = new(() => OpenRenamePopup(dataObject)) { text = "✎" };
                renameButton.AddToClassList(ActionButtonClass);
                renameButton.AddToClassList("rename-button");
                renameButton.AddToClassList("clickable");
                actionsArea.Add(renameButton);

                Button deleteButton = new(() => DeleteObject(dataObject)) { text = "X" };
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
#if ODIN_INSPECTOR

            Type objectType = _selectedObject.GetType();
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

            if (_selectedObject is BaseDataObject baseDataObject)
            {
                VisualElement customElement = baseDataObject.BuildGUI(
                    new DataVisualizerGUIContext(_currentInspectorScriptableObject)
                );
                if (customElement != null)
                {
                    _inspectorContainer.Add(customElement);
                }
            }
        }

        private void DeleteObject(ScriptableObject objectToDelete)
        {
            if (objectToDelete == null)
            {
                return;
            }

            string objectName = objectToDelete.name;

            ConfirmActionPopup popup = ConfirmActionPopup.CreateAndConfigureInstance(
                title: "Confirm Delete",
                message: $"Are you sure you want to delete the asset <b>'{objectName}'</b>?{Environment.NewLine}This action cannot be undone.",
                confirmButtonText: "Delete",
                cancelButtonText: "Cancel",
                position,
                onComplete: confirmed =>
                {
                    if (!confirmed)
                    {
                        return;
                    }

                    string path = AssetDatabase.GetAssetPath(objectToDelete);
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        Debug.LogError(
                            $"Could not find asset path for '{objectName}' post-confirmation. Cannot delete."
                        );
                        ScheduleRefresh();
                        return;
                    }

                    int currentIndex = _selectedObjects.IndexOf(objectToDelete);
                    if (0 <= currentIndex)
                    {
                        _selectedObjects.RemoveAt(currentIndex);
                        if (_selectedType != null)
                            UpdateAndSaveObjectOrderList(_selectedType, _selectedObjects);
                    }
                    bool removed = _objectVisualElementMap.Remove(
                        objectToDelete,
                        out VisualElement visualElement
                    );
                    bool deleted = AssetDatabase.DeleteAsset(path);

                    if (deleted)
                    {
                        AssetDatabase.Refresh();
                        if (removed)
                        {
                            visualElement?.RemoveFromHierarchy();
                        }

                        if (_selectedObject != objectToDelete)
                        {
                            return;
                        }

                        ScriptableObject objectToSelect = null;
                        if (0 <= currentIndex)
                        {
                            if (currentIndex < _selectedObjects.Count)
                            {
                                objectToSelect = _selectedObjects[currentIndex];
                            }
                            else
                            {
                                int nextIndex = currentIndex - 1;
                                if (0 <= nextIndex)
                                {
                                    objectToSelect = _selectedObjects[nextIndex];
                                }
                                else if (0 < _selectedObjects.Count)
                                {
                                    objectToSelect = _selectedObjects[0];
                                }
                            }
                        }
                        SelectObject(objectToSelect);
                    }
                    else
                    {
                        ScheduleRefresh();
                    }
                }
            );

            popup.ShowModalUtility();
        }

        private void OpenRenamePopup(ScriptableObject objectToRename)
        {
            if (objectToRename == null)
            {
                return;
            }

            string currentPath = AssetDatabase.GetAssetPath(objectToRename);
            if (string.IsNullOrWhiteSpace(currentPath))
            {
                EditorUtility.DisplayDialog(
                    "Error",
                    "Cannot rename object: Asset path not found.",
                    "OK"
                );
                return;
            }

            DataVisualizer mainVisualizerWindow = this;

            RenameAssetPopup.ShowWindow(
                currentPath,
                renameSuccessful =>
                {
                    if (renameSuccessful)
                    {
                        if (_selectedType != null)
                        {
                            LoadObjectTypes(_selectedType);
                            BuildObjectsView();
                            SelectObject(objectToRename);
                        }
                        else
                        {
                            BuildObjectsView();
                        }
                    }
                    mainVisualizerWindow.Focus();
                }
            );
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

            // Nullify asset guid references
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

            List<string> customGuidOrder = GetObjectOrderForType(type.FullName); // Use helper
            HashSet<string> orderedGuidsSet = new HashSet<string>(customGuidOrder);

            // 2. Find assets and map GUID to object
            Dictionary<string, BaseDataObject> objectsByGuid =
                new Dictionary<string, BaseDataObject>();
            // FindAssets now works for any ScriptableObject type name
            string[] assetGuids = AssetDatabase.FindAssets($"t:{type.Name}");
            foreach (string assetGuid in assetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (string.IsNullOrEmpty(assetPath))
                    continue;
                // Load as base class, but check type just in case FindAssets was too broad
                ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
                // Make sure it's the correct type AND can be cast to BaseDataObject for list compatibility
                // *** Correction: List should now be List<ScriptableObject> ***
                // Let's change _selectedObjects to List<ScriptableObject>
                if (asset != null && asset.GetType() == type) // Check exact type
                {
                    if (!string.IsNullOrEmpty(assetGuid))
                    {
                        // Cast to BaseDataObject might fail now, need to store as ScriptableObject
                        // We'll handle BaseDataObject specific things elsewhere
                        objectsByGuid[assetGuid] = asset as BaseDataObject; // Store as BDO for now, NEED TO CHANGE LIST TYPE
                    }
                }
            }

            List<ScriptableObject> sortedObjects = new List<ScriptableObject>();

            // 3. Build sorted list based on custom GUID order
            foreach (string guid in customGuidOrder)
            {
                // Need to load the asset by GUID to get the ScriptableObject reference
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                {
                    ScriptableObject dataObject = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                        path
                    );
                    if (dataObject != null && dataObject.GetType() == type)
                    { // Check type again
                        sortedObjects.Add(dataObject);
                        // Remove from map to track remaining items (use path as key?) No, use GUID. Need to rebuild map.
                        objectsByGuid.Remove(guid); // Assumes keys were GUIDs in the map populated above
                    }
                }
            }

            // 4. Add remaining objects (newly created, not in saved order)
            //    Sort alphabetically by asset name
            List<ScriptableObject> remainingObjects = objectsByGuid
                .Values.Select(bdo => (ScriptableObject)bdo)
                .ToList(); // Get remaining SOs
            remainingObjects.Sort(
                (a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase)
            );
            sortedObjects.AddRange(remainingObjects);

            // 5. Assign to the main list (assuming _selectedObjects is List<ScriptableObject>)
            _selectedObjects.Clear(); // Ensure clear before adding range
            _selectedObjects.AddRange(sortedObjects);

            // Reset selection and inspector
            SelectObject(null); // Clear previous object selection
        }

        private void LoadScriptableObjectTypes()
        {
            if (_settings == null)
                _settings = LoadOrCreateSettings();

            List<string> managedTypeFullNames = GetManagedTypeNames(); // Use helper
            HashSet<string> managedTypeSet = new HashSet<string>(managedTypeFullNames); // Faster lookups

            List<Type> allObjectTypes = LoadRelevantScriptableObjectTypes();

            // Filter based on managed list OR if it inherits from BaseDataObject (default inclusion)
            List<Type> typesToDisplay = allObjectTypes
                .Where(t =>
                    managedTypeSet.Contains(t.FullName)
                    || typeof(BaseDataObject).IsAssignableFrom(t)
                )
                .ToList();

            var groups = typesToDisplay
                .GroupBy(GetNamespaceKey) // Use existing namespace key logic
                .Select(g => (key: g.Key, types: g.ToList()));

            _scriptableObjectTypes.Clear();
            _scriptableObjectTypes.AddRange(groups); // Add the filtered and grouped types

            List<string> customNamespaceOrder = GetNamespaceOrder();
            _scriptableObjectTypes.Sort(
                (lhs, rhs) => CompareUsingCustomOrder(lhs.key, rhs.key, customNamespaceOrder)
            );

            foreach ((string key, List<Type> types) in _scriptableObjectTypes)
            {
                // Use GetTypeOrderForNamespace helper
                List<string> customTypeNameOrder = GetTypeOrderForNamespace(key);
                types.Sort(
                    (lhs, rhs) => CompareUsingCustomOrder(lhs.Name, rhs.Name, customTypeNameOrder)
                );
            }
            Debug.Log(
                $"Loaded {_scriptableObjectTypes.Sum(g => g.types.Count)} managed types in {_scriptableObjectTypes.Count} namespaces."
            );
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

        private void OnTypePointerDown(PointerDownEvent evt)
        {
            if (evt.currentTarget is not VisualElement { userData: Type type } targetElement)
            {
                return;
            }

            if (evt.button == 0)
            {
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
            if (0 <= oldDataIndex)
            {
                _selectedObjects.RemoveAt(oldDataIndex);
                int dataInsertIndex = targetIndex;
                dataInsertIndex = Mathf.Clamp(dataInsertIndex, 0, _selectedObjects.Count);
                _selectedObjects.Insert(dataInsertIndex, draggedObject);
                if (_selectedType != null)
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
                Debug.Log(
                    $"TargetIndex {targetIndex}, LastInsertionIndex {_lastGhostInsertIndex}."
                );
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

        private void UpdateAndSaveObjectOrderList(Type type, List<ScriptableObject> orderedObjects) // Takes List<ScriptableObject> now
        {
            if (type == null || orderedObjects == null)
                return;

            List<string> orderedGuids = new List<string>();
            foreach (ScriptableObject obj in orderedObjects)
            {
                if (obj == null)
                    continue;
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path))
                {
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    if (!string.IsNullOrEmpty(guid))
                        orderedGuids.Add(guid);
                }
                else
                {
                    // Object might be newly created/not saved, can't include in saved order yet
                    Debug.LogWarning(
                        $"Cannot get path/GUID for object '{obj.name}' during order save."
                    );
                }
            }

            // Use the persistence helper to save the list
            SetObjectOrderForType(type.FullName, orderedGuids); // Use FullName as key
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
                return new List<string>();
            if (_settings.persistStateInSettingsAsset)
            {
                if (_settings.managedTypeNames == null)
                    _settings.managedTypeNames = new List<string>();
                return _settings.managedTypeNames;
            }
            else
            {
                if (_userState == null)
                    LoadUserStateFromFile();
                if (_userState.managedTypeNames == null)
                    _userState.managedTypeNames = new List<string>();
                return _userState.managedTypeNames;
            }
        }

        // Add SetManagedTypeNames if needed, or just modify list directly and mark dirty/save

        private List<string> GetObjectOrderForType(string typeFullName)
        {
            if (_settings == null || string.IsNullOrEmpty(typeFullName))
                return new List<string>();
            if (_settings.persistStateInSettingsAsset)
            {
                var entry = _settings.objectOrders?.Find(o =>
                    string.Equals(o.TypeFullName, typeFullName, StringComparison.Ordinal)
                );
                return entry?.ObjectGuids ?? new List<string>();
            }
            else
            {
                if (_userState == null)
                    LoadUserStateFromFile();
                var entry = _userState.objectOrders?.Find(o =>
                    string.Equals(o.TypeFullName, typeFullName, StringComparison.Ordinal)
                );
                return entry?.ObjectGuids ?? new List<string>();
            }
        }

        private void SetObjectOrderForType(string typeFullName, List<string> objectGuids)
        {
            if (_settings == null || string.IsNullOrEmpty(typeFullName) || objectGuids == null)
                return;
            if (_settings.persistStateInSettingsAsset)
            {
                var entryList = _settings.GetOrCreateObjectOrderList(typeFullName); // Use helper
                if (!entryList.SequenceEqual(objectGuids))
                { // Check if different
                    entryList.Clear();
                    entryList.AddRange(objectGuids);
                    MarkSettingsDirty();
                }
            }
            else if (_userState != null)
            {
                var entryList = _userState.GetOrCreateObjectOrderList(typeFullName); // Use helper
                if (!entryList.SequenceEqual(objectGuids))
                {
                    entryList.Clear();
                    entryList.AddRange(objectGuids);
                    MarkUserStateDirty(); // Triggers save
                }
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
