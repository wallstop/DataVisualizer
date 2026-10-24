// ReSharper disable AccessToModifiedClosure
namespace WallstopStudios.DataVisualizer.Editor
{
    using System;
    using System.Collections.Generic;
    using Data;
    using Extensions;
    using Styles;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Utilities;

    public sealed class NamespaceController
    {
        private const string TypeItemLabelName = "type-item-label";

        public Type SelectedType => _selectedType;

        internal readonly Dictionary<Type, VisualElement> _namespaceCache = new();

        private readonly Dictionary<string, List<Type>> _managedTypes;
        private readonly Dictionary<string, int> _namespaceOrder;
        private Type _selectedType;

        public NamespaceController(Dictionary<string, List<Type>> managedTypes, Dictionary<string, int> namespaceOrder)
        {
            // Need to store references, not copy, for double data binding
            _managedTypes = managedTypes ?? throw new ArgumentNullException(nameof(managedTypes));
            _namespaceOrder = namespaceOrder ?? throw new ArgumentNullException(nameof(namespaceOrder));
            _selectedType = null;
        }

        public static void RecalibrateVisualElements(VisualElement item, int offset = 0)
        {
            VisualElement parent = item?.parent;
            if (parent == null)
            {
                return;
            }

            int index = parent.IndexOf(item);
            if (index < 0)
            {
                return;
            }

            Button goUpButton = item.Q<Button>("go-up-button");
            if (goUpButton != null)
            {
                int compensatedIndex = Mathf.Max(0, index - offset);
                goUpButton.EnableInClassList("go-button-disabled", compensatedIndex == 0);
                goUpButton.EnableInClassList(StyleConstants.ActionButtonClass, compensatedIndex != 0);
                goUpButton.EnableInClassList("go-button", compensatedIndex != 0);
            }
            Button goDownButton = item.Q<Button>("go-down-button");
            if (goDownButton != null)
            {
                goDownButton.EnableInClassList("go-button-disabled", index == parent.childCount - 1);
                goDownButton.EnableInClassList(StyleConstants.ActionButtonClass, index != parent.childCount - 1);
                goDownButton.EnableInClassList("go-button", index != parent.childCount - 1);
            }
        }

        public static List<string> RemoveManagedTypeNames(IEnumerable<string> managedTypeNames, IEnumerable<string> typeNamesToRemove)
        {
            if (managedTypeNames == null)
            {
                return new List<string>();
            }

            HashSet<string> removedTypeNames = new(StringComparer.Ordinal);
            if (typeNamesToRemove != null)
            {
                foreach (string removedTypeName in typeNamesToRemove)
                {
                    if (!string.IsNullOrWhiteSpace(removedTypeName))
                    {
                        removedTypeNames.Add(removedTypeName);
                    }
                }
            }

            if (removedTypeNames.Count == 0)
            {
                return new List<string>(managedTypeNames);
            }

            List<string> remainingTypeNames = new();
            HashSet<string> emittedTypeNames = new(StringComparer.Ordinal);
            foreach (string typeName in managedTypeNames)
            {
                if (removedTypeNames.Contains(typeName) || !emittedTypeNames.Add(typeName))
                {
                    continue;
                }

                remainingTypeNames.Add(typeName);
            }

            return remainingTypeNames;
        }

        public static string GetNamespaceKey(Type type)
        {
            const string emptyNamespace = "No Namespace";
            if (type == null)
            {
                return emptyNamespace;
            }

            if (type.IsAttributeDefined(out CustomDataVisualizationAttribute attribute) && !string.IsNullOrWhiteSpace(attribute.Namespace))
            {
                return attribute.Namespace;
            }

            string namespaceName = type.Namespace;
            if (namespaceName == null)
            {
                return emptyNamespace;
            }

            int lastSeparatorIndex = namespaceName.LastIndexOf('.');
            return lastSeparatorIndex < 0 ? namespaceName : namespaceName.Substring(lastSeparatorIndex + 1);
        }

