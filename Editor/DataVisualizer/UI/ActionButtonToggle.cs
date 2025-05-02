namespace WallstopStudios.Editor.DataVisualizer.UI
{
    using System.Collections.Generic;
    using UnityEngine.UIElements;

    public class ActionButtonToggle : VisualElement, INotifyValueChanged<bool>
    {
        // Implement INotifyValueChanged<bool>
        private bool m_Value;

        public bool value
        {
            get => m_Value;
            set
            {
                if (EqualityComparer<bool>.Default.Equals(m_Value, value))
                    return;

                // Use panel.SendEvent for proper change event dispatching
                using (ChangeEvent<bool> evt = ChangeEvent<bool>.GetPooled(m_Value, value))
                {
                    evt.target = this;
                    SetValueWithoutNotify(value); // Update internal value and visuals
                    SendEvent(evt); // Send the event
                }
            }
        }

        public void SetValueWithoutNotify(bool newValue)
        {
            m_Value = newValue;
            UpdateVisualState(); // Update appearance when value changes
        }

        // --- USS Class Names ---
        public static readonly string ussClassName = "action-button-toggle";
        public static readonly string labelUssClassName = ussClassName + "__label";
        public static readonly string buttonUssClassName = ussClassName + "__button";
        public static readonly string knobUssClassName = ussClassName + "__knob";
        public static readonly string buttonOnUssClassName = buttonUssClassName + "--on"; // State class

        // --- Element References ---
        private Label _labelElement;
        private VisualElement _toggleButtonElement; // The track/background
        private VisualElement _knobElement; // The moving knob

        // --- Constructors ---
        public ActionButtonToggle()
            : this(null) { }

        public ActionButtonToggle(string label)
        {
            // --- Base Element Setup ---
            AddToClassList(ussClassName);
            style.flexDirection = FlexDirection.Row; // Arrange label and button horizontally
            style.alignItems = Align.Center; // Vertically center label and button

            // --- Label ---
            _labelElement = new Label(label);
            _labelElement.AddToClassList(labelUssClassName);
            _labelElement.style.marginRight = 5; // Space between label and button
            // Prevent label click from doing anything weird (like text selection)
            _labelElement.pickingMode = PickingMode.Ignore;
            Add(_labelElement);

            // --- Toggle Button (Track) ---
            _toggleButtonElement = new VisualElement();
            _toggleButtonElement.AddToClassList(buttonUssClassName);
            // *** Register click handler on the BUTTON AREA ONLY ***
            _toggleButtonElement.RegisterCallback<ClickEvent>(OnClick);
            _toggleButtonElement.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation()); // Prevent drag starting on button
            Add(_toggleButtonElement);

            // --- Toggle Knob ---
            _knobElement = new VisualElement();
            _knobElement.AddToClassList(knobUssClassName);
            _knobElement.pickingMode = PickingMode.Ignore; // Knob itself isn't clickable
            _toggleButtonElement.Add(_knobElement); // Knob is child of button/track

            // --- Initial State ---
            SetValueWithoutNotify(false); // Default to off
        }

        // --- Public Property for Label ---
        public string label
        {
            get => _labelElement?.text;
            set
            {
                if (_labelElement != null)
                    _labelElement.text = value;
            }
        }

        // --- Event Handlers ---
        private void OnClick(ClickEvent evt)
        {
            // Toggle the value - setter handles visuals and event notification
            this.value = !this.value;
            evt.StopPropagation(); // Stop click from propagating further
        }

        // --- Visual Update ---
        private void UpdateVisualState()
        {
            // Add/remove the '--on' class based on the value
            _toggleButtonElement?.EnableInClassList(buttonOnUssClassName, m_Value);
        }
    }
}
