namespace WallstopStudios.DataVisualizer.Editor
{
    using System;
    using System.Collections.Generic;
    using Data;
    using Extensions;
    using Styles;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Utilities;

    public sealed class SavedAssetSearchPanel : AssetToolPanel
    {
        private IVisualElementScheduledItem _updates;
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
        private Button _rename;
        private Button _delete;
        private Button _apply;
        private Button _previous;
        private Button _next;
        private Button _cancel;

        public SavedAssetSearchPanel(DataVisualizer owner, SavedAssetSearch draft)
            : base("Saved searches", "Keep useful searches with their exact type and labels. Applying a search only reads assets.", owner.CloseAssetTool)
        {
            name = "saved-search-workspace";
            _owner = owner;
            _draft = draft.Clone();
            _saved = owner.CopySavedSearches();
            BuildContent();
            _updates = schedule.Execute(AdvanceQuery).Every(100);
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

        public override void Dispose()
        {
            _updates?.Pause();
            _query?.Dispose();
            _query = null;
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
            _selected = _saved.Count - 1;
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
                AdvanceQuery();
            }
            catch (Exception error)
            {
                _message.text = error.Message;
            }
        }

        public void AdvanceQuery()
        {
            if (_query == null || _status == null)
            {
                return;
            }
            _query.Advance();
            UpdateActions();
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

        private void BuildContent()
        {
            ScrollView form = new();
            form.AddToClassList(StyleConstants.AssetToolFormClass);
            Add(form);
            _searches = new DropdownField("Saved search", new List<string>(), -1) { name = "saved-search-list" };
            _searches.RegisterValueChangedCallback(_ => SelectSearch(_searches.index));
            form.Add(_searches);
            _name = new TextField("Name") { name = "saved-search-name" };
            form.Add(_name);
            _scope = new Label { name = "saved-search-scope", style = { whiteSpace = WhiteSpace.Normal } };
            _scope.AddToClassList(StyleConstants.AssetToolScopeClass);
            form.Add(_scope);
            VisualElement actions = ActionRow(form);
            AddAction(actions, "Save current search", "Create a named snapshot captured when this workspace opened", SaveCurrent, true);
            _rename = AddAction(actions, "Rename", "Rename the selected saved search", RenameSelected);
            _delete = AddAction(actions, "Delete", "Remove the selected saved search; assets are unchanged", DeleteSelected);
            _apply = AddAction(actions, "Apply", "Query the saved type, text and labels", ApplySelected, true);
            _status = new Label { name = "saved-search-status", style = { whiteSpace = WhiteSpace.Normal } };
            Add(_status);
            _message = new Label { name = "saved-search-message", style = { whiteSpace = WhiteSpace.Normal } };
            form.Add(_message);
            _message.AddToClassList(StyleConstants.AssetToolMessageClass);
            _resultScope = new Label { name = "saved-results-scope", style = { whiteSpace = WhiteSpace.Normal } };
            Add(_resultScope);
            _results = new ListView
            {
                itemsSource = _rows,
                fixedItemHeight = 42,
                bindItem = (element, index) =>
                {
                    EditorAssetRecord item = _rows[index];
                    element.Q<Label>("asset-result-name").text = item.Name;
                    element.Q<Label>("asset-result-path").text = item.Path;
                    element.tooltip = item.Path + "\n" + (item.Error ?? item.Guid);
                    element.userData = item;
                },
                style = { flexGrow = 1 },
            };
            _results.AddToClassList(StyleConstants.AssetToolResultsClass);
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
                row.AddToClassList(StyleConstants.AssetToolResultClass);
                row.Add(new Label { name = "asset-result-name" });
                Label path = new() { name = "asset-result-path" };
                path.AddToClassList(StyleConstants.AssetToolSummaryClass);
                row.Add(path);
                return row;
            };
            Add(_results);
            VisualElement paging = ActionRow(this);
            _previous = AddAction(paging, "Previous", "Show the previous 1,000 results", () => ChangePage(-1000));
            _next = AddAction(paging, "Next", "Show the next 1,000 results", () => ChangePage(1000));
            _cancel = AddAction(paging, "Cancel search", "Keep partial results and stop indexing", CancelQuery);
            RefreshChoices();
            UpdateActions();
            _scope.text = "Current snapshot\nQuery: " + _draft.query + "\nType: " + _draft.typeIdentity?.Split(',')[0] + "\nLabels: " + DescribeLabels(_draft.labels);
        }

        private void SelectSearch(int index)
        {
            _selected = index;
            UpdateActions();
            if (index < 0)
            {
                _name.SetValueWithoutNotify(string.Empty);
                _scope.text = "Current snapshot\nQuery: " + _draft.query + "\nType: " + _draft.typeIdentity?.Split(',')[0] + "\nLabels: " + DescribeLabels(_draft.labels);
                return;
            }
            SavedAssetSearch entry = _saved[index];
            _name.value = entry.name;
            _scope.text = "Query: " + entry.query + "\nType: " + entry.typeIdentity?.Split(',')[0] + "\nLabels: " + DescribeLabels(entry.labels);
        }

        private bool ValidateName(bool renaming)
        {
            if (_owner == null)
            {
                _message.text = "Reopen Saved searches from DxVisualizer before saving.";
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
                _message.text = "Reopen Saved searches from DxVisualizer before saving.";
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
            UpdateActions();
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
            AdvanceQuery();
        }

        private void CancelQuery()
        {
            _query?.Dispose();
            AdvanceQuery();
        }

        private void UpdateActions()
        {
            bool selected = 0 <= _selected && _selected < _saved.Count;
            _rename.SetEnabled(selected);
            _delete.SetEnabled(selected);
            _apply.SetEnabled(selected);
            _previous.SetEnabled(_query != null && 0 < _offset);
            _next.SetEnabled(_query != null && _offset + 1000 < _query.ResultCount);
            _cancel.SetEnabled(_query != null && !_query.IsComplete && !_query.IsCancelled);
        }
    }
}
