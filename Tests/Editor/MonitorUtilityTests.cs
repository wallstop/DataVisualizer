namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class MonitorUtilityTests
    {
        [Test]
        public void Should_ReturnUsableMonitorRect_When_EditorIsRunning()
        {
            Rect rect = MonitorUtility.GetPrimaryMonitorRect();

            Assert.That(rect.width, Is.GreaterThan(0));
            Assert.That(rect.height, Is.GreaterThan(0));
            Assert.That(float.IsNaN(rect.x), Is.False);
            Assert.That(float.IsNaN(rect.y), Is.False);
        }
    }
}
