namespace WallstopStudios.DataVisualizer.Editor
{
    using System;
    using System.Collections.Generic;
    using Data;
    using Extensions;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Utilities;

    public sealed class SavedAssetSearchWindow : EditorWindow
    {
        private DataVisualizer _owner;
        private SavedAssetSearch _draft;
        private List<SavedAssetSearch> _saved;
        private EditorAssetQuery _query;
        private DropdownField _searches;
        private TextField _name;
        private Label _scope;
        private Label _status;
        private Label _message;
        private Label _resultScope;
        private ListView _results;
        private readonly List<EditorAssetRecord> _rows = new();
        private int _selected = -1;
        private int _offset;

        public static SavedAssetSearchWindow Open(DataVisualizer owner, SavedAssetSearch draft)
        {
            SavedAssetSearchWindow window = GetWindow<SavedAssetSearchWindow>("Saved searches");
            window._query?.Dispose();
            window._query = null;
            window._rows.Clear();
            window._selected = -1;
            window._offset = 0;
            window._owner = owner;
            window._draft = draft.Clone();
            window._saved = owner.CopySavedSearches();
            window.CreateGUI();
            window.Show();
            return window;
        }

        private static string DescribeLabels(TypeLabelFilterConfig labels)
        {
            string all = labels?.andLabels is { Count: > 0 } ? "All of [" + string.Join(", ", labels.andLabels) + "]" : null;
            string any = labels?.orLabels is { Count: > 0 } ? "Any of [" + string.Join(", ", labels.orLabels) + "]" : null;
            if (all != null && any != null)
            {
                return "(" + all + ") " + (labels.combinationType == LabelCombinationType.Or ? "OR" : "AND") + " (" + any + ")";
            }
            return all ?? any ?? "All assets (no label filter)";
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 6;
            rootVisualElement.style.paddingRight = 6;
            if (_owner == null || _draft == null)
            {
                rootVisualElement.Add(new Label("Open Saved searches from DxVisualizer to restore the current persistence mode."));
                return;
            }
            rootVisualElement.Add(
                new Label("Save the current query, selected type and label expression. Assets are never changed.") { style = { whiteSpace = WhiteSpace.Normal } }
            );
            _searches = new DropdownField("Saved search", new List<string>(), -1) { name = "saved-search-list" };
            _searches.RegisterValueChangedCallback(_ => SelectSearch(_searches.index));
            rootVisualElement.Add(_searches);
            _name = new TextField("Name") { name = "saved-search-name" };
            rootVisualElement.Add(_name);
            _scope = new Label { name = "saved-search-scope", style = { whiteSpace = WhiteSpace.Normal } };
            rootVisualElement.Add(_scope);
            rootVisualElement.Add(new Button(SaveCurrent) { text = "Save current search", tooltip = "Create a named snapshot of the search captured when this window opened" });
            rootVisualElement.Add(new Button(RenameSelected) { text = "Rename", tooltip = "Rename the chosen saved search without replacing its scope" });
            rootVisualElement.Add(new Button(DeleteSelected) { text = "Delete", tooltip = "Remove the chosen saved search; assets are unchanged" });
            rootVisualElement.Add(new Button(ApplySelected) { text = "Apply", tooltip = "Query the exact saved type, text and labels" });
            rootVisualElement.Add(new Button(CancelQuery) { text = "Cancel", tooltip = "Keep partial results and stop indexing" });
            _status = new Label { name = "saved-search-status", style = { whiteSpace = WhiteSpace.Normal } };
            rootVisualElement.Add(_status);
            _message = new Label { name = "saved-search-message", style = { whiteSpace = WhiteSpace.Normal } };
            rootVisualElement.Add(_message);
            _resultScope = new Label { name = "saved-results-scope", style = { whiteSpace = WhiteSpace.Normal } };
            rootVisualElement.Add(_resultScope);
            _results = new ListView
            {
                itemsSource = _rows,
                fixedItemHeight = 42,
                bindItem = (element, index) =>
                {
                    EditorAssetRecord item = _rows[index];
                    ((Button)element).text = item.Name + "\n" + item.Path;
                    element.tooltip = item.Error ?? item.Guid;
                    element.userData = item;
                },
                style = { flexGrow = 1 },
            };
            _results.makeItem = () =>
            {
                Button row = null;
                row = new Button(() =>
                {
                    if (row.userData is EditorAssetRecord record)
                    {
                        UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(record.Guid));
                        if (asset != null)
                        {
                            Selection.activeObject = asset;
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                });
                return row;
            };
            rootVisualElement.Add(_results);
            rootVisualElement.Add(new Button(() => ChangePage(-1000)) { text = "Previous results" });
            rootVisualElement.Add(new Button(() => ChangePage(1000)) { text = "Next results" });
            RefreshChoices();
            _scope.text = "Current snapshot: " + _draft.query + "\n" + _draft.typeIdentity?.Split(',')[0] + "\n" + DescribeLabels(_draft.labels);
        }

        public void SaveCurrent()
        {
            if (!ValidateName(false))
            {
                return;
            }
            try
            {
                using EditorAssetQuery check = _draft.StartQuery();
            }
            catch (Exception error)
            {
                _message.text = error.Message;
                return;
            }
            SavedAssetSearch entry = _draft.Clone();
            entry.name = _name.value.Trim();
            _saved.Add(entry);
            Persist();
        }

        public void RenameSelected()
        {
            if (_selected < 0 || !ValidateName(true))
            {
                return;
            }
            _saved[_selected].name = _name.value.Trim();
            Persist();
        }

        public void DeleteSelected()
        {
            if (_selected < 0)
            {
                return;
            }
            _saved.RemoveAt(_selected);
            _selected = -1;
            Persist();
        }

        public void ApplySelected()
        {
            if (_selected < 0)
            {
                return;
            }
            try
            {
                EditorAssetQuery next = _saved[_selected].StartQuery();
                _query?.Dispose();
                _message.text = string.Empty;
                _resultScope.text = "Results for: " + _saved[_selected].name;
                _query = next;
                _offset = 0;
                _rows.Clear();
                _results.RefreshItems();
                UpdateQuery();
            }
            catch (Exception error)
            {
                _message.text = error.Message;
            }
        }

        private void SelectSearch(int index)
        {
            _selected = index;
            if (index < 0)
            {
                return;
            }
            SavedAssetSearch entry = _saved[index];
            _name.value = entry.name;
            _scope.text = entry.query + "\n" + entry.typeIdentity?.Split(',')[0] + "\n" + DescribeLabels(entry.labels);
        }

        private bool ValidateName(bool renaming)
        {
            if (_owner == null)
            {
                _message.text = "Reopen this window from DxVisualizer before saving.";
                return false;
            }
            string candidate = _name.value.Trim();
            if (candidate.Length == 0)
            {
                _message.text = "Enter a name.";
                return false;
            }
            for (int i = 0; i < _saved.Count; i++)
            {
                if ((!renaming || i != _selected) && string.Equals(_saved[i].name, candidate, StringComparison.Ordinal))
                {
                    _message.text = "Use a different name.";
                    return false;
                }
            }
            return true;
        }

        private void Persist()
        {
            if (_owner == null)
            {
                _message.text = "Reopen this window from DxVisualizer before saving.";
                return;
            }
            _owner.SaveSearches(_saved);
            _message.text = string.Empty;
            RefreshChoices();
            _status.text = "Saved in the current DxVisualizer persistence mode.";
        }

        private void RefreshChoices()
        {
            List<string> choices = new();
            foreach (SavedAssetSearch entry in _saved)
            {
                choices.Add(entry.name);
            }
            _searches.choices = choices;
            _searches.index = _selected < choices.Count ? _selected : -1;
        }

        private void ChangePage(int delta)
        {
            int next = _offset + delta;
            if (_query == null || next < 0 || _query.ResultCount <= next)
            {
                return;
            }
            _offset = next;
            _rows.Clear();
            UpdateQuery();
        }

        private void CancelQuery()
        {
            _query?.Dispose();
            UpdateQuery();
        }

        private void OnInspectorUpdate()
        {
            UpdateQuery();
        }

        private void OnDisable()
        {
            _query?.Dispose();
            _query = null;
        }

        private void UpdateQuery()
        {
            if (_query == null || _status == null)
            {
                return;
            }
            _query.Advance();
            _status.text =
                (
                    _query.IsComplete ? "Complete"
                    : _query.IsCancelled ? "Cancelled (partial)"
                    : _query.IsWaitingForEditor ? "Waiting for editor (partial)"
                    : "Indexing (partial)"
                )
                + ": "
                + _query.ResultCount
                + " matches. Page "
                + (_offset / 1000 + 1)
                + ".";
            if (_rows.Count != Math.Min(Math.Max(0, _query.ResultCount - _offset), 1000))
            {
                _rows.Clear();
                _rows.AddRange(_query.GetPage(_offset, 1000));
                _results.RefreshItems();
            }
        }
    }
}
