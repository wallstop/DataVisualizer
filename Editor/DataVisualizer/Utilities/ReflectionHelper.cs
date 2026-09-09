namespace WallstopStudios.DataVisualizer.Helper
{
    using System;
    using System.Reflection;

    internal static class ReflectionHelpers
    {
        public static bool IsAttributeDefined<T>(
            this ICustomAttributeProvider provider,
            out T attribute,
            bool inherit = true
        )
            where T : Attribute
        {
            try
            {
                if (provider.IsDefined(typeof(T), inherit))
                {
                    attribute = (T)provider.GetCustomAttributes(typeof(T), inherit)[0];
                    return true;
                }
            }
            catch
            {
                // Attribute construction can fail for an optional or malformed dependency.
            }

            attribute = default;
            return false;
        }

        public static bool IsAttributeDefined<T>(this ICustomAttributeProvider provider)
            where T : Attribute
        {
            return IsAttributeDefined<T>(provider, inherit: true);
        }

        public static bool IsAttributeDefined<T>(
            this ICustomAttributeProvider provider,
            bool inherit
        )
            where T : Attribute
        {
            return IsAttributeDefined(provider, out T _, inherit);
        }
    }
}
