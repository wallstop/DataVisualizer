namespace WallstopStudios.DataVisualizer.Editor.State
{
    using System;
    using System.Collections.Generic;

    public sealed class VisualizerSessionState
    {
        public VisualizerSessionState()
        {
            Selection = new SelectionState();
            Pagination = new PaginationState();
            Search = new SearchState();
            Popovers = new PopoverState();
            Labels = new LabelFilterState();
        }

        public SelectionState Selection { get; }

        public PaginationState Pagination { get; }

        public SearchState Search { get; }

        public PopoverState Popovers { get; }

        public LabelFilterState Labels { get; }

        public sealed class SelectionState
        {
            private readonly List<string> _selectedObjectGuids = new List<string>();
            private readonly HashSet<string> _collapsedNamespaces = new HashSet<string>(
                StringComparer.Ordinal
            );

            public string SelectedNamespaceKey { get; private set; }

            public string SelectedTypeFullName { get; private set; }

            public string PrimarySelectedObjectGuid { get; private set; }

            public IReadOnlyList<string> SelectedObjectGuids => _selectedObjectGuids;

            public IReadOnlyCollection<string> CollapsedNamespaces => _collapsedNamespaces;

            public bool SetSelectedNamespace(string namespaceKey)
            {
                if (string.Equals(SelectedNamespaceKey, namespaceKey, StringComparison.Ordinal))
                {
                    return false;
                }

                SelectedNamespaceKey = namespaceKey;
                return true;
            }

            public bool SetSelectedType(string typeFullName)
            {
                if (string.Equals(SelectedTypeFullName, typeFullName, StringComparison.Ordinal))
                {
                    return false;
                }

                SelectedTypeFullName = typeFullName;
                return true;
            }

            public bool SetPrimarySelectedObject(string objectGuid)
            {
                if (string.Equals(PrimarySelectedObjectGuid, objectGuid, StringComparison.Ordinal))
                {
                    return false;
                }

                PrimarySelectedObjectGuid = objectGuid;
                return true;
            }

            public bool SetSelectedObjects(IEnumerable<string> objectGuids)
            {
                if (objectGuids == null)
                {
                    _selectedObjectGuids.Clear();
                    return true;
                }

                bool changed = false;
                int index = 0;
                foreach (string guid in objectGuids)
                {
                    if (string.IsNullOrWhiteSpace(guid))
                    {
                        continue;
                    }

                    if (_selectedObjectGuids.Count <= index)
                    {
                        _selectedObjectGuids.Add(guid);
                        changed = true;
                    }
                    else if (
                        !string.Equals(_selectedObjectGuids[index], guid, StringComparison.Ordinal)
                    )
                    {
                        _selectedObjectGuids[index] = guid;
                        changed = true;
                    }

                    index++;
                }

                if (_selectedObjectGuids.Count > index)
                {
                    _selectedObjectGuids.RemoveRange(index, _selectedObjectGuids.Count - index);
                    changed = true;
                }

                return changed;
            }

            public bool SetNamespaceCollapsed(string namespaceKey, bool isCollapsed)
            {
                if (string.IsNullOrWhiteSpace(namespaceKey))
                {
                    return false;
                }

                if (isCollapsed)
                {
                    if (_collapsedNamespaces.Contains(namespaceKey))
                    {
                        return false;
                    }

                    _collapsedNamespaces.Add(namespaceKey);
                    return true;
                }

                if (_collapsedNamespaces.Remove(namespaceKey))
                {
                    return true;
                }

                return false;
            }
        }

        public sealed class PaginationState
        {
            public int CurrentPage { get; private set; }

            public int ItemsPerPage { get; private set; }

            public int TotalItems { get; private set; }

            public bool SetCurrentPage(int pageIndex)
            {
                if (pageIndex < 0)
                {
                    pageIndex = 0;
                }

                if (CurrentPage == pageIndex)
                {
                    return false;
                }

                CurrentPage = pageIndex;
                return true;
            }

            public bool SetItemsPerPage(int itemsPerPage)
            {
                if (itemsPerPage < 1)
                {
                    itemsPerPage = 1;
                }

                if (ItemsPerPage == itemsPerPage)
                {
                    return false;
                }

                ItemsPerPage = itemsPerPage;
                return true;
            }

            public bool SetTotalItems(int totalItems)
            {
                if (totalItems < 0)
                {
                    totalItems = 0;
                }

                if (TotalItems == totalItems)
                {
                    return false;
                }

                TotalItems = totalItems;
                return true;
            }
        }

        public sealed class SearchState
        {
            public string CurrentQuery { get; private set; }

            public int HighlightIndex { get; private set; } = -1;

            public bool SetQuery(string query)
            {
                if (string.Equals(CurrentQuery, query, StringComparison.Ordinal))
                {
                    return false;
                }

                CurrentQuery = query;
                return true;
            }

            public bool SetHighlightIndex(int index)
            {
                if (HighlightIndex == index)
                {
                    return false;
                }

                HighlightIndex = index;
                return true;
            }
        }

        public sealed class PopoverState
        {
            public string ActivePopoverId { get; private set; }

            public string ActiveNestedPopoverId { get; private set; }

            public bool SetActivePopover(string popoverId)
            {
                if (string.Equals(ActivePopoverId, popoverId, StringComparison.Ordinal))
                {
                    return false;
                }

                ActivePopoverId = popoverId;
                if (!string.Equals(popoverId, string.Empty, StringComparison.Ordinal))
                {
                    ActiveNestedPopoverId = null;
                }

                return true;
            }

            public bool SetActiveNestedPopover(string popoverId)
            {
                if (string.Equals(ActiveNestedPopoverId, popoverId, StringComparison.Ordinal))
                {
                    return false;
                }

                ActiveNestedPopoverId = popoverId;
                return true;
            }

            public void Clear()
            {
                ActivePopoverId = null;
                ActiveNestedPopoverId = null;
            }
        }

        public sealed class LabelFilterState
        {
            private readonly List<string> _andLabels = new List<string>();
            private readonly List<string> _orLabels = new List<string>();

            public IReadOnlyList<string> AndLabels => _andLabels;

            public IReadOnlyList<string> OrLabels => _orLabels;

            public bool SetAndLabels(IEnumerable<string> labels)
            {
                return SetLabelsInternal(_andLabels, labels);
            }

            public bool SetOrLabels(IEnumerable<string> labels)
            {
                return SetLabelsInternal(_orLabels, labels);
            }

            private static bool SetLabelsInternal(List<string> target, IEnumerable<string> source)
            {
                if (target == null)
                {
                    throw new ArgumentNullException(nameof(target));
                }

                List<string> normalized = new List<string>();
                if (source != null)
                {
                    foreach (string label in source)
                    {
                        if (string.IsNullOrWhiteSpace(label))
                        {
                            continue;
                        }

                        normalized.Add(label);
                    }
                }

                if (normalized.Count == target.Count)
                {
                    bool sequencesEqual = true;
                    for (int index = 0; index < target.Count; index++)
                    {
                        if (
                            !string.Equals(
                                target[index],
                                normalized[index],
                                StringComparison.Ordinal
                            )
                        )
                        {
                            sequencesEqual = false;
                            break;
                        }
                    }

                    if (sequencesEqual)
                    {
                        return false;
                    }
                }
                else if (normalized.Count == 0 && target.Count == 0)
                {
                    return false;
                }

                target.Clear();
                target.AddRange(normalized);
                return true;
            }
        }
    }
}
