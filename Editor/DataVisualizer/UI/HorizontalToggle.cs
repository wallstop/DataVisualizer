namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;
    using UnityEngine.UIElements.Experimental;

    public class HorizontalToggle : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<HorizontalToggle, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlStringAttributeDescription m_LeftText = new UxmlStringAttributeDescription
            {
                name = "left-text",
                defaultValue = "Left",
            };
            UxmlStringAttributeDescription m_RightText = new UxmlStringAttributeDescription
            {
                name = "right-text",
                defaultValue = "Right",
            };

            UxmlColorAttributeDescription m_SelectedBackgroundColor =
                new UxmlColorAttributeDescription
                {
                    name = "selected-background-color",
                    defaultValue = new Color(0.1f, 0.5f, 0.8f),
                };
            UxmlColorAttributeDescription m_UnselectedBackgroundColor =
                new UxmlColorAttributeDescription
                {
                    name = "unselected-background-color",
                    defaultValue = new Color(0.2f, 0.2f, 0.2f),
                };
            UxmlColorAttributeDescription m_SelectedTextColor = new UxmlColorAttributeDescription
            {
                name = "selected-text-color",
                defaultValue = Color.white,
            };
            UxmlColorAttributeDescription m_UnselectedTextColor = new UxmlColorAttributeDescription
            {
                name = "unselected-text-color",
                defaultValue = new Color(0.7f, 0.7f, 0.7f),
            };
            UxmlColorAttributeDescription m_IndicatorColor = new UxmlColorAttributeDescription
            {
                name = "indicator-color",
                defaultValue = new Color(0.15f, 0.65f, 0.95f),
            };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var ate = ve as HorizontalToggle;

                ate.LeftText = m_LeftText.GetValueFromBag(bag, cc);
                ate.RightText = m_RightText.GetValueFromBag(bag, cc);
                ate.SelectedBackgroundColor = m_SelectedBackgroundColor.GetValueFromBag(bag, cc);
                ate.UnselectedBackgroundColor = m_UnselectedBackgroundColor.GetValueFromBag(
                    bag,
                    cc
                );
                ate.SelectedTextColor = m_SelectedTextColor.GetValueFromBag(bag, cc);
                ate.UnselectedTextColor = m_UnselectedTextColor.GetValueFromBag(bag, cc);
                ate.IndicatorColor = m_IndicatorColor.GetValueFromBag(bag, cc);

                ate.Clear(); // Clear existing elements if any from UXML
                ate.Initialize();
            }
        }

        public static readonly string ussClassName = "horizontal-toggle";
        public static readonly string containerUssClassName = ussClassName + "__container";
        public static readonly string labelContainerUssClassName =
            ussClassName + "__label-container";
        public static readonly string leftLabelUssClassName = ussClassName + "__left-label";
        public static readonly string rightLabelUssClassName = ussClassName + "__right-label";
        public static readonly string indicatorUssClassName = ussClassName + "__indicator";
        public static readonly string indicatorSelectedUssClassName =
            indicatorUssClassName + "--selected";

        private Label _leftLabel;
        private Label _rightLabel;
        private VisualElement _indicator;
        private VisualElement _labelContainer; // To hold the labels
        private VisualElement _container; // Main background container

        private bool _isLeftSelected = true;
        private bool _isAnimating = false;
        private const float AnimationDurationMs = 150f; // Duration of the animation in milliseconds

        public event Action OnLeftSelected;
        public event Action OnRightSelected;

        private string _leftText = "Left";
        public string LeftText
        {
            get => _leftText;
            set
            {
                _leftText = value;
                if (_leftLabel != null)
                    _leftLabel.text = value;
            }
        }

        public VisualElement Indicator => _indicator;

        public Label LeftLabel => _leftLabel;
        public Label RightLabel => _rightLabel;

        private string _rightText = "Right";
        public string RightText
        {
            get => _rightText;
            set
            {
                _rightText = value;
                if (_rightLabel != null)
                    _rightLabel.text = value;
            }
        }

        private Color _selectedBackgroundColor = new Color(0.1f, 0.5f, 0.8f);
        public Color SelectedBackgroundColor
        {
            get => _selectedBackgroundColor;
            set
            {
                _selectedBackgroundColor = value;
                UpdateColors();
            }
        }

        private Color _unselectedBackgroundColor = new Color(0.2f, 0.2f, 0.2f);
        public Color UnselectedBackgroundColor
        {
            get => _unselectedBackgroundColor;
            set
            {
                _unselectedBackgroundColor = value;
                UpdateColors();
            }
        }

        private Color _selectedTextColor = Color.white;
        public Color SelectedTextColor
        {
            get => _selectedTextColor;
            set
            {
                _selectedTextColor = value;
                UpdateColors();
            }
        }

        private Color _unselectedTextColor = new Color(0.7f, 0.7f, 0.7f);
        public Color UnselectedTextColor
        {
            get => _unselectedTextColor;
            set
            {
                _unselectedTextColor = value;
                UpdateColors();
            }
        }

        private Color _indicatorColor = new Color(0.15f, 0.65f, 0.95f);
        public Color IndicatorColor
        {
            get => _indicatorColor;
            set
            {
                _indicatorColor = value;
                if (_indicator != null)
                    _indicator.style.backgroundColor = value;
            }
        }

        public bool IsLeftSelected => _isLeftSelected;

        public HorizontalToggle()
        {
            AddToClassList(ussClassName);
            Initialize();
        }

        private void Initialize()
        {
            _container = new VisualElement();
            _container.AddToClassList(containerUssClassName);
            Add(_container);

            _indicator = new VisualElement();
            _indicator.AddToClassList(indicatorUssClassName);
            _container.Add(_indicator); // Add indicator first so it's behind labels

            _labelContainer = new VisualElement();
            _labelContainer.AddToClassList(labelContainerUssClassName);
            _container.Add(_labelContainer);

            _leftLabel = new Label(LeftText);
            _leftLabel.AddToClassList(leftLabelUssClassName);
            _labelContainer.Add(_leftLabel);

            _rightLabel = new Label(RightText);
            _rightLabel.AddToClassList(rightLabelUssClassName);
            _labelContainer.Add(_rightLabel);

            // Register geometry changed callback to position indicator correctly after layout
            RegisterCallback<GeometryChangedEvent>(OnGeometryChange);

            _leftLabel.RegisterCallback<ClickEvent>(evt => SelectLeft());
            _rightLabel.RegisterCallback<ClickEvent>(evt => SelectRight());

            // Initial state
            UpdateColors();
            UpdateIndicatorPosition(false); // Initial position without animation
        }

        private void OnGeometryChange(GeometryChangedEvent evt)
        {
            // We only need to react once the layout is stable.
            // For simplicity, we update on any geometry change, but this could be optimized.
            UpdateIndicatorPosition(false); // Re-position indicator without animation
            UnregisterCallback<GeometryChangedEvent>(OnGeometryChange); // Avoid multiple calls if not needed
            RegisterCallback<GeometryChangedEvent>(OnGeometryChangeReapply); // For subsequent changes like resizing
        }

        private void OnGeometryChangeReapply(GeometryChangedEvent evt)
        {
            UpdateIndicatorPosition(false);
        }

        public void SelectLeft(bool animate = true, bool notify = true)
        {
            if (_isLeftSelected && notify)
                return; // Already selected, do nothing if notify is true
            if (_isAnimating)
                return;

            _isLeftSelected = true;
            UpdateColors();
            UpdateIndicatorPosition(animate);

            if (notify)
            {
                OnLeftSelected?.Invoke();
            }
        }

        public void SelectRight(bool animate = true, bool notify = true)
        {
            if (!_isLeftSelected && notify)
                return; // Already selected, do nothing if notify is true
            if (_isAnimating)
                return;

            _isLeftSelected = false;
            UpdateColors();
            UpdateIndicatorPosition(animate);

            if (notify)
            {
                OnRightSelected?.Invoke();
            }
        }

        private void UpdateColors()
        {
            if (_leftLabel == null || _rightLabel == null || _container == null)
                return;

            _container.style.backgroundColor = UnselectedBackgroundColor; // Or your desired overall background
            _indicator.style.backgroundColor = IndicatorColor;

            if (_isLeftSelected)
            {
                _leftLabel.style.color = SelectedTextColor;
                _rightLabel.style.color = UnselectedTextColor;
            }
            else
            {
                _leftLabel.style.color = UnselectedTextColor;
                _rightLabel.style.color = SelectedTextColor;
            }
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
                // Elements might not be fully resolved yet.
                // Schedule a call for the next frame.
                schedule.Execute(() => UpdateIndicatorPosition(animate)).StartingIn(0);
                return;
            }
            _isAnimating = true;

            float targetX = _isLeftSelected ? 0 : _leftLabel.resolvedStyle.width;
            float targetWidth = _isLeftSelected
                ? _leftLabel.resolvedStyle.width
                : _rightLabel.resolvedStyle.width;

            if (animate && resolvedStyle.width > 0) // only animate if visible
            {
                _indicator.RemoveFromClassList(indicatorSelectedUssClassName); // For styling purposes

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
                    _indicator.AddToClassList(indicatorSelectedUssClassName); // ensure class is added even if animation was skipped
            }
        }
    }
}
