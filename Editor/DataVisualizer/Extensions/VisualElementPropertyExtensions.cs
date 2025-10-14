namespace WallstopStudios.DataVisualizer.Editor.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using UnityEngine.UIElements;

    internal static class VisualElementPropertyExtensions
    {
        private static readonly ConditionalWeakTable<
            VisualElement,
            Dictionary<string, object>
        > PropertyTable = new ConditionalWeakTable<VisualElement, Dictionary<string, object>>();

        public static void SetProperty(this VisualElement element, string key, object value)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            Dictionary<string, object> propertyBag = PropertyTable.GetValue(
                element,
                _ => new Dictionary<string, object>(StringComparer.Ordinal)
            );

            if (value == null)
            {
                propertyBag.Remove(key);
                return;
            }

            propertyBag[key] = value;
        }

        public static object GetProperty(this VisualElement element, string key)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (!PropertyTable.TryGetValue(element, out Dictionary<string, object> propertyBag))
            {
                return null;
            }

            return propertyBag.TryGetValue(key, out object value) ? value : null;
        }

        public static bool TryGetProperty<TValue>(
            this VisualElement element,
            string key,
            out TValue value
        )
        {
            object storedValue = GetProperty(element, key);
            if (storedValue is TValue castValue)
            {
                value = castValue;
                return true;
            }

            value = default;
            return false;
        }
    }
}
