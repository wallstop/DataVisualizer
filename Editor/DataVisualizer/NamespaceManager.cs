namespace WallstopStudios.Editor.DataVisualizer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Data;
    using Helper;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer;

    public sealed class NamespaceManager
    {
        private const string NamespaceItemClass = "namespace-item";
        private const string NamespaceHeaderClass = "namespace-header";
        private const string NamespaceGroupHeaderClass = "namespace-group-header";
        private const string NamespaceIndicatorClass = "namespace-indicator";
        private const string NamespaceLabelClass = "namespace-item__label";
        private const string TypeItemClass = "type-item";
        private const string TypeLabelClass = "type-item__label";

        private const string ArrowCollapsed = "►";
        private const string ArrowExpanded = "▼";

        public Type SelectedType => _selectedType;

        private readonly List<(string key, List<Type> types)> _managedTypes;
        private readonly Dictionary<Type, VisualElement> _namespaceCache = new();

        private Type _selectedType;

        public NamespaceManager(List<(string key, List<Type> types)> managedTypes)
        {
            _managedTypes = managedTypes ?? throw new ArgumentNullException(nameof(managedTypes));
        }

        public bool TryGet(Type type, out VisualElement element)
        {
            return _namespaceCache.TryGetValue(type, out element);
        }

        public void DecrementTypeSelection()
        {
            if (_selectedType == null)
            {
                return;
            }

            if (!TryGet(_selectedType, out VisualElement element))
            {
                return;
            }

            element.RemoveFromClassList("selected");
            VisualElement parent = element.parent;
            if (parent == null)
            {
                return;
            }

            int currentIndex = parent.IndexOf(element);
            currentIndex--;
            if (currentIndex < 0)
            {
                currentIndex = parent.childCount - 1;
            }

            if (0 <= currentIndex && currentIndex < parent.childCount)
            {
                element = parent.ElementAt(currentIndex);
                element.AddToClassList("selected");
                _selectedType = element.userData as Type;
            }
        }

        public void IncrementTypeSelection()
        {
            if (_selectedType == null)
            {
                return;
            }

            if (!TryGet(_selectedType, out VisualElement element))
            {
                return;
            }

            element.RemoveFromClassList("selected");
            VisualElement parent = element.parent;
            if (parent == null)
            {
                return;
            }

            int currentIndex = parent.IndexOf(element);
            currentIndex++;
            if (parent.childCount <= currentIndex)
            {
                currentIndex = 0;
            }

            if (0 <= currentIndex && currentIndex < parent.childCount)
            {
                element = parent.ElementAt(currentIndex);
                element.AddToClassList("selected");
                _selectedType = element.userData as Type;
            }
        }

        public void SelectType(DataVisualizer dataVisualizer, Type type)
        {
            if (_selectedType == type)
            {
                return;
            }

            if (_selectedType != null && TryGet(_selectedType, out VisualElement currentSelection))
            {
                currentSelection.RemoveFromClassList("selected");
                if (TryGetNamespace(currentSelection, out VisualElement currentNamespaceElement))
                {
                    currentNamespaceElement.RemoveFromClassList("selected");
                }
            }

            _selectedType = type;
            if (!TryGet(type, out VisualElement element))
            {
                Debug.LogWarning($"Failed to find namespace for type '{type.FullName}'");
                return;
            }

            element.AddToClassList("selected");
            if (TryGetNamespace(element, out VisualElement newlySelectedNamespace))
            {
                newlySelectedNamespace.AddToClassList("selected");
            }

            SaveNamespaceAndTypeSelectionState(
                dataVisualizer,
                GetNamespaceKey(_selectedType),
                _selectedType.Name
            );

            dataVisualizer.LoadObjectTypes(_selectedType);
            ScriptableObject objectToSelect = dataVisualizer.DetermineObjectToAutoSelect();
            dataVisualizer.BuildObjectsView();
            dataVisualizer.SelectObject(objectToSelect);
        }

        public void Build(DataVisualizer dataVisualizer, ref VisualElement namespaceListContainer)
        {
            if (namespaceListContainer == null)
            {
                return;
            }

            namespaceListContainer.Clear();
            _namespaceCache.Clear();
            foreach ((string key, List<Type> types) in _managedTypes)
            {
                string namespaceKey = key;
                HashSet<string> managedFullNamesInGroup = types
                    .Select(t => t.FullName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                List<Type> nonCoreManagedTypes = types
                    .Where(t =>
                        managedFullNamesInGroup.Contains(t.FullName)
                        && !typeof(BaseDataObject).IsAssignableFrom(t)
                    )
                    .ToList();
                int removableTypeCount = nonCoreManagedTypes.Count;
                bool showNamespaceRemoveButton = removableTypeCount > 0; // Show if *any* removable types exist

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

                namespaceListContainer.Add(namespaceGroupItem);
                namespaceGroupItem.RegisterCallback<PointerDownEvent>(
                    dataVisualizer.OnNamespacePointerDown
                );

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

                VisualElement headerRight = new() { style = { flexShrink = 0 } };
                header.Add(headerRight);
                if (showNamespaceRemoveButton)
                {
                    Button nsRemoveButton = new(() =>
                    {
                        dataVisualizer.BuildAndOpenConfirmationPopover(
                            $"Remove {removableTypeCount} non-core type{(removableTypeCount > 1 ? "s" : "")} from namespace '{namespaceKey}'?",
                            "Remove",
                            () =>
                                HandleRemoveNamespaceTypesConfirmed(
                                    dataVisualizer,
                                    namespaceKey,
                                    nonCoreManagedTypes
                                ),
                            header
                        );
                    })
                    {
                        text = "X",
                        tooltip =
                            $"Remove {removableTypeCount} non-BaseDataObject type{(removableTypeCount > 1 ? "s" : "")}",
                    };
                    nsRemoveButton.AddToClassList("action-button");
                    nsRemoveButton.AddToClassList("delete-button");
                    headerRight.Add(nsRemoveButton);
                }

                VisualElement typesContainer = new()
                {
                    name = $"types-container-{key}",
                    style = { marginLeft = 10 },
                    userData = key,
                };
                namespaceGroupItem.Add(typesContainer);

                bool isCollapsed = GetIsNamespaceCollapsed(dataVisualizer, key);
                ApplyNamespaceCollapsedState(
                    dataVisualizer,
                    indicator,
                    typesContainer,
                    isCollapsed,
                    false
                );

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
                            dataVisualizer,
                            associatedIndicator,
                            associatedTypesContainer,
                            newCollapsedState,
                            true
                        );
                    }

                    evt.StopPropagation();
                });

                // ReSharper disable once ForCanBeConvertedToForeach
                for (int i = 0; i < types.Count; i++)
                {
                    Type type = types[i];

                    bool isManaged = managedFullNamesInGroup.Contains(type.FullName);
                    bool isRemovableType =
                        isManaged && !typeof(BaseDataObject).IsAssignableFrom(type);

                    VisualElement typeItem = new()
                    {
                        name = $"type-item-{type.Name}",
                        userData = type,
                        pickingMode = PickingMode.Position,
                        focusable = true,
                        style =
                        {
                            flexDirection = FlexDirection.Row,
                            alignItems = Align.Center,
                            justifyContent = Justify.SpaceBetween,
                        },
                    };
                    _namespaceCache[type] = typeItem;

                    typeItem.AddToClassList(TypeItemClass);
                    Label typeLabel = new(type.Name) { name = "type-item-label" };
                    typeLabel.AddToClassList(TypeLabelClass);
                    typeLabel.AddToClassList("clickable");
                    typeItem.Add(typeLabel);
                    // ReSharper disable once HeapView.CanAvoidClosure
                    typeItem.RegisterCallback<PointerDownEvent>(evt =>
                        dataVisualizer.OnTypePointerDown(namespaceGroupItem, evt)
                    );
                    // ReSharper disable once HeapView.CanAvoidClosure
                    typeItem.RegisterCallback<PointerUpEvent>(evt =>
                    {
                        if (dataVisualizer._isDragging || evt.button != 0)
                        {
                            return;
                        }

                        SelectType(dataVisualizer, type);
                        evt.StopPropagation();
                    });

                    if (isRemovableType)
                    {
                        Button typeRemoveButton = new(() =>
                        {
                            dataVisualizer.BuildAndOpenConfirmationPopover(
                                $"Remove type '{type.Name}' from Data Visualizer?",
                                "Remove",
                                () => HandleRemoveTypeConfirmed(dataVisualizer, type),
                                typeItem
                            );
                        })
                        {
                            text = "X",
                            tooltip = "Remove this type",
                        };
                        typeRemoveButton.AddToClassList("action-button");
                        typeRemoveButton.AddToClassList("delete-button");
                        typeRemoveButton.style.flexShrink = 0;
                        typeItem.Add(typeRemoveButton);
                    }

                    typesContainer.Add(typeItem);
                }
            }
        }

        private static void SaveNamespaceAndTypeSelectionState(
            DataVisualizer dataVisualizer,
            string namespaceKey,
            string typeName
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(namespaceKey))
                {
                    return;
                }

                SetLastSelectedNamespaceKey(dataVisualizer, namespaceKey);
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    SetLastSelectedTypeName(dataVisualizer, typeName);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error saving type/namespace selection state. {e}");
            }
        }

        private static void SetLastSelectedTypeName(DataVisualizer dataVisualizer, string value)
        {
            DataVisualizerSettings settings = dataVisualizer.Settings;

            if (settings.persistStateInSettingsAsset)
            {
                if (string.Equals(settings.lastSelectedTypeName, value, StringComparison.Ordinal))
                {
                    return;
                }

                settings.lastSelectedTypeName = value;
                settings.MarkDirty();
            }
            else
            {
                DataVisualizerUserState userState = dataVisualizer.UserState;
                if (string.Equals(userState.lastSelectedTypeName, value, StringComparison.Ordinal))
                {
                    return;
                }

                userState.lastSelectedTypeName = value;
                dataVisualizer.MarkUserStateDirty();
            }
        }

        private static void SetLastSelectedNamespaceKey(DataVisualizer dataVisualizer, string value)
        {
            DataVisualizerSettings settings = dataVisualizer.Settings;
            if (settings.persistStateInSettingsAsset)
            {
                if (
                    string.Equals(
                        settings.lastSelectedNamespaceKey,
                        value,
                        StringComparison.Ordinal
                    )
                )
                {
                    return;
                }

                settings.lastSelectedNamespaceKey = value;
                settings.MarkDirty();
            }
            else
            {
                DataVisualizerUserState userState = dataVisualizer.UserState;
                if (
                    string.Equals(
                        userState.lastSelectedNamespaceKey,
                        value,
                        StringComparison.Ordinal
                    )
                )
                {
                    return;
                }

                userState.lastSelectedNamespaceKey = value;
                dataVisualizer.MarkUserStateDirty();
            }
        }

        private static bool TryGetNamespace(
            VisualElement typeElement,
            out VisualElement namespaceElement
        )
        {
            namespaceElement = typeElement?.parent?.parent;
            return namespaceElement != null;
        }

        private static void ApplyNamespaceCollapsedState(
            DataVisualizer dataVisualizer,
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

            indicator.text = collapsed ? ArrowCollapsed : ArrowExpanded;
            typesContainer.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;

            if (saveState)
            {
                string namespaceKey = typesContainer.parent?.userData as string;
                if (string.IsNullOrWhiteSpace(namespaceKey))
                {
                    return;
                }
                SetIsNamespaceCollapsed(dataVisualizer, namespaceKey, collapsed);
            }
        }

        private static void SetIsNamespaceCollapsed(
            DataVisualizer dataVisualizer,
            string namespaceKey,
            bool isCollapsed
        )
        {
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return;
            }

            dataVisualizer.PersistSettings(
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

        private static bool GetIsNamespaceCollapsed(
            DataVisualizer dataVisualizer,
            string namespaceKey
        )
        {
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return false;
            }

            DataVisualizerSettings settings = dataVisualizer.Settings;
            if (settings.persistStateInSettingsAsset)
            {
                NamespaceCollapseState entry = settings.namespaceCollapseStates?.Find(state =>
                    string.Equals(state.namespaceKey, namespaceKey, StringComparison.Ordinal)
                );
                return entry?.isCollapsed ?? false;
            }
            else
            {
                DataVisualizerUserState userState = dataVisualizer.UserState;
                NamespaceCollapseState entry = userState.namespaceCollapseStates?.Find(state =>
                    string.Equals(state.namespaceKey, namespaceKey, StringComparison.Ordinal)
                );
                return entry?.isCollapsed ?? false;
            }
        }

        private void HandleRemoveNamespaceTypesConfirmed(
            DataVisualizer dataVisualizer,
            string namespaceKey,
            List<Type> typesToRemove
        )
        {
            if (typesToRemove == null || typesToRemove.Count == 0)
            {
                return;
            }

            List<string> currentManagedList = GetManagedTypeNames(namespaceKey);
            bool changed = false;
            foreach (Type type in typesToRemove)
            {
                if (
                    typeof(BaseDataObject).IsAssignableFrom(type)
                    || !currentManagedList.Remove(type.FullName)
                )
                {
                    continue;
                }

                changed = true;
                dataVisualizer.SetLastSelectedObjectGuidForType(type.FullName, null);
                RemoveTypeOrderEntry(dataVisualizer, namespaceKey, type.Name);
            }
            if (changed)
            {
                PersistManagedTypesList(dataVisualizer, currentManagedList);
                DataVisualizer.SignalRefresh();
            }
        }

        private void HandleRemoveTypeConfirmed(DataVisualizer dataVisualizer, Type typeToRemove)
        {
            if (typeToRemove == null || typeof(BaseDataObject).IsAssignableFrom(typeToRemove))
            {
                Debug.LogWarning(
                    $"Attempted to remove BaseDataObject derivative '{typeToRemove?.Name}' or null type."
                );
                return;
            }

            List<string> currentManagedList = GetManagedTypeNames(typeToRemove.Namespace);
            if (currentManagedList.Remove(typeToRemove.FullName))
            {
                dataVisualizer.SetLastSelectedObjectGuidForType(typeToRemove.FullName, null);
                RemoveTypeOrderEntry(
                    dataVisualizer,
                    GetNamespaceKey(typeToRemove),
                    typeToRemove.Name
                );

                PersistManagedTypesList(dataVisualizer, currentManagedList);
                DataVisualizer.SignalRefresh();
            }
            else
            {
                Debug.LogWarning(
                    $"Type '{typeToRemove.FullName}' was not found in managed list during removal."
                );
            }
        }

        private static void PersistManagedTypesList(
            DataVisualizer dataVisualizer,
            List<string> managedList
        )
        {
            dataVisualizer.PersistSettings(
                settings =>
                {
                    if (
                        settings.managedTypeNames != null
                        && settings.managedTypeNames.SequenceEqual(managedList)
                    )
                    {
                        return false;
                    }
                    settings.managedTypeNames = new List<string>(managedList);
                    return true;
                },
                userState =>
                {
                    if (
                        userState.managedTypeNames != null
                        && userState.managedTypeNames.SequenceEqual(managedList)
                    )
                    {
                        return false;
                    }

                    userState.managedTypeNames = new List<string>(managedList);
                    return true;
                }
            );
        }

        internal static void RemoveTypeOrderEntry(
            DataVisualizer dataVisualizer,
            string namespaceKey,
            string typeName
        )
        {
            if (string.IsNullOrWhiteSpace(namespaceKey) || string.IsNullOrWhiteSpace(typeName))
            {
                return;
            }

            dataVisualizer.PersistSettings(
                settings =>
                {
                    NamespaceTypeOrder orderEntry = settings.typeOrder?.Find(o =>
                        string.Equals(o.namespaceKey, namespaceKey, StringComparison.Ordinal)
                    );
                    return orderEntry != null && orderEntry.typeNames.Remove(typeName);
                },
                userState =>
                {
                    NamespaceTypeOrder orderEntry = userState.typeOrders?.Find(o =>
                        string.Equals(o.namespaceKey, namespaceKey, StringComparison.Ordinal)
                    );
                    return orderEntry != null && orderEntry.typeNames.Remove(typeName);
                }
            );
        }

        private List<string> GetManagedTypeNames(string namespaceKey)
        {
            foreach ((string key, List<Type> types) in _managedTypes)
            {
                if (string.Equals(key, namespaceKey, StringComparison.OrdinalIgnoreCase))
                {
                    return types.Select(type => type.FullName).ToList();
                }
            }

            return new List<string>(0);
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
}