        public static Type GetAdjacentVisibleType(VisualElement current, bool forward)
        {
            VisualElement parent = current?.parent;
            if (parent == null)
            {
                return null;
            }
            for (VisualElement ancestor = parent; ancestor != null; ancestor = ancestor.parent)
            {
                if (ancestor.style.display == DisplayStyle.None || ancestor.style.visibility == Visibility.Hidden)
                {
                    return null;
                }
            }
            int currentIndex = parent.IndexOf(current);
            int step = forward ? 1 : -1;
            for (int offset = 1; offset <= parent.childCount; offset++)
            {
                int index = (currentIndex + step * offset + parent.childCount) % parent.childCount;
                VisualElement candidate = parent[index];
                if (candidate.userData is Type type && candidate.style.display != DisplayStyle.None && candidate.style.visibility != Visibility.Hidden)
                {
                    return type;
                }
            }
            return null;
        }

        internal static bool IsTypeRemovable(Type type)
        {
            return type == null || (!typeof(BaseDataObject).IsAssignableFrom(type) && !type.IsAttributeDefined<CustomDataVisualizationAttribute>());
        }

        internal static string GetTypeDisplayName(Type type)
        {
            const string emptyType = "No Type";
            if (type == null)
            {
                return emptyType;
            }
            if (type.IsAttributeDefined(out CustomDataVisualizationAttribute attribute, inherit: false) && !string.IsNullOrWhiteSpace(attribute.TypeName))
            {
                return attribute.TypeName;
            }

            return type.Name;
        }

        private static void SaveNamespaceAndTypeSelectionState(DataVisualizer dataVisualizer, string namespaceKey, Type type)
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

