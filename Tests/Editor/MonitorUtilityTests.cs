namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Utilities;

    public sealed class MonitorUtilityTests
    {
        private static Rect ThrowProviderException()
        {
            throw new InvalidOperationException("Expected test provider failure.");
        }

        private static Rect ThrowUnexpectedProviderCall()
        {
            Assert.Fail("A lower-priority provider was called unexpectedly.");
            return Rect.zero;
        }

        [Test]
        public void Should_ReturnPlatformRect_When_PlatformRectIsUsable()
        {
            Rect expected = new(-1920, 0, 1920, 1080);

            bool result = MonitorUtility.TryResolveMonitorRect(
                () => expected,
                ThrowUnexpectedProviderCall,
                ThrowUnexpectedProviderCall,
                out Rect actual
            );

            Assert.That(result, Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCase(0f, 0f, 0f, 1080f, TestName = "Zero width")]
        [TestCase(0f, 0f, -1f, 1080f, TestName = "Negative width")]
        [TestCase(0f, 0f, 1920f, 0f, TestName = "Zero height")]
        [TestCase(0f, 0f, 1920f, -1f, TestName = "Negative height")]
        [TestCase(float.NaN, 0f, 1920f, 1080f, TestName = "NaN x")]
        [TestCase(0f, float.NaN, 1920f, 1080f, TestName = "NaN y")]
        [TestCase(0f, 0f, float.NaN, 1080f, TestName = "NaN width")]
        [TestCase(0f, 0f, 1920f, float.NaN, TestName = "NaN height")]
        [TestCase(float.PositiveInfinity, 0f, 1920f, 1080f, TestName = "Infinite x")]
        [TestCase(0f, float.NegativeInfinity, 1920f, 1080f, TestName = "Infinite y")]
        [TestCase(0f, 0f, float.PositiveInfinity, 1080f, TestName = "Infinite width")]
        [TestCase(0f, 0f, 1920f, float.NegativeInfinity, TestName = "Infinite height")]
        public void Should_ReturnMainWindowRect_When_PlatformRectIsInvalid(
            float x,
            float y,
            float width,
            float height
        )
        {
            Rect expected = new(40, 50, 1600, 900);

            bool result = MonitorUtility.TryResolveMonitorRect(
                () => new Rect(x, y, width, height),
                () => expected,
                ThrowUnexpectedProviderCall,
                out Rect actual
            );

            Assert.That(result, Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Should_ReturnMainWindowRect_When_PlatformProviderThrows()
        {
            Rect expected = new(40, 50, 1600, 900);

            bool result = MonitorUtility.TryResolveMonitorRect(
                ThrowProviderException,
                () => expected,
                ThrowUnexpectedProviderCall,
                out Rect actual
            );

            Assert.That(result, Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Should_ReturnCurrentResolution_When_EarlierProvidersFail()
        {
            Rect expected = new(0, 0, 2560, 1440);

            bool result = MonitorUtility.TryResolveMonitorRect(
                () => Rect.zero,
                ThrowProviderException,
                () => expected,
                out Rect actual
            );

            Assert.That(result, Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Should_ReturnFalse_When_AllProvidersFail()
        {
            bool result = MonitorUtility.TryResolveMonitorRect(
                () => Rect.zero,
                () => new Rect(0, 0, float.NaN, 1080),
                ThrowProviderException,
                out Rect actual
            );

            Assert.That(result, Is.False);
            Assert.That(actual, Is.EqualTo(default(Rect)));
        }
    }
}
