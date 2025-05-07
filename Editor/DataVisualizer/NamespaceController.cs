namespace WallstopStudios.DataVisualizer.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Data;
    using Helper;
    using Styles;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed class NamespaceController
    {
        private const string TypeItemLabelName = "type-item-label";

        public Type SelectedType => _selectedType;

        private readonly Dictionary<Type, VisualElement> _namespaceCache = new();

        private readonly Dictionary<string, List<Type>> _managedTypes;
        private readonly Dictionary<string, int> _namespaceOrder;
        private Type _selectedType;

        public NamespaceController(
            Dictionary<string, List<Type>> managedTypes,
            Dictionary<string, int> namespaceOrder
        )
        {
            // Need to store references, not copy, for double data binding
            _managedTypes = managedTypes ?? throw new ArgumentNullException(nameof(managedTypes));
            _namespaceOrder =
                namespaceOrder ?? throw new ArgumentNullException(nameof(namespaceOrder));
            _selectedType = null;
        }

        public void Clear()
        {
            _selectedType = null;
        }

        public void DecrementTypeSelection(DataVisualizer dataVisualizer)
        {
            if (!InternalDeselectAndGetCurrentIndex(out VisualElement parent, out int currentIndex))
            {
                return;
            }

            currentIndex--;
            if (currentIndex < 0)
            {
                currentIndex = parent.childCount - 1;
            }

            Type type = InternalSelected(parent, currentIndex);
            SelectType(dataVisualizer, type);
        }

        public void IncrementTypeSelection(DataVisualizer dataVisualizer)
        {
            if (!InternalDeselectAndGetCurrentIndex(out VisualElement parent, out int currentIndex))
            {
                return;
            }

            currentIndex++;
            if (parent.childCount <= currentIndex)
            {
                currentIndex = 0;
            }
            Type type = InternalSelected(parent, currentIndex);
            SelectType(dataVisualizer, type);
        }

        private bool InternalDeselectAndGetCurrentIndex(
            out VisualElement parent,
            out int currentIndex
        )
        {
            if (!TryGet(_selectedType, out VisualElement element))
            {
                parent = default;
                currentIndex = default;
                return false;
            }

            element.RemoveFromClassList(StyleConstants.SelectedClass);
            parent = element.parent;
            if (parent == null)
            {
                currentIndex = default;
                return false;
            }

            currentIndex = parent.IndexOf(element);
            return true;
        }

        private static Type InternalSelected(VisualElement parent, int index)
        {
            if (0 > index || index >= parent.childCount)
            {
                return null;
            }

            VisualElement element = parent.ElementAt(index);
            element.AddToClassList(StyleConstants.SelectedClass);
            return element.userData as Type;
        }

        public void SelectType(DataVisualizer dataVisualizer, Type type)
        {
            if (_selectedType == type)
            {
                return;
            }

            if (TryGet(_selectedType, out VisualElement currentSelection))
            {
                currentSelection.RemoveFromClassList(StyleConstants.SelectedClass);
                currentSelection
                    .Q<Label>(TypeItemLabelName)
                    ?.AddToClassList(StyleConstants.ClickableClass);
                if (TryGetNamespace(currentSelection, out VisualElement currentNamespaceElement))
                {
                    currentNamespaceElement.RemoveFromClassList(StyleConstants.SelectedClass);
                }
            }

            _selectedType = null;
            if (!TryGet(type, out VisualElement element))
            {
                if (type != null)
                {
                    Debug.LogWarning(
                        $"Could not find type {type?.FullName}. Namespace cache: [{string.Join(",", _namespaceCache.Keys.Select(nsType => nsType.Name).OrderBy(x => x))}]."
                    );
                }

                return;
            }

            _selectedType = type;
            element.AddToClassList(StyleConstants.SelectedClass);
            element.Q<Label>(TypeItemLabelName)?.RemoveFromClassList(StyleConstants.ClickableClass);
            if (TryGetNamespace(element, out VisualElement newlySelectedNamespace))
            {
                newlySelectedNamespace.AddToClassList(StyleConstants.SelectedClass);
            }

            string namespaceKey = GetNamespaceKey(_selectedType);
            SaveNamespaceAndTypeSelectionState(dataVisualizer, namespaceKey, _selectedType);
            dataVisualizer.LoadObjectTypes(_selectedType);
            ScriptableObject objectToSelect = dataVisualizer.DetermineObjectToAutoSelect();
            dataVisualizer.BuildObjectsView();
            dataVisualizer.SelectObject(objectToSelect);
        }

        public void Build(DataVisualizer dataVisualizer, ref VisualElement namespaceListContainer)
        {
            _selectedType = null;
            namespaceListContainer ??= new VisualElement { name = "namespace-list" };
            namespaceListContainer.Clear();
            _namespaceCache.Clear();
            foreach (
                (string key, List<Type> types) in _managedTypes.OrderBy(kvp =>
                    _namespaceOrder.GetValueOrDefault(kvp.Key, _namespaceOrder.Count)
                )
            )
            {
                string namespaceKey = key;
                List<Type> nonCoreManagedTypes = types
                    .Where(t => !typeof(BaseDataObject).IsAssignableFrom(t))
                    .ToList();
                int removableTypeCount = nonCoreManagedTypes.Count;
                bool showNamespaceRemoveButton = removableTypeCount > 1;

                VisualElement namespaceGroupItem = new()
                {
                    name = $"namespace-group-{key}",
                    userData = namespaceKey,
                };

                namespaceGroupItem.AddToClassList(StyleConstants.NamespaceItemClass);
                if (types.Count == 0)
                {
                    namespaceGroupItem.AddToClassList(StyleConstants.NamespaceGroupItemEmptyClass);
                }

                namespaceListContainer.Add(namespaceGroupItem);
                namespaceGroupItem.RegisterCallback<PointerDownEvent>(
                    dataVisualizer.OnNamespacePointerDown
                );

                VisualElement header = new() { name = $"namespace-header-{key}" };
                header.AddToClassList(StyleConstants.NamespaceHeaderClass);
                namespaceGroupItem.Add(header);

                Label indicator = new(StyleConstants.ArrowExpanded)
                {
                    name = $"namespace-indicator-{key}",
                };
                indicator.AddToClassList(StyleConstants.NamespaceIndicatorClass);
                indicator.AddToClassList(StyleConstants.ClickableClass);
                header.Add(indicator);

                Label namespaceLabel = new(key) { name = $"namespace-name-{key}" };
                namespaceLabel.AddToClassList(StyleConstants.BoldClass);
                namespaceLabel.AddToClassList(StyleConstants.NamespaceLabelClass);
                namespaceLabel.AddToClassList(StyleConstants.ClickableClass);

                header.Add(namespaceLabel);

                VisualElement headerRight = new();
                headerRight.AddToClassList(StyleConstants.NamespaceHeaderRightClass);
                header.Add(headerRight);
                if (showNamespaceRemoveButton)
                {
                    Button namespaceRemoveButton = null;
                    namespaceRemoveButton = new Button(() =>
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
                            namespaceRemoveButton
                        );
                    })
                    {
                        text = "X",
                        tooltip =
                            $"Remove {removableTypeCount} non-BaseDataObject type{(removableTypeCount > 1 ? "s" : "")}",
                    };
                    namespaceRemoveButton.AddToClassList(StyleConstants.ActionButtonClass);
                    namespaceRemoveButton.AddToClassList(StyleConstants.DeleteButtonClass);
                    namespaceRemoveButton.AddToClassList(StyleConstants.NamespaceDeleteButton);
                    headerRight.Add(namespaceRemoveButton);
                }

                VisualElement typesContainer = new()
                {
                    name = $"types-container-{key}",
                    userData = namespaceKey,
                };
                typesContainer.AddToClassList(StyleConstants.TypesContainerClass);
                namespaceGroupItem.Add(typesContainer);

                bool isCollapsed = GetIsNamespaceCollapsed(dataVisualizer, namespaceKey);
                ApplyNamespaceCollapsedState(
                    dataVisualizer,
                    indicator,
                    typesContainer,
                    isCollapsed,
                    false
                );

                // ReSharper disable once HeapView.CanAvoidClosure
                indicator.RegisterCallback<PointerDownEvent>(ToggleNamespace);

                // ReSharper disable once ForCanBeConvertedToForeach
                for (int i = 0; i < types.Count; i++)
                {
                    Type type = types[i];
                    bool isRemovableType = !typeof(BaseDataObject).IsAssignableFrom(type);

                    VisualElement typeItem = new()
                    {
                        name = $"type-item-{type.Name}",
                        userData = type,
                        pickingMode = PickingMode.Position,
                        focusable = true,
                    };
                    typeItem.AddToClassList(StyleConstants.TypeItemClass);
                    _namespaceCache[type] = typeItem;

                    Label typeLabel = new(GetTypeDisplayName(type)) { name = TypeItemLabelName };
                    typeLabel.AddToClassList(StyleConstants.TypeLabelClass);
                    typeLabel.AddToClassList(StyleConstants.ClickableClass);
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
                        Button typeRemoveButton = null;
                        typeRemoveButton = new Button(() =>
                        {
                            dataVisualizer.BuildAndOpenConfirmationPopover(
                                $"Remove type '<color=yellow><i>{type.Name}</i></color>' from Data Visualizer?",
                                "Remove",
                                () => HandleRemoveTypeConfirmed(dataVisualizer, type),
                                typeRemoveButton
                            );
                        })
                        {
                            text = "X",
                            tooltip = $"Remove {type.FullName}",
                        };
                        typeRemoveButton.AddToClassList(StyleConstants.ActionButtonClass);
                        typeRemoveButton.AddToClassList(StyleConstants.DeleteButtonClass);
                        typeItem.Add(typeRemoveButton);
                    }

                    typesContainer.Add(typeItem);
                }

                continue;

                void ToggleNamespace(PointerDownEvent evt)
                {
                    if (evt.button != 0 || evt.propagationPhase == PropagationPhase.TrickleDown)
                    {
                        return;
                    }

                    VisualElement parentGroup = header.parent;
                    Label associatedIndicator = parentGroup?.Q<Label>(
                        className: StyleConstants.NamespaceIndicatorClass
                    );
                    VisualElement associatedTypesContainer = parentGroup?.Q<VisualElement>(
                        $"types-container-{namespaceKey}"
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
                }
            }
        }

        private bool TryGet(Type type, out VisualElement element)
        {
            if (type != null)
            {
                return _namespaceCache.TryGetValue(type, out element);
            }

            element = default;
            return false;
        }

        private static void SaveNamespaceAndTypeSelectionState(
            DataVisualizer dataVisualizer,
            string namespaceKey,
            Type type
        )
        {
            try
            {
                if (string.IsNullOrWhiteSpace(namespaceKey))
                {
                    return;
                }

                SetLastSelectedNamespaceKey(dataVisualizer, namespaceKey);
                string typeFullName = type?.FullName;
                if (string.IsNullOrWhiteSpace(typeFullName))
                {
                    return;
                }

                SetLastSelectedTypeName(dataVisualizer, typeFullName);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error saving type/namespace selection state. {e}");
            }
        }

        private static void SetLastSelectedTypeName(
            DataVisualizer dataVisualizer,
            string typeFullName
        )
        {
            dataVisualizer.PersistSettings(
                settings =>
                {
                    if (
                        string.Equals(
                            settings.lastSelectedTypeName,
                            typeFullName,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return false;
                    }
                    settings.lastSelectedTypeName = typeFullName;
                    return true;
                },
                userState =>
                {
                    if (
                        string.Equals(
                            userState.lastSelectedTypeName,
                            typeFullName,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return false;
                    }
                    userState.lastSelectedTypeName = typeFullName;
                    return true;
                }
            );
        }

        private static void SetLastSelectedNamespaceKey(DataVisualizer dataVisualizer, string value)
        {
            dataVisualizer.PersistSettings(
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

            indicator.text = collapsed
                ? StyleConstants.ArrowCollapsed
                : StyleConstants.ArrowExpanded;
            typesContainer.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;

            if (!saveState)
            {
                return;
            }

            string namespaceKey = typesContainer.parent?.userData as string;
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return;
            }
            SetIsNamespaceCollapsed(dataVisualizer, namespaceKey, collapsed);
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
                    bool dirty = !settings.HasCollapseState(namespaceKey);
                    NamespaceCollapseState entry = settings.GetOrCreateCollapseState(namespaceKey);
                    if (entry.isCollapsed == isCollapsed)
                    {
                        dirty = true;
                    }

                    entry.isCollapsed = isCollapsed;
                    return dirty;
                },
                userState =>
                {
                    bool dirty = !userState.HasCollapseState(namespaceKey);
                    NamespaceCollapseState entry = userState.GetOrCreateCollapseState(namespaceKey);
                    if (entry.isCollapsed != isCollapsed)
                    {
                        dirty = true;
                    }
                    entry.isCollapsed = isCollapsed;
                    return dirty;
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
                string typeName = type.FullName;
                if (
                    typeof(BaseDataObject).IsAssignableFrom(type)
                    || !currentManagedList.Remove(typeName)
                )
                {
                    continue;
                }

                changed = true;
                dataVisualizer.SetLastSelectedObjectGuidForType(typeName, null);
                RemoveTypeOrderEntry(dataVisualizer, namespaceKey, typeName);
            }

            if (changed)
            {
                PersistManagedTypesList(dataVisualizer, currentManagedList);
                DataVisualizer.SignalRefresh();
            }
            else
            {
                Debug.LogWarning(
                    $"No change detected for namespace '{namespaceKey}' removal (tried to remove [{string.Join(",", typesToRemove.Select(type => type.Name))}])"
                );
            }
        }

        private void HandleRemoveTypeConfirmed(DataVisualizer dataVisualizer, Type typeToRemove)
        {
            if (typeToRemove == null || typeof(BaseDataObject).IsAssignableFrom(typeToRemove))
            {
                Debug.LogWarning(
                    $"Attempted to remove BaseDataObject derivative '{typeToRemove?.FullName}' or null type."
                );
                return;
            }

            string namespaceKey = GetNamespaceKey(typeToRemove);
            List<string> currentManagedList = GetManagedTypeNames(namespaceKey);
            string typeName = typeToRemove.FullName;
            if (currentManagedList.Remove(typeName))
            {
                dataVisualizer.SetLastSelectedObjectGuidForType(typeName, null);
                RemoveTypeOrderEntry(dataVisualizer, namespaceKey, typeName);
                PersistManagedTypesList(dataVisualizer, currentManagedList);
                DataVisualizer.SignalRefresh();
            }
            else
            {
                Debug.LogWarning(
                    $"Type '{typeName}' was not found in managed list during removal. Current list: [{string.Join(",", currentManagedList)}]."
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
                    settings.managedTypeNames = new List<string>(managedList);
                    return true;
                },
                userState =>
                {
                    userState.managedTypeNames = new List<string>(managedList);
                    return true;
                }
            );
        }

        private static void RemoveTypeOrderEntry(
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
                    NamespaceTypeOrder orderEntry = settings.typeOrders?.Find(typeOrder =>
                        string.Equals(
                            typeOrder.namespaceKey,
                            namespaceKey,
                            StringComparison.Ordinal
                        )
                    );
                    return orderEntry != null && orderEntry.typeNames.Remove(typeName);
                },
                userState =>
                {
                    NamespaceTypeOrder orderEntry = userState.typeOrders?.Find(typeOrder =>
                        string.Equals(
                            typeOrder.namespaceKey,
                            namespaceKey,
                            StringComparison.Ordinal
                        )
                    );
                    return orderEntry != null && orderEntry.typeNames.Remove(typeName);
                }
            );
        }

        internal List<string> GetManagedTypeNames(string namespaceKey)
        {
            return _managedTypes
                .GetValueOrDefault(namespaceKey, new List<Type>())
                .Select(type => type.FullName)
                .ToList();
        }

        internal List<string> GetAllManagedTypeNames()
        {
            return _managedTypes
                .Values.SelectMany(types => types.Select(type => type.FullName))
                .ToList();
        }

        internal static string GetNamespaceKey(Type type)
        {
            const string emptyNamespace = "No Namespace";
            if (type == null)
            {
                return emptyNamespace;
            }

            if (
                type.IsAttributeDefined(out CustomDataVisualizationAttribute attribute)
                && !string.IsNullOrWhiteSpace(attribute.Namespace)
            )
            {
                return attribute.Namespace;
            }
            return type.Namespace?.Split('.').LastOrDefault() ?? emptyNamespace;
        }

        internal static string GetTypeDisplayName(Type type)
        {
            const string emptyType = "No Type";
            if (type == null)
            {
                return emptyType;
            }
            if (
                type.IsAttributeDefined(out CustomDataVisualizationAttribute attribute)
                && !string.IsNullOrWhiteSpace(attribute.TypeName)
            )
            {
                return attribute.TypeName;
            }

            return type.Name;
        }
    }
}
