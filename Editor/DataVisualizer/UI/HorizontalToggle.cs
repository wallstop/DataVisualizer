namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;
    using UnityEngine.UIElements.Experimental;

    /*
        Instantiated only from C# (never from UXML), so the deprecated UxmlFactory/UxmlTraits
        pair is intentionally omitted.
    */
    public sealed class HorizontalToggle : VisualElement
    {
        private const float AnimationDurationMs = 150f;

        public event Action OnLeftSelected;
        public event Action OnRightSelected;

        public static readonly string ussClassName = "horizontal-toggle";
        public static readonly string containerUssClassName = ussClassName + "__container";
        public static readonly string labelContainerUssClassName =
            ussClassName + "__label-container";
        public static readonly string leftLabelUssClassName = ussClassName + "__left-label";
        public static readonly string rightLabelUssClassName = ussClassName + "__right-label";
        public static readonly string indicatorUssClassName = ussClassName + "__indicator";
        public static readonly string indicatorSelectedUssClassName =
            indicatorUssClassName + "--selected";
        public string LeftText
        {
            get => _leftText;
            set
            {
                _leftText = value;
                if (_leftLabel != null)
                {
                    _leftLabel.text = value;
                }
            }
        }

        public VisualElement Indicator => _indicator;

        public Label LeftLabel => _leftLabel;
        public Label RightLabel => _rightLabel;
        public string RightText
        {
            get => _rightText;
            set
            {
                _rightText = value;
                if (_rightLabel != null)
                {
                    _rightLabel.text = value;
                }
            }
        }
        public Color SelectedBackgroundColor
        {
            get => _selectedBackgroundColor ?? _indicator.resolvedStyle.backgroundColor;
            set
            {
                _selectedBackgroundColor = value;
                UpdateColors();
            }
        }
        public Color UnselectedBackgroundColor
        {
            get => _unselectedBackgroundColor ?? _container.resolvedStyle.backgroundColor;
            set
            {
                _unselectedBackgroundColor = value;
                UpdateColors();
            }
        }
        public Color SelectedTextColor
        {
            get =>
                _selectedTextColor
                ?? (_isLeftSelected ? _leftLabel : _rightLabel).resolvedStyle.color;
            set
            {
                _selectedTextColor = value;
                UpdateColors();
            }
        }
        public Color UnselectedTextColor
        {
            get =>
                _unselectedTextColor
                ?? (_isLeftSelected ? _rightLabel : _leftLabel).resolvedStyle.color;
            set
            {
                _unselectedTextColor = value;
                UpdateColors();
            }
        }
        public Color IndicatorColor
        {
            get => _indicatorColor ?? _indicator.resolvedStyle.backgroundColor;
            set
            {
                _indicatorColor = value;
                if (_indicator != null)
                {
                    _indicator.style.backgroundColor = value;
                }
            }
        }

        public bool IsLeftSelected => _isLeftSelected;

        private Label _leftLabel;
        private Label _rightLabel;
        private VisualElement _indicator;
        private VisualElement _labelContainer;
        private VisualElement _container;

        private bool _isLeftSelected = true;
        private bool _isAnimating = false;

        private string _leftText = "Left";

        private string _rightText = "Right";

        private Color? _selectedBackgroundColor;

        private Color? _unselectedBackgroundColor;

        private Color? _selectedTextColor;

        private Color? _unselectedTextColor;

        private Color? _indicatorColor;

        public HorizontalToggle()
        {
            AddToClassList(ussClassName);
            Initialize();
        }

        public void SelectLeft(bool animate = true, bool notify = true, bool force = false)
        {
            if (!force && _isLeftSelected && notify)
            {
                return;
            }

            if (_isAnimating)
            {
                return;
            }

            _isLeftSelected = true;
            UpdateColors();
            UpdateIndicatorPosition(animate);

            if (notify)
            {
                OnLeftSelected?.Invoke();
            }
        }

        public void SelectRight(bool animate = true, bool notify = true, bool force = false)
        {
            if (!force && !_isLeftSelected && notify)
            {
                return;
            }

            if (_isAnimating)
            {
                return;
            }
            _isLeftSelected = false;
            UpdateColors();
            UpdateIndicatorPosition(animate);

            if (notify)
            {
                OnRightSelected?.Invoke();
            }
        }

        private void Initialize()
        {
            _container = new VisualElement();
            _container.AddToClassList(containerUssClassName);
            Add(_container);

            _indicator = new VisualElement();
            _indicator.AddToClassList(indicatorUssClassName);
            _container.Add(_indicator);

            _labelContainer = new VisualElement();
            _labelContainer.AddToClassList(labelContainerUssClassName);
            _container.Add(_labelContainer);

            _leftLabel = new Label(LeftText);
            _leftLabel.AddToClassList(leftLabelUssClassName);
            _labelContainer.Add(_leftLabel);

            _rightLabel = new Label(RightText);
            _rightLabel.AddToClassList(rightLabelUssClassName);
            _labelContainer.Add(_rightLabel);

            RegisterCallback<GeometryChangedEvent>(OnGeometryChange);

            _leftLabel.RegisterCallback<ClickEvent, HorizontalToggle>(
                static (_, context) => context.SelectLeft(),
                this
            );
            _rightLabel.RegisterCallback<ClickEvent, HorizontalToggle>(
                static (_, context) => context.SelectRight(),
                this
            );

            UpdateColors();
            UpdateIndicatorPosition(false);
        }

        private void OnGeometryChange(GeometryChangedEvent evt)
        {
            UpdateIndicatorPosition(false);
            UnregisterCallback<GeometryChangedEvent>(OnGeometryChange);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChangeReapply);
        }

        private void OnGeometryChangeReapply(GeometryChangedEvent evt)
        {
            UpdateIndicatorPosition(false);
        }

        private void UpdateColors()
        {
            if (_leftLabel == null || _rightLabel == null || _container == null)
            {
                return;
            }

            _leftLabel.EnableInClassList("selected", _isLeftSelected);
            _rightLabel.EnableInClassList("selected", !_isLeftSelected);
            _leftLabel.EnableInClassList("unselected", !_isLeftSelected);
            _rightLabel.EnableInClassList("unselected", _isLeftSelected);

            _container.style.backgroundColor = _unselectedBackgroundColor.HasValue
                ? new StyleColor(_unselectedBackgroundColor.Value)
                : new StyleColor(StyleKeyword.Null);
            Color? indicatorColor = _indicatorColor ?? _selectedBackgroundColor;
            _indicator.style.backgroundColor = indicatorColor.HasValue
                ? new StyleColor(indicatorColor.Value)
                : new StyleColor(StyleKeyword.Null);

            StyleColor selectedTextColor = _selectedTextColor.HasValue
                ? new StyleColor(_selectedTextColor.Value)
                : new StyleColor(StyleKeyword.Null);
            StyleColor unselectedTextColor = _unselectedTextColor.HasValue
                ? new StyleColor(_unselectedTextColor.Value)
                : new StyleColor(StyleKeyword.Null);
            _leftLabel.style.color = _isLeftSelected ? selectedTextColor : unselectedTextColor;
            _rightLabel.style.color = _isLeftSelected ? unselectedTextColor : selectedTextColor;
        }

        private void UpdateIndicatorPosition(bool animate)
        {
            if (
                _indicator == null
                || _leftLabel == null
                || _rightLabel == null
                || float.IsNaN(_leftLabel.resolvedStyle.width)
                || float.IsNaN(_rightLabel.resolvedStyle.width)
            )
            {
                schedule.Execute(() => UpdateIndicatorPosition(animate)).StartingIn(0);
                return;
            }
            _isAnimating = true;

            float targetX = _isLeftSelected ? 0 : _leftLabel.resolvedStyle.width;
            float targetWidth = _isLeftSelected
                ? _leftLabel.resolvedStyle.width
                : _rightLabel.resolvedStyle.width;

            if (animate && 0 < resolvedStyle.width)
            {
                _indicator.RemoveFromClassList(indicatorSelectedUssClassName);

                _indicator
                    .experimental.animation.Start(
                        new StyleValues { left = targetX, width = targetWidth },
                        (int)AnimationDurationMs
                    )
                    .Ease(Easing.OutQuad)
                    .OnCompleted(() =>
                    {
                        _isAnimating = false;
                        _indicator.AddToClassList(indicatorSelectedUssClassName);
                    });
            }
            else
            {
                _indicator.style.left = new Length(targetX, LengthUnit.Pixel);
                _indicator.style.width = new Length(targetWidth, LengthUnit.Pixel);
                _isAnimating = false;
                if (animate)
                {
                    _indicator.AddToClassList(indicatorSelectedUssClassName);
                }
            }
        }
    }
}
