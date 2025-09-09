namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;
    using UnityEngine.UIElements.Experimental;

    public sealed class HorizontalToggle : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<HorizontalToggle, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlStringAttributeDescription _leftText = new()
            {
                name = "left-text",
                defaultValue = "Left",
            };

            private readonly UxmlStringAttributeDescription _rightText = new()
            {
                name = "right-text",
                defaultValue = "Right",
            };

            private readonly UxmlColorAttributeDescription _selectedBackgroundColor = new()
            {
                name = "selected-background-color",
                defaultValue = new Color(0.1f, 0.5f, 0.8f),
            };

            private readonly UxmlColorAttributeDescription _unselectedBackgroundColor = new()
            {
                name = "unselected-background-color",
                defaultValue = new Color(0.2f, 0.2f, 0.2f),
            };

            private readonly UxmlColorAttributeDescription _selectedTextColor = new()
            {
                name = "selected-text-color",
                defaultValue = Color.white,
            };

            private readonly UxmlColorAttributeDescription _unselectedTextColor = new()
            {
                name = "unselected-text-color",
                defaultValue = new Color(0.7f, 0.7f, 0.7f),
            };

            private readonly UxmlColorAttributeDescription _indicatorColor = new()
            {
                name = "indicator-color",
                defaultValue = new Color(0.15f, 0.65f, 0.95f),
            };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                HorizontalToggle ate = ve as HorizontalToggle;

                ate.LeftText = _leftText.GetValueFromBag(bag, cc);
                ate.RightText = _rightText.GetValueFromBag(bag, cc);
                ate.SelectedBackgroundColor = _selectedBackgroundColor.GetValueFromBag(bag, cc);
                ate.UnselectedBackgroundColor = _unselectedBackgroundColor.GetValueFromBag(bag, cc);
                ate.SelectedTextColor = _selectedTextColor.GetValueFromBag(bag, cc);
                ate.UnselectedTextColor = _unselectedTextColor.GetValueFromBag(bag, cc);
                ate.IndicatorColor = _indicatorColor.GetValueFromBag(bag, cc);

                ate.Clear();
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
        private VisualElement _labelContainer;
        private VisualElement _container;

        private bool _isLeftSelected = true;
        private bool _isAnimating = false;
        private const float AnimationDurationMs = 150f;

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
                {
                    _leftLabel.text = value;
                }
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
                {
                    _rightLabel.text = value;
                }
            }
        }

        private Color _selectedBackgroundColor = new(0.1f, 0.5f, 0.8f);
        public Color SelectedBackgroundColor
        {
            get => _selectedBackgroundColor;
            set
            {
                _selectedBackgroundColor = value;
                UpdateColors();
            }
        }

        private Color _unselectedBackgroundColor = new(0.2f, 0.2f, 0.2f);
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

        private Color _unselectedTextColor = new(0.7f, 0.7f, 0.7f);
        public Color UnselectedTextColor
        {
            get => _unselectedTextColor;
            set
            {
                _unselectedTextColor = value;
                UpdateColors();
            }
        }

        private Color _indicatorColor = new(0.15f, 0.65f, 0.95f);
        public Color IndicatorColor
        {
            get => _indicatorColor;
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
                (_, context) => context.SelectLeft(),
                this
            );
            _rightLabel.RegisterCallback<ClickEvent, HorizontalToggle>(
                (_, context) => context.SelectRight(),
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

            _leftLabel.EnableInClassList("selected", true);
            _rightLabel.EnableInClassList("selected", false);
            _leftLabel.EnableInClassList("unselected", false);
            _rightLabel.EnableInClassList("unselected", true);
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
            _leftLabel.EnableInClassList("selected", false);
            _rightLabel.EnableInClassList("selected", true);
            _leftLabel.EnableInClassList("unselected", true);
            _rightLabel.EnableInClassList("unselected", false);
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
            {
                return;
            }

            _container.style.backgroundColor = UnselectedBackgroundColor;
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
                schedule.Execute(() => UpdateIndicatorPosition(animate)).StartingIn(0);
                return;
            }
            _isAnimating = true;

            float targetX = _isLeftSelected ? 0 : _leftLabel.resolvedStyle.width;
            float targetWidth = _isLeftSelected
                ? _leftLabel.resolvedStyle.width
                : _rightLabel.resolvedStyle.width;

            if (animate && resolvedStyle.width > 0)
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
