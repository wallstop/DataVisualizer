namespace WallstopStudios.DataVisualizer
{
    using System;

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class CustomDataVisualization : Attribute
    {
        public string Namespace { get; set; }
#if ODIN_INSPECTOR
        public bool UseOdinInspector { get; set; } = true;
#endif
    }
}
