namespace WallstopStudios.DataVisualizer.Editor
{
    using Styles;
    using UnityEngine.UIElements;
    using Utilities;

    /// <summary>A confirmed snapshot of processor targets, owned by the browser workspace.</summary>
    public sealed class ProcessorOperationPanel : AssetToolPanel
    {
        public EditorProcessorOperation Operation { get; }

        private readonly IVisualElementScheduledItem _updates;
        private readonly Button _run;
        private readonly Button _cancel;
        private readonly Label _status;
        private readonly ListView _list;
        private readonly DataVisualizer _owner;

        public ProcessorOperationPanel(DataVisualizer owner, EditorProcessorOperation operation, string processorName)
            : base(
                "Run " + processorName,
                "Review every target, including hidden selections. Process runs once on this batch. No automatic Undo or rollback; use version control to recover.",
                owner.CloseAssetTool
            )
        {
            _owner = owner;
            Operation = operation;
            name = "processor-workspace";
            _status = new Label();
            _status.AddToClassList(StyleConstants.AssetToolSummaryClass);
            Add(_status);
            VisualElement actions = ActionRow(this);
            _run = AddAction(
                actions,
                "Run confirmed targets",
                "Apply only this exact preview; later selection changes do not alter it",
                () =>
                {
                    Operation.Apply();
                    UpdateOperation();
                },
                true
            );
            _run.name = "processor-confirm";
            _cancel = AddAction(
                actions,
                "Cancel",
                "Stop before Process begins; its execution cannot be interrupted",
                () =>
                {
                    Operation.Cancel();
                    UpdateOperation();
                }
            );
            _cancel.name = "processor-cancel";
            _list = new ListView
            {
                name = "processor-targets",
                itemsSource = new System.Collections.Generic.List<EditorProcessorTarget>(operation.Targets),
                fixedItemHeight = 68,
                selectionType = SelectionType.None,
                makeItem = () =>
                {
                    TextField row = new() { isReadOnly = true, multiline = true };
                    row.AddToClassList(StyleConstants.AssetToolResultClass);
                    return row;
                },
                bindItem = (element, index) =>
                {
                    EditorProcessorTarget target = Operation.Targets[index];
                    string outcome =
                        target.Code == "applied" ? "Batch returned"
                        : target.Code == "outcome_unknown" ? "Outcome unknown"
                        : target.Code == "stale_target" ? "Target changed"
                        : "Not run";
                    ((TextField)element).SetValueWithoutNotify(target.Path + "\n" + target.Guid + "\n" + outcome);
                    element.tooltip = target.Path + "\n" + target.Guid + "\n" + target.Code;
                },
            };
            _list.AddToClassList(StyleConstants.AssetToolResultsClass);
            Add(_list);
            _updates = schedule.Execute(UpdateOperation).Every(16);
            UpdateOperation();
        }

        public override void Dispose()
        {
            _updates?.Pause();
            Operation.Dispose();
        }

        private void UpdateOperation()
        {
            bool wasFinished = Operation.IsFinished;
            if (_owner.IsAssetEditingSuspended)
            {
                Operation.Cancel();
            }
            else
            {
                Operation.Advance();
            }
            _run.SetEnabled(Operation.Status == "preview");
            _cancel.SetEnabled(!Operation.IsFinished);
            string phase =
                Operation.Status == "preview" ? "Preview"
                : Operation.Status == "preparing" ? "Preparing"
                : Operation.Status == "complete" ? "Complete"
                : Operation.Status == "cancelled" ? "Cancelled"
                : Operation.Status == "rejected" ? "Cannot run"
                : "Failed";
            _status.text =
                phase
                + ": "
                + Operation.Targets.Count
                + " targets. "
                + (Operation.Status == "preparing" ? Operation.PreparedCount + " prepared. " : string.Empty)
                + Operation.Detail;
            if (Operation.IsFinished)
            {
                _updates?.Pause();
                _list.RefreshItems();
                if (!wasFinished && Operation.Status != "cancelled")
                {
                    DataVisualizer.SignalRefresh();
                }
            }
        }
    }
}
