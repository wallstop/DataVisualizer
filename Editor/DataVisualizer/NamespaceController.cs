namespace WallstopStudios.Editor.DataVisualizer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Data;
    using Helper;
    using Styles;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer;

    public sealed class NamespaceController
    {
        public Type SelectedType => _selectedType;

        private readonly Dictionary<string, List<Type>> _managedTypes;
        private readonly Dictionary<string, int> _namespaceOrder;
        private readonly Dictionary<Type, VisualElement> _namespaceCache = new();

        private Type _selectedType;

        public NamespaceController(
            Dictionary<string, List<Type>> managedTypes,
            Dictionary<string, int> namespaceOrder
        )
        {
            _managedTypes = managedTypes ?? throw new ArgumentNullException(nameof(managedTypes));
            _namespaceOrder =
                namespaceOrder ?? throw new ArgumentNullException(nameof(namespaceOrder));
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
                if (TryGetNamespace(currentSelection, out VisualElement currentNamespaceElement))
                {
                    currentNamespaceElement.RemoveFromClassList(StyleConstants.SelectedClass);
                }
            }

            _selectedType = type;
            if (!TryGet(type, out VisualElement element))
            {
                Debug.LogWarning($"Failed to find namespace for type '{type.FullName}'");
                return;
            }

            element.AddToClassList(StyleConstants.SelectedClass);
            if (TryGetNamespace(element, out VisualElement newlySelectedNamespace))
            {
                newlySelectedNamespace.AddToClassList(StyleConstants.SelectedClass);
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
                    userData = key,
                };

                namespaceGroupItem.AddToClassList(StyleConstants.NamespaceItemClass);
                namespaceGroupItem.userData = key;
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
                header.Add(namespaceLabel);

                VisualElement headerRight = new();
                headerRight.AddToClassList(StyleConstants.NamespaceHeaderRightClass);
                header.Add(headerRight);
                if (showNamespaceRemoveButton)
                {
                    Button namespaceRemoveButton = new(() =>
                    {
                        dataVisualizer.BuildAndOpenConfirmationPopover(
                            $"Remove {removableTypeCount} non-core type{(removableTypeCount > 1 ? "s" : "")} from namespace '{namespaceKey}'?",
                            "<b><color=red>Remove</color></b>",
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
                    namespaceRemoveButton.AddToClassList(StyleConstants.ActionButtonClass);
                    namespaceRemoveButton.AddToClassList(StyleConstants.DeleteButtonClass);
                    namespaceRemoveButton.AddToClassList(StyleConstants.NamespaceDeleteButton);
                    headerRight.Add(namespaceRemoveButton);
                }

                VisualElement typesContainer = new()
                {
                    name = $"types-container-{key}",
                    userData = key,
                };
                typesContainer.AddToClassList(StyleConstants.TypesContainerClass);
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
                        className: StyleConstants.NamespaceIndicatorClass
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

                    Label typeLabel = new(type.Name) { name = "type-item-label" };
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
                        Button typeRemoveButton = new(() =>
                        {
                            dataVisualizer.BuildAndOpenConfirmationPopover(
                                $"Remove type '{type.Name}' from Data Visualizer?",
                                "<b><color=red>Remove</color></b>",
                                () => HandleRemoveTypeConfirmed(dataVisualizer, type),
                                typeItem
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

                SetLastSelectedTypeName(dataVisualizer, typeName);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error saving type/namespace selection state. {e}");
            }
        }

        private static void SetLastSelectedTypeName(DataVisualizer dataVisualizer, string value)
        {
            dataVisualizer.PersistSettings(
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
                    NamespaceTypeOrder orderEntry = settings.typeOrders?.Find(o =>
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

        internal List<string> GetManagedTypeNames(string namespaceKey = null)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return _managedTypes
                    .SelectMany(kvp => kvp.Value.Select(type => type.FullName))
                    .ToList();
            }
            return _managedTypes
                .GetValueOrDefault(namespaceKey, new List<Type>())
                .Select(type => type.FullName)
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
                type.IsAttributeDefined(out CustomDataVisualization attribute)
                && !string.IsNullOrWhiteSpace(attribute.Namespace)
            )
            {
                return attribute.Namespace;
            }
            return type.Namespace?.Split('.').LastOrDefault() ?? emptyNamespace;
        }
    }
}
