namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using UnityEditor;

    /*
        Dedicated offscreen capture host. The suite also drives other EditorWindow types
        through GetWindow and Resources.FindObjectsOfTypeAll sweeps (SplitterWidthPersistence
        anchors on LayoutTestWindow), so the capture surface must be a type nothing else
        looks up by name; otherwise a popup-hosted capture window can be reused as a dock
        anchor it cannot host.
    */
    internal sealed class EditorSurfaceCaptureHostWindow : EditorWindow { }
}
