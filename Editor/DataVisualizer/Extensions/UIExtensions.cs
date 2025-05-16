namespace WallstopStudios.DataVisualizer.Editor.Extensions
{
    using System;
    using System.Collections.Generic;
    using Styles;
    using UnityEngine.UIElements;

    internal static class UIExtensions
    {
        private static readonly string PlaceholderTextFieldClass =
            TextField.ussClassName + "__placeholder";

        /// <summary>
        /// Sets up placeholder text to show when the TextField's text is empty.
        /// WARNING: Should only be called once.
        /// </summary>
        /// <param name="textField">Target TextField.</param>
        /// <param name="placeholder">Text to use as a placeholder.</param>
        /// <param name="clearExistingText">If true, will set the text in the TextField to the empty string.</param>
        /// <param name="changeValueOnFocus">If true, will reset the text to the placeHolder when focus is changed (out).</param>
        public static void SetPlaceholderText(
            this TextField textField,
            string placeholder,
            bool clearExistingText = true,
            bool changeValueOnFocus = true
        )
        {
            if (clearExistingText)
            {
                textField.value = string.Empty;
            }

            IVisualElementScheduledItem blinkSchedule = null;
            textField.SetValueWithoutNotify(placeholder);
            OnFocusOut();
            textField.RegisterCallback<FocusInEvent>(_ => OnFocusIn());
            textField.RegisterCallback<FocusOutEvent>(_ => OnFocusOut());

            return;

            void OnFocusIn()
            {
                if (string.Equals(textField.value, placeholder, StringComparison.Ordinal))
                {
                    textField.SetValueWithoutNotify(string.Empty);
                }

                textField.RemoveFromClassList(PlaceholderTextFieldClass);
                blinkSchedule?.Pause();

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
                blinkSchedule?.Pause();
                blinkSchedule = null;
                if (string.IsNullOrWhiteSpace(textField.value))
                {
                    textField.SetValueWithoutNotify(placeholder);
                }

                if (changeValueOnFocus)
                {
                    textField.SetValueWithoutNotify(placeholder);
                    textField.AddToClassList(PlaceholderTextFieldClass);
                    textField.EnableInClassList(StyleConstants.TransparentCursorClass, false);
                    textField.EnableInClassList(StyleConstants.StyledCursorClass, true);
                }
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
