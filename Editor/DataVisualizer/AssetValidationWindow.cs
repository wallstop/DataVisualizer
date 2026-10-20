namespace WallstopStudios.DataVisualizer.Editor
{
    using System.Collections.Generic;
    using Extensions;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Utilities;

    public sealed class AssetValidationWindow : EditorWindow
    {
        private EditorAssetValidation _validation;
        private Label _status;
        private Label _selectionMessage;
        private ListView _list;
        private readonly List<AssetValidationFinding> _rows = new();

        [MenuItem("Tools/Wallstop Studios/Validate Selected Data Assets")]
        public static void Open()
        {
            GetWindow<AssetValidationWindow>("Asset validation").Show();
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

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = 6;
            root.style.paddingRight = 6;
            root.style.paddingTop = 6;
            root.Add(
                new Label("Checks Unity serialized object references and duplicate authored IDs within the selected main assets.") { style = { whiteSpace = WhiteSpace.Normal } }
            );
            root.Add(new Label("Custom serialization is not checked. Results can be incomplete until processing finishes.") { style = { whiteSpace = WhiteSpace.Normal } });
            root.Add(
                new Button(ValidateSelection)
                {
                    name = "validate-selection",
                    text = "Validate current Project selection",
                    tooltip = "Read selected ScriptableObject main assets without saving or repairing them",
                }
            );
            root.Add(new Button(CancelValidation) { text = "Cancel", tooltip = "Stop between assets and retain partial findings" });
            _status = new Label("Select ScriptableObject main assets in the Project window.") { name = "validation-status" };
            root.Add(_status);
            _selectionMessage = new Label { name = "validation-selection-message", style = { whiteSpace = WhiteSpace.Normal, display = DisplayStyle.None } };
            root.Add(_selectionMessage);
            _list = new ListView
            {
                itemsSource = _rows,
                makeItem = () => new Label { style = { whiteSpace = WhiteSpace.Normal } },
                bindItem = (element, index) =>
                {
                    AssetValidationFinding finding = _rows[index];
                    ((Label)element).text = FindingTitle(finding.Code) + "\n" + finding.Path + "\n" + finding.PropertyPath + " " + finding.Detail;
                    element.tooltip = ((Label)element).text + "\n" + finding.Guid;
                },
                fixedItemHeight = 86,
                style = { flexGrow = 1 },
            };
            root.Add(_list);
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

        private void CancelValidation()
        {
            _validation?.Dispose();
            UpdateValidation();
        }

        private void OnInspectorUpdate()
        {
            UpdateValidation();
        }

        private void OnDisable()
        {
            _validation?.Dispose();
            _validation = null;
        }

        private void UpdateValidation()
        {
            if (_validation == null || _status == null)
            {
                return;
            }
            _validation.Advance();
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
    }
}