                SetLastSelectedTypeFullName(dataVisualizer, typeFullName);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error saving type/namespace selection state. {e}");
            }
        }

        private static void SetLastSelectedTypeFullName(DataVisualizer dataVisualizer, string typeFullName)
        {
            dataVisualizer.PersistSettings(
                settings =>
                {
                    if (string.Equals(settings.lastSelectedTypeFullName, typeFullName, StringComparison.Ordinal))
                    {
                        return false;
                    }
                    settings.lastSelectedTypeFullName = typeFullName;
                    return true;
                },
                userState =>
                {
                    if (string.Equals(userState.lastSelectedTypeFullName, typeFullName, StringComparison.Ordinal))
                    {
                        return false;
                    }
                    userState.lastSelectedTypeFullName = typeFullName;
                    return true;
                }
            );
        }

        private static void SetLastSelectedNamespaceKey(DataVisualizer dataVisualizer, string value)
        {
            dataVisualizer.PersistSettings(
                settings =>
                {
                    if (string.Equals(settings.lastSelectedNamespaceKey, value, StringComparison.Ordinal))
                    {
                        return false;
                    }
                    settings.lastSelectedNamespaceKey = value;
                    return true;
                },
                userState =>
                {
                    if (string.Equals(userState.lastSelectedNamespaceKey, value, StringComparison.Ordinal))
                    {
                        return false;
                    }
                    userState.lastSelectedNamespaceKey = value;
                    return true;
                }
            );
        }

        private static bool TryGetNamespace(VisualElement typeElement, out VisualElement namespaceElement)
        {
            VisualElement candidateNamespace = typeElement?.parent?.parent;
            if (candidateNamespace == null)
            {
                namespaceElement = null;
                return false;
            }

            namespaceElement = candidateNamespace;
            return true;
        }

        private static void ApplyNamespaceCollapsedState(DataVisualizer dataVisualizer, TextElement indicator, VisualElement typesContainer, bool collapsed, bool saveState)
        {
            if (indicator == null || typesContainer == null)
            {
                return;
            }

            indicator.text = collapsed ? StyleConstants.ArrowCollapsed : StyleConstants.ArrowExpanded;
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

        private static void SetIsNamespaceCollapsed(DataVisualizer dataVisualizer, string namespaceKey, bool isCollapsed)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return;
            }

            dataVisualizer.PersistSettings(
                settings => settings.SetNamespaceCollapsed(namespaceKey, isCollapsed),
                userState => userState.SetNamespaceCollapsed(namespaceKey, isCollapsed)
            );
        }

        private static bool GetIsNamespaceCollapsed(DataVisualizer dataVisualizer, string namespaceKey)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return false;
            }

            DataVisualizerSettings settings = dataVisualizer.Settings;
            if (settings.persistStateInSettingsAsset)
            {
                NamespaceCollapseState entry = settings.namespaceCollapseStates?.Find(state => string.Equals(state.namespaceKey, namespaceKey, StringComparison.Ordinal));
                return entry?.isCollapsed ?? false;
            }
            else
            {
                DataVisualizerUserState userState = dataVisualizer.UserState;
                NamespaceCollapseState entry = userState.namespaceCollapseStates?.Find(state => string.Equals(state.namespaceKey, namespaceKey, StringComparison.Ordinal));
                return entry?.isCollapsed ?? false;
            }
        }

        private static void PersistManagedTypesList(DataVisualizer dataVisualizer, List<string> managedList)
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

        private static void RemoveTypeOrderEntry(DataVisualizer dataVisualizer, string namespaceKey, string typeName)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey) || string.IsNullOrWhiteSpace(typeName))
            {
                return;
            }

            dataVisualizer.PersistSettings(
                settings =>
                {
                    NamespaceTypeOrder orderEntry = settings.typeOrders?.Find(typeOrder => string.Equals(typeOrder.namespaceKey, namespaceKey, StringComparison.Ordinal));
                    return orderEntry != null && orderEntry.typeNames.Remove(typeName);
                },
                userState =>
                {
                    NamespaceTypeOrder orderEntry = userState.typeOrders?.Find(typeOrder => string.Equals(typeOrder.namespaceKey, namespaceKey, StringComparison.Ordinal));
                    return orderEntry != null && orderEntry.typeNames.Remove(typeName);
                }
            );
        }

        private static void RemoveNamespaceCollapseState(DataVisualizer dataVisualizer, string namespaceKey)
        {
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return;
            }

            dataVisualizer.PersistSettings(settings => settings.RemoveNamespaceCollapseState(namespaceKey), userState => userState.RemoveNamespaceCollapseState(namespaceKey));
        }

        private static HashSet<Type> CollectManagedTypes(Dictionary<string, List<Type>> managedTypes)
        {
            HashSet<Type> managedTypeSet = new();
            foreach (List<Type> types in managedTypes.Values)
            {
                foreach (Type type in types)
                {
                    managedTypeSet.Add(type);
                }
            }
            return managedTypeSet;
        }

        /*
            Stable ordering parity with the removed `OrderBy` chain: entries with a persisted
            order key sort by that key ascending, and equal-key entries keep dictionary
            enumeration order. The insertion sort is what provides that stability.
        */
        private static KeyValuePair<string, List<Type>>[] OrderedNamespaceEntries(Dictionary<string, List<Type>> managedTypes, Dictionary<string, int> namespaceOrder)
        {
            KeyValuePair<string, List<Type>>[] entries = new KeyValuePair<string, List<Type>>[managedTypes.Count];
            int[] orderKeys = new int[entries.Length];
            int defaultOrder = namespaceOrder.Count;
            int index = 0;
            foreach (KeyValuePair<string, List<Type>> entry in managedTypes)
            {
                entries[index] = entry;
                orderKeys[index] = namespaceOrder.GetValueOrDefault(entry.Key, defaultOrder);
                index += 1;
            }

            for (int outer = 1; outer < entries.Length; outer++)
            {
                int currentOrder = orderKeys[outer];
                KeyValuePair<string, List<Type>> currentEntry = entries[outer];
                int inner = outer - 1;
                while (0 <= inner && currentOrder < orderKeys[inner])
                {
                    orderKeys[inner + 1] = orderKeys[inner];
                    entries[inner + 1] = entries[inner];
                    inner -= 1;
                }

                orderKeys[inner + 1] = currentOrder;
                entries[inner + 1] = currentEntry;
            }

            return entries;
        }

        public void Clear()
        {
            _selectedType = null;
        }

        public void DecrementTypeSelection(DataVisualizer dataVisualizer)
        {
            NavigateTypeSelection(dataVisualizer, forward: false);
        }

        public void IncrementTypeSelection(DataVisualizer dataVisualizer)
        {
            NavigateTypeSelection(dataVisualizer, forward: true);
        }

        public void SelectType(DataVisualizer dataVisualizer, Type type)
        {
            /*
                Manual type switching starts asset loads and selection persistence; both are
                package-driven work, so they stay suspended during Play Mode exactly like the
                mutation actions.
            */
            if (dataVisualizer.IsAssetEditingSuspended)
            {
                return;
            }

            if (_selectedType == type)
            {
                return;
            }

            if (TryGet(_selectedType, out VisualElement currentSelection))
            {
                currentSelection.RemoveFromClassList(StyleConstants.SelectedClass);
                currentSelection.Q<Label>(TypeItemLabelName)?.AddToClassList(StyleConstants.ClickableClass);
                if (TryGetNamespace(currentSelection, out VisualElement currentNamespaceElement))
                {
                    currentNamespaceElement.RemoveFromClassList(StyleConstants.SelectedClass);
                }
            }

            _selectedType = null;
            if (!TryGet(type, out VisualElement element))
            {
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
            dataVisualizer.LoadObjectTypesAsync(_selectedType);
            /*
                LoadObjectTypesAsync rebuilds the object view and selects the saved/first object
                synchronously from the priority batch, then streams the remaining objects in.
            */
            dataVisualizer.BuildProcessorColumnView();
            dataVisualizer.UpdateCreateObjectButtonStyle();
            dataVisualizer.UpdateLabelAreaAndFilter();
            dataVisualizer.ShowObjectPaneIfCompact();
        }

        public void Build(DataVisualizer dataVisualizer, ref VisualElement namespaceListContainer)
        {
            HashSet<Type> currentTypes = CollectManagedTypes(_managedTypes);
            if (currentTypes.SetEquals(_namespaceCache.Keys))
            {
                return;
            }

            if (!currentTypes.Contains(_selectedType))
            {
                _selectedType = null;
            }

            namespaceListContainer ??= new VisualElement { name = "namespace-list" };
            namespaceListContainer.Clear();
            VisualElement namespaceContainer = namespaceListContainer;
            _namespaceCache.Clear();
            KeyValuePair<string, List<Type>>[] orderedEntries = OrderedNamespaceEntries(_managedTypes, _namespaceOrder);
            for (int index = 0; index < orderedEntries.Length; index++)
            {
                (string key, List<Type> types) = orderedEntries[index];
                string namespaceKey = key;
                List<Type> nonCoreManagedTypes = new();
                foreach (Type type in types)
                {
                    if (IsTypeRemovable(type))
                    {
                        nonCoreManagedTypes.Add(type);
                    }
                }
                int removableTypeCount = nonCoreManagedTypes.Count;
                bool showNamespaceRemoveButton = 1 < removableTypeCount;

                VisualElement namespaceGroupItem = new() { name = $"namespace-group-{key}", userData = namespaceKey };

                namespaceGroupItem.AddToClassList(StyleConstants.NamespaceItemClass);
                if (types.Count == 0)
                {
                    namespaceGroupItem.AddToClassList(StyleConstants.NamespaceGroupItemEmptyClass);
                }

                namespaceListContainer.Add(namespaceGroupItem);
                namespaceGroupItem.RegisterCallback<PointerDownEvent>(dataVisualizer.OnNamespacePointerDown);

                VisualElement header = new() { name = $"namespace-header-{key}" };
                header.AddToClassList(StyleConstants.NamespaceHeaderClass);
                namespaceGroupItem.Add(header);

                Button indicator = new()
                {
                    text = StyleConstants.ArrowExpanded,
                    name = $"namespace-indicator-{key}",
                    tooltip = $"Collapse or expand {key} (Enter/Space; Left to collapse, Right to expand)",
                };
                indicator.AddToClassList(StyleConstants.NamespaceIndicatorClass);
                indicator.AddToClassList(StyleConstants.ClickableClass);
                header.Add(indicator);

                Button namespaceGoUpButton = new(() =>
                {
                    namespaceContainer.Remove(namespaceGroupItem);
                    namespaceContainer.Insert(0, namespaceGroupItem);
                    foreach (VisualElement child in namespaceContainer.Children())
                    {
                        RecalibrateVisualElements(child);
                    }
                    dataVisualizer.PersistCatalogOrder(namespaceContainer);
                })
                {
                    name = "go-up-button",
                    text = "↑",
                    tooltip = $"Move {namespaceKey} to top",
                };
                if (_managedTypes.Count == 1 || index == 0)
                {
                    namespaceGoUpButton.AddToClassList("go-button-disabled");
                }
                else
                {
                    namespaceGoUpButton.AddToClassList(StyleConstants.ActionButtonClass);
                    namespaceGoUpButton.AddToClassList("go-button");
                }

                header.Add(namespaceGoUpButton);

                Button namespaceGoDownButton = new(() =>
                {
                    namespaceContainer.Remove(namespaceGroupItem);
                    namespaceContainer.Insert(namespaceContainer.childCount, namespaceGroupItem);
                    foreach (VisualElement child in namespaceContainer.Children())
                    {
                        RecalibrateVisualElements(child);
                    }
                    dataVisualizer.PersistCatalogOrder(namespaceContainer);
                })
                {
                    name = "go-down-button",
                    text = "↓",
                    tooltip = $"Move {namespaceKey} to bottom",
                };
                if (_managedTypes.Count == 1 || index == orderedEntries.Length - 1)
                {
                    namespaceGoDownButton.AddToClassList("go-button-disabled");
                }
                else
                {
                    namespaceGoDownButton.AddToClassList(StyleConstants.ActionButtonClass);
                    namespaceGoDownButton.AddToClassList("go-button");
                }

                header.Add(namespaceGoDownButton);

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
                            $"Remove {removableTypeCount} non-core type{(1 < removableTypeCount ? "s" : "")} from namespace '{namespaceKey}'?",
                            "Remove",
                            () =>
                            {
                                HandleRemoveNamespaceTypesConfirmed(dataVisualizer, namespaceKey, nonCoreManagedTypes);
                                if (nonCoreManagedTypes.Contains(SelectedType))
                                {
                                    SelectType(dataVisualizer, null);
                                    dataVisualizer.SelectObject(null);
                                }
                            },
                            namespaceRemoveButton
                        );
                    })
                    {
                        text = "X",
                        tooltip = $"Remove {removableTypeCount} non-BaseDataObject type{(1 < removableTypeCount ? "s" : "")}",
                    };
                    namespaceRemoveButton.AddToClassList(StyleConstants.ActionButtonClass);
                    namespaceRemoveButton.AddToClassList(StyleConstants.DeleteButtonClass);
                    namespaceRemoveButton.AddToClassList(StyleConstants.NamespaceDeleteButton);
                    headerRight.Add(namespaceRemoveButton);
                }

                VisualElement typesContainer = new() { name = $"types-container-{key}", userData = namespaceKey };
                typesContainer.AddToClassList(StyleConstants.TypesContainerClass);
                namespaceGroupItem.Add(typesContainer);

                bool isCollapsed = GetIsNamespaceCollapsed(dataVisualizer, namespaceKey);
                ApplyNamespaceCollapsedState(dataVisualizer, indicator, typesContainer, isCollapsed, false);

                indicator.clicked += ToggleNamespace;
                indicator.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
                indicator.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (dataVisualizer.IsAssetEditingSuspended || (evt.keyCode != KeyCode.LeftArrow && evt.keyCode != KeyCode.RightArrow))
                    {
                        return;
                    }
                    ApplyNamespaceCollapsedState(dataVisualizer, indicator, typesContainer, evt.keyCode == KeyCode.LeftArrow, true);
                    evt.StopPropagation();
                    evt.PreventDefaultCompat();
                });

                // ReSharper disable once ForCanBeConvertedToForeach
                for (int i = 0; i < types.Count; i++)
                {
                    Type type = types[i];
                    bool isRemovableType = IsTypeRemovable(type);

                    VisualElement typeItem = new()
                    {
                        name = $"type-item-{type.Name}",
                        userData = type,
                        pickingMode = PickingMode.Position,
                        focusable = true,
                    };
                    typeItem.AddToClassList(StyleConstants.TypeItemClass);
                    _namespaceCache[type] = typeItem;

                    Button goUpButton = new(() =>
                    {
                        typesContainer.Remove(typeItem);
                        typesContainer.Insert(0, typeItem);
                        foreach (VisualElement child in typesContainer.Children())
                        {
                            RecalibrateVisualElements(child);
                        }
                        dataVisualizer.PersistCatalogOrder(typesContainer);
                    })
                    {
                        name = "go-up-button",
                        text = "↑",
                        tooltip = $"Move {GetTypeDisplayName(type)} to top",
                    };
                    if (types.Count == 1 || i == 0)
                    {
                        goUpButton.AddToClassList("go-button-disabled");
                    }
                    else
                    {
                        goUpButton.AddToClassList(StyleConstants.ActionButtonClass);
                        goUpButton.AddToClassList("go-button");
                    }

                    typeItem.Add(goUpButton);

                    Button goDownButton = new(() =>
                    {
                        typesContainer.Remove(typeItem);
                        typesContainer.Insert(typesContainer.childCount, typeItem);
                        foreach (VisualElement child in typesContainer.Children())
                        {
                            RecalibrateVisualElements(child);
                        }
                        dataVisualizer.PersistCatalogOrder(typesContainer);
                    })
                    {
                        name = "go-down-button",
                        text = "↓",
                        tooltip = $"Move {GetTypeDisplayName(type)} to bottom",
                    };
                    if (types.Count == 1 || i == types.Count - 1)
                    {
                        goDownButton.AddToClassList("go-button-disabled");
                    }
                    else
                    {
                        goDownButton.AddToClassList(StyleConstants.ActionButtonClass);
                        goDownButton.AddToClassList("go-button");
                    }

                    typeItem.Add(goDownButton);

                    Label typeLabel = new(GetTypeDisplayName(type)) { name = TypeItemLabelName };
                    typeLabel.AddToClassList(StyleConstants.TypeLabelClass);
                    typeLabel.AddToClassList(StyleConstants.ClickableClass);
                    typeItem.Add(typeLabel);

                    // ReSharper disable once HeapView.CanAvoidClosure
                    typeItem.RegisterCallback<PointerDownEvent>(evt => dataVisualizer.OnTypePointerDown(namespaceGroupItem, evt));
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
                                $"Remove type '<color=yellow><i>{type.Name}</i></color>' from DxVisualizer?",
                                "Remove",
                                () =>
                                {
                                    HandleRemoveTypeConfirmed(dataVisualizer, type);
                                    if (type == SelectedType)
                                    {
                                        SelectType(dataVisualizer, null);
                                        dataVisualizer.SelectObject(null);
                                    }
                                },
                                typeRemoveButton
                            );
                        })
                        {
                            text = "X",
                            tooltip = $"Remove {GetTypeDisplayName(type)}",
                        };
                        typeRemoveButton.AddToClassList(StyleConstants.ActionButtonClass);
                        typeRemoveButton.AddToClassList(StyleConstants.DeleteButtonClass);
                        typeItem.Add(typeRemoveButton);
                    }

                    typesContainer.Add(typeItem);
                }

                continue;

                void ToggleNamespace()
                {
                    if (dataVisualizer.IsAssetEditingSuspended)
                    {
                        return;
                    }
                    bool collapsed = typesContainer.style.display == DisplayStyle.None;
                    ApplyNamespaceCollapsedState(dataVisualizer, indicator, typesContainer, !collapsed, true);
                }
            }
        }

        internal List<string> GetManagedTypeNames(string namespaceKey)
        {
            List<string> typeNames = new();
            if (!_managedTypes.TryGetValue(namespaceKey, out List<Type> types))
            {
                return typeNames;
            }

            foreach (Type type in types)
            {
                typeNames.Add(type.FullName);
            }
            return typeNames;
        }

        internal List<string> GetAllManagedTypeNames()
        {
            List<string> typeNames = new();
            foreach (List<Type> types in _managedTypes.Values)
            {
                foreach (Type type in types)
                {
                    typeNames.Add(type.FullName);
                }
            }
            return typeNames;
        }

        private void NavigateTypeSelection(DataVisualizer dataVisualizer, bool forward)
        {
            if (dataVisualizer.IsAssetEditingSuspended || !TryGet(_selectedType, out VisualElement current))
            {
                return;
            }
            Type next = GetAdjacentVisibleType(current, forward);
            if (next != null)
            {
                SelectType(dataVisualizer, next);
                ScrollView scroll = current.GetFirstAncestorOfType<ScrollView>();
                if (scroll?.panel != null)
                {
                    scroll.ScrollTo(_namespaceCache[next]);
                }
            }
        }

        private bool TryGet(Type type, out VisualElement element)
        {
            if (type != null && _namespaceCache.TryGetValue(type, out VisualElement cachedElement))
            {
                element = cachedElement;
                return true;
            }

            element = default;
            return false;
        }

        private void HandleRemoveNamespaceTypesConfirmed(DataVisualizer dataVisualizer, string namespaceKey, List<Type> typesToRemove)
        {
            if (typesToRemove == null || typesToRemove.Count == 0)
            {
                return;
            }

            HashSet<string> currentManagedTypeNames = new(GetManagedTypeNames(namespaceKey), StringComparer.Ordinal);
            List<string> removedTypeNames = new();
            bool changed = false;
            foreach (Type type in typesToRemove)
            {
                string typeName = type.FullName;
                if (!IsTypeRemovable(type) || !currentManagedTypeNames.Remove(typeName))
                {
                    continue;
                }

                changed = true;
                removedTypeNames.Add(typeName);
                dataVisualizer.SetLastSelectedObjectGuidForType(typeName, null);
                RemoveTypeOrderEntry(dataVisualizer, namespaceKey, typeName);
            }

            if (changed)
            {
                if (currentManagedTypeNames.Count == 0)
                {
                    RemoveNamespaceCollapseState(dataVisualizer, namespaceKey);
                }

                PersistManagedTypesList(dataVisualizer, RemoveManagedTypeNames(GetAllManagedTypeNames(), removedTypeNames));
                DataVisualizer.SignalRefresh();
            }
            /*
                A confirm whose targets already left the managed list (stale popover after a
                refresh) is an idempotent no-op: the requested end state already exists, so drop
                silently instead of warning.
            */
        }

        private void HandleRemoveTypeConfirmed(DataVisualizer dataVisualizer, Type typeToRemove)
        {
            if (!IsTypeRemovable(typeToRemove))
            {
                Debug.LogWarning($"Attempted to remove BaseDataObject derivative '{typeToRemove?.FullName}' or null type.");
                return;
            }

            string namespaceKey = GetNamespaceKey(typeToRemove);
            List<string> currentManagedList = GetManagedTypeNames(namespaceKey);
            string typeName = typeToRemove.FullName;
            if (currentManagedList.Remove(typeName))
            {
                dataVisualizer.SetLastSelectedObjectGuidForType(typeName, null);
                RemoveTypeOrderEntry(dataVisualizer, namespaceKey, typeName);
                if (currentManagedList.Count == 0)
                {
                    RemoveNamespaceCollapseState(dataVisualizer, namespaceKey);
                }

                PersistManagedTypesList(dataVisualizer, RemoveManagedTypeNames(GetAllManagedTypeNames(), new[] { typeName }));
                DataVisualizer.SignalRefresh();
            }
            /*
                A confirm for a type that already left the managed list (stale popover after a
                refresh) is an idempotent no-op: drop silently instead of warning.
            */
        }
    }
}
