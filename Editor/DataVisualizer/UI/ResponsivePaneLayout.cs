namespace WallstopStudios.DataVisualizer.Editor.UI
{
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>Owns temporary pane visibility and clamping without changing preferred widths.</summary>
    public sealed class ResponsivePaneLayout
    {
        public const float CatalogMinimum = 320f;
        public const float ObjectsMinimum = 220f;
        public const float InspectorMinimum = 360f;
        public const float ExpandedMinimum = CatalogMinimum + ObjectsMinimum + InspectorMinimum + 12f;

        public bool CanSaveWidths { get; private set; }
        public bool IsCompact { get; private set; }

        private readonly Action _beforeNavigate;
        private readonly TwoPaneSplitView _outer;
        private readonly TwoPaneSplitView _inner;
        private readonly Button _typesButton;
        private readonly Button _objectsButton;
        private readonly Button _inspectorButton;
        private Vector2 _preferred;
        private float _width;
        private bool _initialized;
        private bool _showTypes = true;
        private bool _showObjects = true;
        private PaneNavigation _active = PaneNavigation.Objects;

        public ResponsivePaneLayout(TwoPaneSplitView outer, TwoPaneSplitView inner, VisualElement navigation, Action beforeNavigate = null)
        {
            _beforeNavigate = beforeNavigate;
            _outer = outer;
            _inner = inner;
            _typesButton = new Button(() => Show(PaneNavigation.Types)) { text = "Types", name = "show-types-button" };
            _objectsButton = new Button(() => Show(PaneNavigation.Objects)) { text = "Objects", name = "show-objects-button" };
            _inspectorButton = new Button(() => Show(PaneNavigation.Inspector)) { text = "Inspector", name = "show-inspector-button" };
            navigation.Add(_typesButton);
            navigation.Add(_objectsButton);
            navigation.Add(_inspectorButton);
            _outer[0].focusable = true;
            _inner[0].focusable = true;
            _inner[1].focusable = true;
            _outer.RegisterCallback<GeometryChangedEvent>(InitializeAfterLayout);
            _inner.RegisterCallback<GeometryChangedEvent>(InitializeAfterLayout);
        }

        public void Resize(float width, Vector2 preferred)
        {
            Transition(width, preferred, null);
        }

        public void Show(PaneNavigation pane)
        {
            if (pane is not (PaneNavigation.Types or PaneNavigation.Objects or PaneNavigation.Inspector))
            {
                return;
            }
            _beforeNavigate?.Invoke();
            Transition(_width, _preferred, pane);
        }

        private void InitializeAfterLayout(GeometryChangedEvent _)
        {
            Transition(_width, _preferred, null);
        }

        private void Transition(float width, Vector2 preferred, PaneNavigation? requested)
        {
            if (width <= 0 || float.IsNaN(width))
            {
                return;
            }
            bool compact = width < ExpandedMinimum;
            bool geometryChanged = !Mathf.Approximately(_width, width);
            _preferred = preferred;
            if (!geometryChanged && !requested.HasValue && _initialized)
            {
                return;
            }

            _width = width;
            if (_outer.fixedPane == null || _inner.fixedPane == null)
            {
                return;
            }
            _initialized = true;
            _outer.UnregisterCallback<GeometryChangedEvent>(InitializeAfterLayout);
            _inner.UnregisterCallback<GeometryChangedEvent>(InitializeAfterLayout);
            IsCompact = compact;
            if (requested.HasValue)
            {
                _active = requested.Value;
                if (!compact)
                {
                    switch (_active)
                    {
                        case PaneNavigation.Types:
                            _showTypes = !_showTypes;
                            break;
                        case PaneNavigation.Objects:
                            _showObjects = !_showObjects;
                            break;
                        case PaneNavigation.Inspector:
                            _showTypes = false;
                            _showObjects = false;
                            break;
                    }
                }
            }

            if (!compact && ((_active == PaneNavigation.Types && !_showTypes) || (_active == PaneNavigation.Objects && !_showObjects)))
            {
                _active = PaneNavigation.Inspector;
            }

            bool typesVisible = compact ? _active == PaneNavigation.Types : _showTypes;
            bool objectsVisible = compact ? _active == PaneNavigation.Objects : _showObjects;
            bool inspectorVisible = !compact || _active == PaneNavigation.Inspector;
            VisualElement focused = _outer.focusController?.focusedElement as VisualElement;
            CanSaveWidths = false;
            _outer.UnCollapse();
            _inner.UnCollapse();
            if (compact && typesVisible)
            {
                _outer.CollapseChild(1);
            }
            else
            {
                if (!typesVisible)
                {
                    _outer.CollapseChild(0);
                }
                if (!objectsVisible)
                {
                    _inner.CollapseChild(0);
                }
                else if (!inspectorVisible)
                {
                    _inner.CollapseChild(1);
                }
            }

            if (!compact)
            {
                float available = width - 12f;
                float catalog = typesVisible ? Mathf.Clamp(preferred.x, CatalogMinimum, available - ObjectsMinimum - InspectorMinimum) : 0f;
                float objects = objectsVisible ? Mathf.Clamp(preferred.y, ObjectsMinimum, available - catalog - InspectorMinimum) : 0f;
                if (typesVisible)
                {
                    _outer.fixedPaneInitialDimension = catalog;
                }
                if (objectsVisible)
                {
                    _inner.fixedPaneInitialDimension = objects;
                }
                CanSaveWidths = typesVisible && objectsVisible && Mathf.Approximately(catalog, preferred.x) && Mathf.Approximately(objects, preferred.y);
            }

            _typesButton.text =
                compact ? "Types"
                : typesVisible ? "Hide types"
                : "Show types";
            _objectsButton.text =
                compact ? "Objects"
                : objectsVisible ? "Hide list"
                : "Show list";
            _typesButton.tooltip = compact ? "Show the type catalog" : "Toggle the type catalog without changing preferred widths";
            _objectsButton.tooltip = compact ? "Show the object list" : "Toggle the object list without changing preferred widths";
            _inspectorButton.tooltip = "Give the inspector the available width";
            _typesButton.EnableInClassList("selected", typesVisible);
            _objectsButton.EnableInClassList("selected", objectsVisible);
            _inspectorButton.EnableInClassList("selected", inspectorVisible);

            VisualElement destination =
                typesVisible ? _outer[0]
                : objectsVisible ? _inner[0]
                : _inner[1];
            bool focusHidden =
                focused != null
                && ((!typesVisible && _outer[0].Contains(focused)) || (!objectsVisible && _inner[0].Contains(focused)) || (!inspectorVisible && _inner[1].Contains(focused)));
            if (requested.HasValue || focusHidden)
            {
                destination.Focus();
            }
        }
    }
}
