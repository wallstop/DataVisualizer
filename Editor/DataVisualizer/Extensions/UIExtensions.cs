namespace WallstopStudios.Editor.DataVisualizer.Extensions
{
    using System.Collections.Generic;
    using Styles;
    using UnityEngine.UIElements;

    public static class UIExtensions
    {
        private static readonly string PlaceholderTextFieldClass =
            TextField.ussClassName + "__placeholder";

        public static void SetPlaceholderText(
            this TextField textField,
            string placeholder,
            bool clearExistingText = true
        )
        {
            if (clearExistingText)
            {
                textField.value = string.Empty;
            }

            IVisualElementScheduledItem blinkSchedule = null;
            OnFocusOut();
            textField.RegisterCallback<FocusInEvent>(_ => OnFocusIn());
            textField.RegisterCallback<FocusOutEvent>(_ => OnFocusOut());
            return;

            void OnFocusIn()
            {
                if (!textField.ClassListContains(PlaceholderTextFieldClass))
                {
                    return;
                }

                textField.value = string.Empty;
                textField.RemoveFromClassList(PlaceholderTextFieldClass);

                blinkSchedule?.Pause();
                blinkSchedule = null;

                bool shouldRenderCursor = true;
                blinkSchedule = textField
                    .schedule.Execute(() =>
                    {
                        textField.EnableInClassList(
                            StyleConstants.TransparentCursorClass,
                            shouldRenderCursor
                        );
                        textField.EnableInClassList(
                            StyleConstants.StyledCursorClass,
                            !shouldRenderCursor
                        );
                        shouldRenderCursor = !shouldRenderCursor;
                    })
                    .Every(StyleConstants.CursorBlinkRateMilliseconds);
            }

            void OnFocusOut()
            {
                if (!string.IsNullOrEmpty(textField.text))
                {
                    return;
                }

                textField.SetValueWithoutNotify(placeholder);
                textField.AddToClassList(PlaceholderTextFieldClass);
                blinkSchedule?.Pause();
                blinkSchedule = null;
                textField.EnableInClassList(StyleConstants.TransparentCursorClass, false);
                textField.EnableInClassList(StyleConstants.StyledCursorClass, true);
            }
        }

        public static IEnumerable<VisualElement> IterateChildrenRecursively(
            this VisualElement element
        )
        {
            foreach (VisualElement child in element.Children())
            {
                yield return child;
                foreach (VisualElement grandChild in IterateChildrenRecursively(child))
                {
                    yield return grandChild;
                }
            }
        }
    }
}
