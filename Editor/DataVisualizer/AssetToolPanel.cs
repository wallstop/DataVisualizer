namespace WallstopStudios.DataVisualizer.Editor
{
    using System;
    using Styles;
    using UnityEngine.UIElements;

    public abstract class AssetToolPanel : VisualElement, IDisposable
    {
        protected AssetToolPanel(string title, string description, Action close)
        {
            focusable = true;
            AddToClassList(StyleConstants.AssetToolPanelClass);
            VisualElement heading = ActionRow(this);
            Label brand = new("DxVisualizer / " + title);
            brand.AddToClassList(StyleConstants.AssetToolTitleClass);
            heading.Add(brand);
            AddAction(heading, "Back to assets", "Close this workspace and keep the current browser state", close).name = "asset-tool-back";
            Label summary = new(description);
            summary.AddToClassList(StyleConstants.AssetToolSummaryClass);
            Add(summary);
            RegisterCallback<DetachFromPanelEvent>(evt =>
            {
                if (evt.target == this)
                {
                    Dispose();
                }
            });
        }

        protected static VisualElement ActionRow(VisualElement parent)
        {
            VisualElement row = new();
            row.AddToClassList(StyleConstants.AssetToolActionsClass);
            parent.Add(row);
            return row;
        }

        protected static Button AddAction(VisualElement parent, string text, string tooltip, Action action, bool primary = false)
        {
            Button button = new(action) { text = text, tooltip = tooltip };
            button.AddToClassList(StyleConstants.PopoverButtonClass);
            if (primary)
            {
                button.AddToClassList(StyleConstants.PopoverPrimaryActionClass);
            }
            parent.Add(button);
            return button;
        }

        public abstract void Dispose();
    }
}
