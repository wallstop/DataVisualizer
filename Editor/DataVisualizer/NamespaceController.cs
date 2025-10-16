// ReSharper disable AccessToModifiedClosure
namespace WallstopStudios.DataVisualizer.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Data;
    using Helper;
    using State;
    using Styles;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed class NamespaceController
    {
        internal const string TypeItemLabelName = "type-item-label";

        public Type SelectedType => _selectedType;

        public event Action<Type> TypeSelected;

        internal readonly Dictionary<Type, VisualElement> _namespaceCache = new();

        internal readonly Dictionary<string, List<Type>> _managedTypes;
        internal readonly Dictionary<string, int> _namespaceOrder;
        internal Type _selectedType;

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

        internal bool InternalDeselectAndGetCurrentIndex(
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

        internal static Type InternalSelected(VisualElement parent, int index)
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
                return;
            }

            _selectedType = type;
            element.AddToClassList(StyleConstants.SelectedClass);
            TypeSelected?.Invoke(_selectedType);
            element.Q<Label>(TypeItemLabelName)?.RemoveFromClassList(StyleConstants.ClickableClass);
            if (TryGetNamespace(element, out VisualElement newlySelectedNamespace))
            {
                newlySelectedNamespace.AddToClassList(StyleConstants.SelectedClass);
            }

            string namespaceKey = GetNamespaceKey(_selectedType);
        }

        public void PerformTypeSearch(string searchText)
        {
            if (_namespaceCache.Count == 0 || string.IsNullOrWhiteSpace(searchText))
            {
                foreach (VisualElement typeItem in _namespaceCache.Values)
                {
                    typeItem.style.display = DisplayStyle.Flex;
                }

                UpdateNamespaceVisibility();
                return;
            }

            string[] terms = searchText
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();

            foreach (KeyValuePair<Type, VisualElement> entry in _namespaceCache)
            {
                Type type = entry.Key;
                VisualElement element = entry.Value;
                string displayName = GetTypeDisplayName(type);
                bool shouldDisplay = terms.All(term =>
                    displayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                );
                element.style.display = shouldDisplay ? DisplayStyle.Flex : DisplayStyle.None;
            }

            UpdateNamespaceVisibility();
        }

        private void UpdateNamespaceVisibility()
        {
            HashSet<VisualElement> namespaceContainers = _namespaceCache
                .Values
                .Select(element => element.parent?.parent)
                .Where(element => element != null)
                .ToHashSet();

            int hiddenNamespaces = 0;
            foreach (VisualElement container in namespaceContainers)
            {
                bool allHidden = container
                    .Query<VisualElement>(className: "type-item")
                    .ToList()
                    .All(item => item.style.display == DisplayStyle.None);
                container.style.display = allHidden ? DisplayStyle.None : DisplayStyle.Flex;
                if (allHidden)
                {
                    hiddenNamespaces++;
                }
            }

            // just to keep parity with previous behavior; DataVisualizer listens to this value
            _namespaceCache.TryGetValue(_selectedType, out VisualElement selectedElement);
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
                goUpButton.EnableInClassList(
                    StyleConstants.ActionButtonClass,
                    compensatedIndex != 0
                );
                goUpButton.EnableInClassList("go-button", compensatedIndex != 0);
            }
            Button goDownButton = item.Q<Button>("go-down-button");
            if (goDownButton != null)
            {
                goDownButton.EnableInClassList(
                    "go-button-disabled",
                    index == parent.childCount - 1
                );
                goDownButton.EnableInClassList(
                    StyleConstants.ActionButtonClass,
                    index != parent.childCount - 1
                );
                goDownButton.EnableInClassList("go-button", index != parent.childCount - 1);
            }
        }

        public void Build(DataVisualizer dataVisualizer, ref VisualElement namespaceListContainer)
        {
            HashSet<Type> currentTypes = _managedTypes.SelectMany(x => x.Value).ToHashSet();
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
            KeyValuePair<string, List<Type>>[] orderedEntries = _managedTypes
                .OrderBy(kvp => _namespaceOrder.GetValueOrDefault(kvp.Key, _namespaceOrder.Count))
                .ToArray();
            for (int index = 0; index < orderedEntries.Length; index++)
            {
                (string key, List<Type> types) = orderedEntries[index];
                string namespaceKey = key;
                List<Type> nonCoreManagedTypes = types.Where(IsTypeRemovable).ToList();
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

                Button namespaceGoUpButton = new(() =>
                {
                    namespaceContainer.Remove(namespaceGroupItem);
                    namespaceContainer.Insert(0, namespaceGroupItem);
                    foreach (VisualElement child in namespaceContainer.Children())
                    {
                        RecalibrateVisualElements(child);
                    }
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
                    Button namespaceRemoveButton = new Button
                    {
                        text = "X",
                        tooltip =
                            $"Remove {removableTypeCount} non-BaseDataObject type{(removableTypeCount > 1 ? "s" : string.Empty)}",
                        userData = namespaceKey,
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

                    if (type == _selectedType)
                    {
                        namespaceGroupItem.AddToClassList(StyleConstants.SelectedClass);
                        typeItem.AddToClassList(StyleConstants.SelectedClass);
                        typeLabel.RemoveFromClassList(StyleConstants.ClickableClass);
                    }

                    if (isRemovableType)
                    {
                        Button typeRemoveButton = new Button
                        {
                            text = "X",
                            tooltip = $"Remove {GetTypeDisplayName(type)}",
                            userData = type,
                        };
                        typeRemoveButton.AddToClassList(StyleConstants.ActionButtonClass);
                        typeRemoveButton.AddToClassList(StyleConstants.DeleteButtonClass);
                        typeItem.Add(typeRemoveButton);
                    }

                    typesContainer.Add(typeItem);
                }

                continue;
            }
        }

        internal bool TryGet(Type type, out VisualElement element)
        {
            if (type != null)
            {
                return _namespaceCache.TryGetValue(type, out element);
            }

            element = default;
            return false;
        }

        internal static bool TryGetNamespace(
            VisualElement typeElement,
            out VisualElement namespaceElement
        )
        {
            namespaceElement = typeElement?.parent?.parent;
            return namespaceElement != null;
        }

        internal static void ApplyNamespaceCollapsedState(
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

            string namespaceKey = typesContainer.parent?.userData as string;
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return;
            }

            dataVisualizer.SessionState.Selection.SetNamespaceCollapsed(namespaceKey, collapsed);

            if (!saveState)
            {
                return;
            }
            SetIsNamespaceCollapsed(dataVisualizer, namespaceKey, collapsed);
        }

        internal static void SetIsNamespaceCollapsed(
            DataVisualizer dataVisualizer,
            string namespaceKey,
            bool isCollapsed
        )
        {
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return;
            }

            dataVisualizer.SessionState.Selection.SetNamespaceCollapsed(namespaceKey, isCollapsed);
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

        internal static bool GetIsNamespaceCollapsed(
            DataVisualizer dataVisualizer,
            string namespaceKey
        )
        {
            if (string.IsNullOrWhiteSpace(namespaceKey))
            {
                return false;
            }

            VisualizerSessionState.SelectionState selection = dataVisualizer
                .SessionState
                ?.Selection;
            if (selection != null && selection.CollapsedNamespaces.Contains(namespaceKey))
            {
                return true;
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

        internal static void PersistManagedTypesList(
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

        internal static bool IsTypeRemovable(Type type)
        {
            return type == null
                || (
                    !typeof(BaseDataObject).IsAssignableFrom(type)
                    && !type.IsAttributeDefined<CustomDataVisualizationAttribute>()
                );
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
                type.IsAttributeDefined(
                    out CustomDataVisualizationAttribute attribute,
                    inherit: false
                ) && !string.IsNullOrWhiteSpace(attribute.TypeName)
            )
            {
                return attribute.TypeName;
            }

            return type.Name;
        }
    }
}
