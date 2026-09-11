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
            throw new AssertionException("A lower-priority provider was called unexpectedly.");
        }

        [Test]
        public void Should_CenterRect_When_PlacementAreaHasNonzeroOrigin()
        {
            Rect actual = MonitorUtility.CalculateCenteredRect(
                new Rect(1920, 100, 1600, 900),
                800,
                600
            );

            Assert.That(actual, Is.EqualTo(new Rect(2320, 250, 800, 600)));
        }

        [Test]
        public void Should_CenterRect_When_PlacementAreaHasNegativeOrigin()
        {
            Rect actual = MonitorUtility.CalculateCenteredRect(
                new Rect(-1920, -200, 1920, 1080),
                1000,
                700
            );

            Assert.That(actual, Is.EqualTo(new Rect(-1460, -10, 1000, 700)));
        }

        [TestCase(false, false, true, TestName = "New floating window")]
        [TestCase(true, false, false, TestName = "Previously placed floating window")]
        [TestCase(false, true, false, TestName = "Restored docked window")]
        [TestCase(true, true, false, TestName = "Previously placed docked window")]
        public void Should_ApplyInitialPlacement_OnlyForNewFloatingWindow(
            bool initialSizeApplied,
            bool isDocked,
            bool expected
        )
        {
            bool actual = MonitorUtility.ShouldApplyInitialPlacement(initialSizeApplied, isDocked);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Should_ReturnPreferredRect_When_TwoProvidersAreUsable()
        {
            Rect expected = new(-1720, 80, 1600, 900);

            bool result = MonitorUtility.TryResolveMonitorRect(
                () => expected,
                ThrowUnexpectedProviderCall,
                out Rect actual
            );

            Assert.That(result, Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Should_ReturnFallbackRect_When_PreferredProviderFails()
        {
            Rect expected = new(0, 0, 2560, 1440);

            bool result = MonitorUtility.TryResolveMonitorRect(
                ThrowProviderException,
                () => expected,
                out Rect actual
            );

            Assert.That(result, Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Should_ReturnFalse_When_BothProvidersFail()
        {
            bool result = MonitorUtility.TryResolveMonitorRect(
                () => Rect.zero,
                ThrowProviderException,
                out Rect actual
            );

            Assert.That(result, Is.False);
            Assert.That(actual, Is.EqualTo(default(Rect)));
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
        public void Should_ReturnFallbackRect_When_PreferredRectIsInvalid(
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
                out Rect actual
            );

            Assert.That(result, Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Should_ReturnFallbackRect_When_PreferredProviderThrows()
        {
            Rect expected = new(40, 50, 1600, 900);

            bool result = MonitorUtility.TryResolveMonitorRect(
                ThrowProviderException,
                () => expected,
                out Rect actual
            );

            Assert.That(result, Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }
    }
}
