namespace WallstopStudios.Editor.DataVisualizer.Extensions
{
    using UnityEngine.UIElements;

    public static class UIExtensions
    {
        public static void SetPlaceholderText(this TextField textField, string placeholder)
        {
            string placeholderClass = TextField.ussClassName + "__placeholder";

            OnFocusOut();
            textField.RegisterCallback<FocusInEvent>(_ => OnFocusIn());
            textField.RegisterCallback<FocusOutEvent>(_ => OnFocusOut());

            return;

            void OnFocusIn()
            {
                if (!textField.ClassListContains(placeholderClass))
                {
                    return;
                }

                textField.value = string.Empty;
                textField.RemoveFromClassList(placeholderClass);
            }

            void OnFocusOut()
            {
                if (!string.IsNullOrEmpty(textField.text))
                {
                    return;
                }

                textField.SetValueWithoutNotify(placeholder);
                textField.AddToClassList(placeholderClass);
            }
        }
    }
}
