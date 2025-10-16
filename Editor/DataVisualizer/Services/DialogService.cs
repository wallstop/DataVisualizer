namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Styles;

    internal sealed class DialogService : IDisposable
    {
        private readonly VisualElement _root;
        private VisualElement _overlay;
        private Label _titleLabel;
        private Label _messageLabel;
        private Button _primaryButton;
        private Button _secondaryButton;
        private Action _onPrimary;
        private Action _onSecondary;
        private bool _isShowing;

        public DialogService(VisualElement hostRoot)
        {
            _root = hostRoot ?? throw new ArgumentNullException(nameof(hostRoot));
            EnsureOverlay();
            Hide();
        }

        public void Dispose()
        {
            if (_overlay != null)
            {
                _overlay.RemoveFromHierarchy();
                _overlay = null;
            }
        }

        public void ShowMessage(string title, string message, string closeText)
        {
            ShowInternal(title, message, closeText, null, null, null);
        }

        public void ShowConfirmation(
            string title,
            string message,
            string confirmText,
            string cancelText,
            Action onConfirm,
            Action onCancel
        )
        {
            ShowInternal(title, message, confirmText, cancelText, onConfirm, onCancel);
        }

        private void ShowInternal(
            string title,
            string message,
            string primaryText,
            string secondaryText,
            Action onPrimary,
            Action onSecondary
        )
        {
            EnsureOverlay();

            _titleLabel.text = string.IsNullOrWhiteSpace(title) ? "" : title.Trim();
            _titleLabel.style.display = string.IsNullOrWhiteSpace(_titleLabel.text)
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            _messageLabel.text = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();

            _primaryButton.text = string.IsNullOrWhiteSpace(primaryText)
                ? "OK"
                : primaryText.Trim();
            _secondaryButton.text = string.IsNullOrWhiteSpace(secondaryText)
                ? string.Empty
                : secondaryText.Trim();

            bool showSecondary = !string.IsNullOrWhiteSpace(_secondaryButton.text);
            _secondaryButton.style.display = showSecondary ? DisplayStyle.Flex : DisplayStyle.None;

            _onPrimary = onPrimary;
            _onSecondary = onSecondary;

            _primaryButton.focusable = true;
            _primaryButton.Focus();

            _overlay.style.display = DisplayStyle.Flex;
            _overlay.RegisterCallback<KeyDownEvent>(HandleKeyDown, TrickleDown.TrickleDown);

            _isShowing = true;
        }

        private void HandleKeyDown(KeyDownEvent evt)
        {
            if (!_isShowing)
            {
                return;
            }

            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                evt.StopPropagation();
                InvokePrimary();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                evt.StopPropagation();
                InvokeSecondary();
            }
        }

        private void EnsureOverlay()
        {
            if (_overlay != null)
            {
                return;
            }

            _overlay = new VisualElement
            {
                pickingMode = PickingMode.Position,
                style =
                {
                    position = Position.Absolute,
                    top = 0,
                    bottom = 0,
                    left = 0,
                    right = 0,
                    justifyContent = Justify.Center,
                    alignItems = Align.Center,
                    backgroundColor = new Color(0f, 0f, 0f, 0.35f),
                    display = DisplayStyle.None,
                },
            };

            VisualElement dialog = new VisualElement
            {
                style =
                {
                    minWidth = 320,
                    maxWidth = 420,
                    paddingLeft = 16,
                    paddingRight = 16,
                    paddingTop = 14,
                    paddingBottom = 14,
                    backgroundColor = new Color(0.18f, 0.18f, 0.18f, 1f),
                    borderTopLeftRadius = 6,
                    borderTopRightRadius = 6,
                    borderBottomLeftRadius = 6,
                    borderBottomRightRadius = 6,
                    flexDirection = FlexDirection.Column,
                },
            };
            dialog.AddToClassList("dialog-container");

            _titleLabel = new Label
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    fontSize = 14,
                },
            };
            _titleLabel.AddToClassList(StyleConstants.BoldClass);
            dialog.Add(_titleLabel);

            _messageLabel = new Label
            {
                style =
                {
                    whiteSpace = WhiteSpace.Normal,
                    unityTextAlign = TextAnchor.UpperLeft,
                },
            };
            dialog.Add(_messageLabel);

            VisualElement buttonRow = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    justifyContent = Justify.FlexEnd,
                },
            };

            _secondaryButton = CreateButton("Cancel", InvokeSecondary, StyleConstants.ClickableClass);
            buttonRow.Add(_secondaryButton);

            _primaryButton = CreateButton("OK", InvokePrimary, StyleConstants.ActionButtonClass);
            buttonRow.Add(_primaryButton);

            dialog.Add(buttonRow);
            _overlay.Add(dialog);
            _root.Add(_overlay);
        }

        private static Button CreateButton(string text, Action onClick, string styleClass)
        {
            Button button = new Button(() => onClick?.Invoke())
            {
                text = text,
            };
            button.AddToClassList("dialog-button");
            if (!string.IsNullOrWhiteSpace(styleClass))
            {
                button.AddToClassList(styleClass);
            }

            button.style.minWidth = 70;
            button.style.paddingLeft = 8;
            button.style.paddingRight = 8;
            button.style.paddingTop = 4;
            button.style.paddingBottom = 4;
            button.style.marginLeft = 4;
            return button;
        }

        private void InvokePrimary()
        {
            if (!_isShowing)
            {
                return;
            }

            Action callback = _onPrimary;
            Hide();
            callback?.Invoke();
        }

        private void InvokeSecondary()
        {
            if (!_isShowing)
            {
                return;
            }

            Action callback = _onSecondary;
            Hide();
            callback?.Invoke();
        }

        private void Hide()
        {
            if (_overlay == null)
            {
                return;
            }

            _overlay.UnregisterCallback<KeyDownEvent>(HandleKeyDown, TrickleDown.TrickleDown);
            _overlay.style.display = DisplayStyle.None;
            _isShowing = false;
            _onPrimary = null;
            _onSecondary = null;
        }
    }
}
