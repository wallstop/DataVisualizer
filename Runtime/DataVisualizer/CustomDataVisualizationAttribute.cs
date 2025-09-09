namespace WallstopStudios.DataVisualizer
{
    using System;

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class CustomDataVisualizationAttribute : Attribute
    {
        public string Namespace { get; set; }

        public string TypeName { get; set; }
#if ODIN_INSPECTOR
        public bool UseOdinInspector { get; set; } = true;
#endif
    }
}
