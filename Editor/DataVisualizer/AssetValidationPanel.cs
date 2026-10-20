namespace WallstopStudios.DataVisualizer.Editor
{
    using System.Collections.Generic;
    using Extensions;
    using Styles;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Utilities;

    public sealed class AssetValidationPanel : AssetToolPanel
    {
        private IVisualElementScheduledItem _updates;
        private EditorAssetValidation _validation;
        private Button _cancel;
        private Label _status;
        private Label _selectionMessage;
        private ListView _list;
        private readonly List<AssetValidationFinding> _rows = new();

        public AssetValidationPanel(DataVisualizer owner)
            : base("Asset validation", "Find missing references and duplicate authored IDs in selected main assets. Assets are never repaired or saved.", owner.CloseAssetTool)
        {
            name = "validation-workspace";
            BuildContent();
            _updates = schedule.Execute(UpdateValidation).Every(100);
        }

        [MenuItem("Tools/Wallstop Studios/Validate Selected Data Assets")]
        public static void Open()
        {
            DataVisualizer window = EditorWindow.GetWindow<DataVisualizer>();
            window.rootVisualElement.schedule.Execute(() =>
            {
                if (window != null)
                {
                    window.OpenAssetValidation();
                }
            });
        }

        private static string FindingTitle(string code)
        {
            return code switch
            {
                "duplicate_authored_id" => "Duplicate authored ID",
                "missing_reference" => "Missing reference",
                "asset_unavailable" => "Asset unavailable",
                _ => "Could not finish checking this asset",
            };
        }

        public void ValidateSelection()
        {
            List<string> guids = new();
            foreach (UnityEngine.Object item in Selection.objects)
            {
                if (!(item is ScriptableObject) || !AssetDatabase.IsMainAsset(item))
                {
                    _selectionMessage.text = "Select only ScriptableObject main assets. No new validation started.";
                    _selectionMessage.style.display = DisplayStyle.Flex;
                    return;
                }
                guids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(item)));
            }
            if (guids.Count == 0)
            {
                _selectionMessage.text = "Select one or more ScriptableObject main assets. No new validation started.";
                _selectionMessage.style.display = DisplayStyle.Flex;
                return;
            }
            _selectionMessage.style.display = DisplayStyle.None;
            _selectionMessage.text = string.Empty;
            _validation?.Dispose();
            _validation = new EditorAssetValidation(guids);
            _rows.Clear();
            _list.RefreshItems();
            UpdateValidation();
        }

        public override void Dispose()
        {
            _updates?.Pause();
            _validation?.Dispose();
            _validation = null;
        }

        private void CancelValidation()
        {
            _validation?.Dispose();
            UpdateValidation();
        }

        private void UpdateValidation()
        {
            if (_validation == null || _status == null)
            {
                return;
            }
            _validation.Advance();
            _cancel.SetEnabled(!_validation.IsComplete && !_validation.IsCancelled);
            _status.text =
                (
                    _validation.IsComplete ? "Complete"
                    : _validation.IsCancelled ? "Cancelled (partial)"
                    : "Processing (partial)"
                )
                + ": "
                + _validation.ProcessedCount
                + "/"
                + _validation.TotalCount
                + " assets, "
                + _validation.Findings.Count
                + " findings.";
            if (_rows.Count != _validation.Findings.Count)
            {
                _rows.Clear();
                for (int i = 0; i < _validation.Findings.Count; i++)
                {
                    _rows.Add(_validation.Findings[i]);
                }
                _list.RefreshItems();
            }
        }

        private void BuildContent()
        {
            VisualElement root = this;
            root.Add(
                new Label("Checks Unity serialized object references and duplicate authored IDs within the selected main assets.") { style = { whiteSpace = WhiteSpace.Normal } }
            );
            root.Add(new Label("Custom serialization is not checked. Results can be incomplete until processing finishes.") { style = { whiteSpace = WhiteSpace.Normal } });
            VisualElement actions = ActionRow(root);
            Button validate = AddAction(
                actions,
                "Validate Project selection",
                "Read selected ScriptableObject main assets without saving or repairing them",
                ValidateSelection,
                true
            );
            validate.name = "validate-selection";
            _cancel = AddAction(actions, "Cancel validation", "Stop between assets and retain partial findings", CancelValidation);
            _cancel.SetEnabled(false);
            _status = new Label("Select ScriptableObject main assets in the Project window.") { name = "validation-status" };
            root.Add(_status);
            _selectionMessage = new Label { name = "validation-selection-message", style = { whiteSpace = WhiteSpace.Normal, display = DisplayStyle.None } };
            root.Add(_selectionMessage);
            _selectionMessage.AddToClassList(StyleConstants.AssetToolMessageClass);
            _list = new ListView
            {
                itemsSource = _rows,
                makeItem = () =>
                {
                    Label row = new();
                    row.AddToClassList(StyleConstants.AssetToolResultClass);
                    return row;
                },
                bindItem = (element, index) =>
                {
                    AssetValidationFinding finding = _rows[index];
                    ((Label)element).text = FindingTitle(finding.Code) + "\n" + finding.Path + "\n" + finding.PropertyPath + " " + finding.Detail;
                    element.tooltip = ((Label)element).text + "\n" + finding.Guid;
                },
                fixedItemHeight = 86,
                style = { flexGrow = 1 },
            };
            _list.AddToClassList(StyleConstants.AssetToolResultsClass);
            root.Add(_list);
        }
    }
}
