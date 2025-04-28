namespace WallstopStudios.Editor.DataVisualizer.Components
{
    using System;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed class ConfirmActionPopup : EditorWindow
    {
        private string _message;
        private string _confirmButtonText;
        private string _cancelButtonText;
        private Action<bool> _onCompleteCallback;
        private bool _callbackInvoked;
        private Rect _parentPosition;
        private VisualElement _contentContainer;

        public static ConfirmActionPopup CreateAndConfigureInstance(
            string title,
            string message,
            string confirmButtonText,
            string cancelButtonText,
            Rect parentPosition,
            Action<bool> onComplete
        )
        {
            ConfirmActionPopup window = CreateInstance<ConfirmActionPopup>();
            window.titleContent = new GUIContent(title);
            window._message = message;
            window._confirmButtonText = confirmButtonText ?? "OK";
            window._cancelButtonText = cancelButtonText ?? "Cancel";
            window._onCompleteCallback = onComplete;
            window._parentPosition = parentPosition;
            return window;
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.style.paddingBottom = 15;
            root.style.paddingTop = 15;
            root.style.paddingLeft = 15;
            root.style.paddingRight = 15;

            _contentContainer = new VisualElement
            {
                name = "popup-content-wrapper",
                style =
                {
                    flexGrow = 0,
                    flexShrink = 0,
                    alignSelf = Align.FlexStart,
                },
            };

            root.Add(_contentContainer);

            Label messageLabel = new(_message)
            {
                style =
                {
                    whiteSpace = WhiteSpace.Normal,
                    marginBottom = 20,
                    fontSize = 12,
                },
            };
            _contentContainer.Add(messageLabel);

            VisualElement buttonContainer = new()
            {
                style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd },
            };
            _contentContainer.Add(buttonContainer);

            Button cancelButton = new(() => ClosePopup(false))
            {
                text = _cancelButtonText,
                style = { marginRight = 5 },
            };
            buttonContainer.Add(cancelButton);

            Button confirmButton = new(() => ClosePopup(true)) { text = _confirmButtonText };
            buttonContainer.Add(confirmButton);

            _contentContainer.schedule.Execute(() => cancelButton.Focus()).ExecuteLater(50);
        }

        private void PositionAndResizeWindow()
        {
            VisualElement measuredElement = _contentContainer ?? rootVisualElement;

            float contentWidth = measuredElement.resolvedStyle.width;
            float contentHeight = measuredElement.resolvedStyle.height;

            if (
                float.IsNaN(contentWidth)
                || float.IsNaN(contentHeight)
                || contentWidth <= 0
                || contentHeight <= 0
            )
            {
                rootVisualElement.schedule.Execute(PositionAndResizeWindow).ExecuteLater(20);
                return;
            }

            const float chromeWidthPadding = 10f;
            const float chromeHeightPadding = 35f;

            float desiredWindowWidth = contentWidth + chromeWidthPadding;
            float desiredWindowHeight = contentHeight + chromeHeightPadding;

            desiredWindowWidth = Mathf.Max(desiredWindowWidth, 250f);
            desiredWindowHeight = Mathf.Max(desiredWindowHeight, 100f);

            Vector2 popupSize = new(desiredWindowWidth, desiredWindowHeight);
            float popupX = _parentPosition.x + (_parentPosition.width - popupSize.x) * 0.5f;
            float popupY = _parentPosition.y + (_parentPosition.height - popupSize.y) * 0.5f;

            try
            {
                position = new Rect(popupX, popupY, popupSize.x, popupSize.y);
                minSize = popupSize;
                maxSize = popupSize;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"Error setting popup window position/size for '{titleContent.text}': {ex}"
                );
            }
        }

        private void OnEnable()
        {
            if (rootVisualElement != null)
            {
                rootVisualElement.schedule.Execute(PositionAndResizeWindow).ExecuteLater(1);
            }
            else
            {
                EditorApplication.delayCall += PositionAndResizeWindow;
            }
        }

        private void ClosePopup(bool result)
        {
            if (!_callbackInvoked)
            {
                _onCompleteCallback?.Invoke(result);
                _callbackInvoked = true;
            }

            Close();
        }

        private void OnDestroy()
        {
            if (!_callbackInvoked)
            {
                _onCompleteCallback?.Invoke(false);
            }
        }
    }
}
