namespace WallstopStudios.DataVisualizer
{
    using System;

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class CustomVisualProviderAttribute : Attribute { }
}
