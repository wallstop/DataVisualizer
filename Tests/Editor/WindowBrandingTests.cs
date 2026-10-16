namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    public sealed class WindowBrandingTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ShouldShowBrandWhenWindowIsEnabled(bool restoreSerializedTitle)
        {
            using (TestCleanupScope cleanup = new())
            {
                DataVisualizerWindow window = ScriptableObject.CreateInstance<DataVisualizerWindow>();
                cleanup.Defer(() => Object.DestroyImmediate(window));
                DataVisualizerWindow target = window;
                if (restoreSerializedTitle)
                {
                    window.titleContent = new GUIContent("Data Visualizer");
                    target = Object.Instantiate(window);
                    cleanup.Defer(() => Object.DestroyImmediate(target));
                }

                Assert.That(target.titleContent.text, Is.EqualTo("DxVisualizer"));
                Assert.That(target.titleContent.image != null, "The editor tab must show the approved brand mark.");
                string iconPath = AssetDatabase.GetAssetPath(target.titleContent.image);
                Assert.That(iconPath, Does.EndWith("/DxVisualizer.png").Or.EndWith("/DxVisualizer@2x.png"));
            }
        }
    }
}
