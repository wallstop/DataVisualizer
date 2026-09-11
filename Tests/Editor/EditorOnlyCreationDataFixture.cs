namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using UnityEngine;

    /*
        This is the sole intentional one-type-per-file exception. The second Unity type reproduces
        external projects that place multiple ScriptableObjects in one source file, which makes
        Unity omit otherwise valid assets from type-filter queries.
    */
    internal abstract class EditorOnlyCreationDataFileMarker : ScriptableObject { }

    public class EditorOnlyCreationData : ScriptableObject { }
}
