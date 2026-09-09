namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class LayoutGeometryTests
    {
        [TestCase(420f, 350f, 320f, 420f)]
        [TestCase(200f, 350f, 320f, 320f)]
        [TestCase(float.NaN, 350f, 320f, 350f)]
        [TestCase(float.PositiveInfinity, 350f, 320f, 350f)]
        [TestCase(float.NegativeInfinity, 350f, 320f, 350f)]
        public void Should_ReturnFiniteClampedPaneWidth_WhenPersistedValueIsInvalidOrSmall(
            float persistedWidth,
            float defaultWidth,
            float minimumWidth,
            float expectedWidth
        )
        {
            float result = LayoutGeometry.ClampPersistedPaneWidth(
                persistedWidth,
                defaultWidth,
                minimumWidth
            );

            Assert.That(result, Is.EqualTo(expectedWidth));
        }

        [TestCase(250.4f, 250)]
        [TestCase(250.6f, 251)]
        [TestCase(0f, 1)]
        [TestCase(float.NaN, 1)]
        public void Should_RoundPositivePaneDimension_WhenCreatingSplitter(
            float width,
            int expectedDimension
        )
        {
            Assert.That(
                LayoutGeometry.ToInitialPaneDimension(width),
                Is.EqualTo(expectedDimension)
            );
        }
    }
}
