namespace WallstopStudios.DataVisualizer.Editor.Editor.DataVisualizer.UI
{
    using System;
    using Styles;
    using UnityEngine.UIElements;

    public sealed class ActionButtonToggle : VisualElement, INotifyValueChanged<bool>
    {
        public const string ClassName = "action-button-toggle";
        public const string LabelClassName = ClassName + "__label";
        public const string ButtonClassName = ClassName + "__button";
        public const string KnobClassName = ClassName + "__knob";
        public const string ButtonOnClassName = ButtonClassName + "--on";

        public bool value
        {
            get => _value;
            set
            {
                if (_value == value)
                {
                    return;
                }

                using ChangeEvent<bool> evt = ChangeEvent<bool>.GetPooled(_value, value);
                evt.target = this;
                SetValueWithoutNotify(value);
                SendEvent(evt);
            }
        }

        public string Label
        {
            get => _labelElement?.text;
            set
            {
                if (_labelElement != null)
                {
                    _labelElement.text = value;
                }
            }
        }

        private readonly Label _labelElement;
        private readonly VisualElement _toggleButtonElement;
        private readonly Action<bool> _onClick;

        private bool _value;

        public ActionButtonToggle(
            string label,
            Action<bool> onClick = null,
            bool initialValue = false
        )
        {
            _onClick = onClick;
            AddToClassList(ClassName);
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;

            _labelElement = new Label(label);
            _labelElement.AddToClassList(LabelClassName);
            _labelElement.pickingMode = PickingMode.Ignore;
            Add(_labelElement);

            _toggleButtonElement = new VisualElement();
            _toggleButtonElement.AddToClassList(ButtonClassName);
            _toggleButtonElement.RegisterCallback<ClickEvent>(OnClick);
            _toggleButtonElement.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            Add(_toggleButtonElement);
            _toggleButtonElement.AddToClassList(StyleConstants.ClickableClass);

            VisualElement knobElement = new();
            knobElement.AddToClassList(KnobClassName);
            knobElement.pickingMode = PickingMode.Ignore;
            _toggleButtonElement.Add(knobElement);

            SetValueWithoutNotify(initialValue);
        }

        public void SetValueWithoutNotify(bool newValue)
        {
            _value = newValue;
            UpdateVisualState();
        }

        private void OnClick(ClickEvent evt)
        {
            value = !value;
            _onClick?.Invoke(value);
            evt.StopPropagation();
        }

        private void UpdateVisualState()
        {
            _toggleButtonElement?.EnableInClassList(ButtonOnClassName, _value);
        }
    }
}
