namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.DataVisualizer.Editor.Utilities;
    using DataVisualizerWindow = WallstopStudios.DataVisualizer.Editor.DataVisualizer;

    public sealed class MonitorUtilityTests
    {
        private const string InitialSizeAppliedKey =
            "WallstopStudios.Editor.DataVisualizer.InitialSizeApplied";
        private const string PreferredWindowSizeKey =
            "WallstopStudios.Editor.DataVisualizer.PreferredWindowSize";
        private const string TemporaryWindowClampSizeKey =
            "WallstopStudios.Editor.DataVisualizer.TemporaryWindowClampSize";

        private static Rect ThrowProviderException()
        {
            throw new InvalidOperationException("Expected test provider failure.");
        }

        private static Rect ThrowUnexpectedProviderCall()
        {
            throw new AssertionException("A lower-priority provider was called unexpectedly.");
        }

        private static void CloseDataVisualizerWindows()
        {
            foreach (
                DataVisualizerWindow window in Resources.FindObjectsOfTypeAll<DataVisualizerWindow>()
            )
            {
                window.Close();
            }
        }

        private static void RestoreBoolPreference(string key, bool existed, bool value)
        {
            if (existed)
            {
                EditorPrefs.SetBool(key, value);
            }
            else
            {
                EditorPrefs.DeleteKey(key);
            }
        }

        private static void RestoreStringPreference(string key, bool existed, string value)
        {
            if (existed)
            {
                EditorPrefs.SetString(key, value);
            }
            else
            {
                EditorPrefs.DeleteKey(key);
            }
        }

        [Test]
        public void ShouldCenterRectWhenPlacementAreaHasNonzeroOrigin()
        {
            Rect actual = MonitorUtility.CalculateCenteredRect(
                new Rect(1920, 100, 1600, 900),
                800,
                600
            );

            Assert.That(actual, Is.EqualTo(new Rect(2320, 250, 800, 600)));
        }

        [Test]
        public void ShouldCenterRectWhenPlacementAreaHasNegativeOrigin()
        {
            Rect actual = MonitorUtility.CalculateCenteredRect(
                new Rect(-1920, -200, 1920, 1080),
                1000,
                700
            );

            Assert.That(actual, Is.EqualTo(new Rect(-1460, -10, 1000, 700)));
        }

        [TestCase(
            100f,
            50f,
            800f,
            600f,
            1200f,
            900f,
            100f,
            50f,
            800f,
            600f,
            TestName = "Oversized on every edge"
        )]
        [TestCase(
            -1600f,
            -100f,
            1000f,
            700f,
            1600f,
            500f,
            -1600f,
            0f,
            1000f,
            500f,
            TestName = "Negative origin and oversized width"
        )]
        [TestCase(
            80f,
            40f,
            1760f,
            960f,
            1600f,
            1200f,
            160f,
            40f,
            1600f,
            960f,
            TestName = "Inset reserved edges and oversized height"
        )]
        public void ShouldConstrainCenteredRectWhenPreferredSizeExceedsPlacementArea(
            float areaX,
            float areaY,
            float areaWidth,
            float areaHeight,
            float preferredWidth,
            float preferredHeight,
            float expectedX,
            float expectedY,
            float expectedWidth,
            float expectedHeight
        )
        {
            Rect actual = MonitorUtility.CalculateCenteredRect(
                new Rect(areaX, areaY, areaWidth, areaHeight),
                preferredWidth,
                preferredHeight
            );

            Assert.That(
                actual,
                Is.EqualTo(new Rect(expectedX, expectedY, expectedWidth, expectedHeight))
            );
        }

        [TestCase(false, 400f, 300f, 860f, 480f, TestName = "Normal floating window")]
        [TestCase(false, 300f, 200f, 860f, 480f, TestName = "Docked window")]
        [TestCase(true, 400f, 300f, 400f, 300f, TestName = "Active temporary clamp")]
        [TestCase(true, 1200f, 900f, 860f, 480f, TestName = "Nonconstraining clamp")]
        [TestCase(true, 0f, float.NaN, 860f, 480f, TestName = "Invalid clamp")]
        public void ShouldRelaxMinimumSizeOnlyForActiveTemporaryClamp(
            bool temporaryClampIsActive,
            float temporaryWidth,
            float temporaryHeight,
            float expectedWidth,
            float expectedHeight
        )
        {
            Vector2 actual = MonitorUtility.CalculateWindowMinimumSize(
                new Vector2(860f, 480f),
                new Vector2(temporaryWidth, temporaryHeight),
                temporaryClampIsActive
            );

            Assert.That(actual, Is.EqualTo(new Vector2(expectedWidth, expectedHeight)));
        }

        [Test]
        public void ShouldSelectExactPreferredPairsAcrossClampAndUserResizes()
        {
            Vector2 minimumSize = new(860f, 480f);
            Vector2 initialLarge = MonitorUtility.SelectPreferredSize(
                default,
                false,
                new Vector2(1200f, 900f),
                minimumSize
            );
            Vector2 temporarilyClamped = MonitorUtility.SelectPreferredSize(
                initialLarge,
                true,
                new Vector2(700f, 480f),
                minimumSize
            );
            Vector2 intentionallyShrunk = MonitorUtility.NormalizePreferredSize(
                new Vector2(1000f, 700f),
                minimumSize
            );
            Vector2 resizedLarger = MonitorUtility.NormalizePreferredSize(
                new Vector2(1600f, 1000f),
                minimumSize
            );

            Assert.That(initialLarge, Is.EqualTo(new Vector2(1200f, 900f)));
            Assert.That(temporarilyClamped, Is.EqualTo(initialLarge));
            Assert.That(intentionallyShrunk, Is.EqualTo(new Vector2(1000f, 700f)));
            Assert.That(resizedLarger, Is.EqualTo(new Vector2(1600f, 1000f)));
        }

        [Test]
        public void ShouldPreserveInitialPreferredSizeWhenPlacementIsConstrained()
        {
            Vector2 preferredSize = MonitorUtility.SelectPreferredSize(
                default,
                false,
                new Vector2(1200f, 900f),
                new Vector2(860f, 480f)
            );
            Rect constrainedRect = MonitorUtility.CalculateCenteredRect(
                new Rect(100f, 50f, 700f, 500f),
                preferredSize.x,
                preferredSize.y
            );
            string serializedPreferredSize = MonitorUtility.SerializeSize(preferredSize);
            bool parsed = MonitorUtility.TryParseSize(
                serializedPreferredSize,
                out Vector2 restoredPreferredSize
            );

            Assert.That(constrainedRect.size, Is.EqualTo(new Vector2(700f, 500f)));
            Assert.That(parsed, Is.True);
            Assert.That(restoredPreferredSize, Is.EqualTo(new Vector2(1200f, 900f)));
        }

        [UnityTest]
        public IEnumerator ShouldPreservePreferredSizeThroughActualPlacementAndWindowRecreation()
        {
            bool hadInitialSizeApplied = EditorPrefs.HasKey(InitialSizeAppliedKey);
            bool initialSizeApplied = EditorPrefs.GetBool(InitialSizeAppliedKey);
            bool hadPreferredSize = EditorPrefs.HasKey(PreferredWindowSizeKey);
            string preferredSize = EditorPrefs.GetString(PreferredWindowSizeKey);
            bool hadTemporaryClampSize = EditorPrefs.HasKey(TemporaryWindowClampSizeKey);
            string temporaryClampSize = EditorPrefs.GetString(TemporaryWindowClampSizeKey);
            Vector2 oversizedPreference = new(10000f, 9000f);
            string serializedOversizedPreference = MonitorUtility.SerializeSize(
                oversizedPreference
            );

            try
            {
                CloseDataVisualizerWindows();
                yield return null;
                EditorPrefs.SetBool(InitialSizeAppliedKey, false);
                EditorPrefs.SetString(PreferredWindowSizeKey, serializedOversizedPreference);
                EditorPrefs.DeleteKey(TemporaryWindowClampSizeKey);

                Rect placementArea = EditorGUIUtility.GetMainWindowPosition();
                DataVisualizerWindow window =
                    ScriptableObject.CreateInstance<DataVisualizerWindow>();
                window.ShowUtility();
                yield return null;
                Assert.That(window.docked, Is.False);
                DataVisualizerWindow.ShowWindow();

                Assert.That(
                    EditorPrefs.GetString(PreferredWindowSizeKey),
                    Is.EqualTo(serializedOversizedPreference)
                );
                Rect actualPosition = window.position;
                Assert.That(actualPosition.width, Is.LessThanOrEqualTo(placementArea.width));
                Assert.That(actualPosition.height, Is.LessThanOrEqualTo(placementArea.height));
                window.Close();
                Assert.That(
                    MonitorUtility.TryParseSize(
                        EditorPrefs.GetString(TemporaryWindowClampSizeKey),
                        out Vector2 persistedClampSize
                    ),
                    Is.True
                );
                Assert.That(
                    MonitorUtility.IsSameSize(persistedClampSize, actualPosition.size),
                    Is.True
                );
                Assert.That(
                    window.minSize,
                    Is.EqualTo(
                        MonitorUtility.CalculateWindowMinimumSize(
                            new Vector2(860f, 480f),
                            actualPosition.size,
                            true
                        )
                    )
                );
                Assert.That(
                    EditorPrefs.GetString(PreferredWindowSizeKey),
                    Is.EqualTo(serializedOversizedPreference)
                );

                yield return null;
                DataVisualizerWindow restoredWindow =
                    ScriptableObject.CreateInstance<DataVisualizerWindow>();
                restoredWindow.position = actualPosition;
                restoredWindow.ShowUtility();
                for (int frame = 0; frame < 20; frame++)
                {
                    yield return null;
                }

                Assert.That(
                    EditorPrefs.GetString(PreferredWindowSizeKey),
                    Is.EqualTo(serializedOversizedPreference)
                );
                Assert.That(
                    MonitorUtility.TryParseSize(
                        EditorPrefs.GetString(TemporaryWindowClampSizeKey),
                        out Vector2 restoredClampSize
                    ),
                    Is.True
                );
                Assert.That(
                    MonitorUtility.IsSameSize(restoredClampSize, restoredWindow.position.size),
                    Is.True
                );
                Assert.That(
                    restoredWindow.minSize,
                    Is.EqualTo(
                        MonitorUtility.CalculateWindowMinimumSize(
                            new Vector2(860f, 480f),
                            restoredClampSize,
                            true
                        )
                    )
                );
            }
            finally
            {
                CloseDataVisualizerWindows();
                RestoreBoolPreference(
                    InitialSizeAppliedKey,
                    hadInitialSizeApplied,
                    initialSizeApplied
                );
                RestoreStringPreference(PreferredWindowSizeKey, hadPreferredSize, preferredSize);
                RestoreStringPreference(
                    TemporaryWindowClampSizeKey,
                    hadTemporaryClampSize,
                    temporaryClampSize
                );
            }
        }

        [Test]
        public void ShouldNeverSynthesizePreferredSizeFromDifferentPairs()
        {
            Vector2 selected = MonitorUtility.SelectPreferredSize(
                new Vector2(1600f, 600f),
                true,
                new Vector2(1000f, 900f),
                new Vector2(860f, 480f)
            );
            Vector2 captured = MonitorUtility.NormalizePreferredSize(
                new Vector2(1000f, 900f),
                new Vector2(860f, 480f)
            );

            Assert.That(selected, Is.EqualTo(new Vector2(1600f, 600f)));
            Assert.That(captured, Is.EqualTo(new Vector2(1000f, 900f)));
        }

        [TestCase(800f, 450f, 860f, 480f, TestName = "Both dimensions below minimum")]
        [TestCase(1000f, 450f, 1000f, 480f, TestName = "Height below minimum")]
        [TestCase(800f, 700f, 860f, 700f, TestName = "Width below minimum")]
        public void ShouldNormalizeCapturedPreferredSizeToOrdinaryMinimum(
            float width,
            float height,
            float expectedWidth,
            float expectedHeight
        )
        {
            Vector2 actual = MonitorUtility.NormalizePreferredSize(
                new Vector2(width, height),
                new Vector2(860f, 480f)
            );

            Assert.That(actual, Is.EqualTo(new Vector2(expectedWidth, expectedHeight)));
        }

        [TestCase(float.NaN, 900f, TestName = "NaN saved width")]
        [TestCase(1200f, float.PositiveInfinity, TestName = "Infinite saved height")]
        [TestCase(-100f, 900f, TestName = "Negative saved width")]
        [TestCase(1200f, 0f, TestName = "Zero saved height")]
        public void ShouldRejectInvalidSavedPreferredSizeAsOnePair(
            float savedWidth,
            float savedHeight
        )
        {
            Vector2 actual = MonitorUtility.SelectPreferredSize(
                new Vector2(savedWidth, savedHeight),
                true,
                new Vector2(1100f, 700f),
                new Vector2(860f, 480f)
            );

            Assert.That(actual, Is.EqualTo(new Vector2(1100f, 700f)));
        }

        [TestCase(true, true, false, false, TestName = "Pending package placement")]
        [TestCase(false, false, false, false, TestName = "Initial enable layout")]
        [TestCase(false, true, true, false, TestName = "Docked resize")]
        [TestCase(false, true, false, true, TestName = "User floating resize")]
        public void ShouldCapturePreferredSizeOnlyFromUserFloatingGeometry(
            bool packagePlacementPending,
            bool hasObservedInitialGeometry,
            bool isDocked,
            bool expected
        )
        {
            bool actual = MonitorUtility.ShouldCapturePreferredSize(
                packagePlacementPending,
                hasObservedInitialGeometry,
                isDocked
            );

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void ShouldRoundTripWindowSizeAsOneInvariantValue()
        {
            Vector2 expected = new(1234.5f, 678.25f);

            string serialized = MonitorUtility.SerializeSize(expected);
            bool parsed = MonitorUtility.TryParseSize(serialized, out Vector2 actual);

            Assert.That(serialized, Is.EqualTo("1234.5,678.25"));
            Assert.That(parsed, Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("1200")]
        [TestCase("1200,900,700")]
        [TestCase("NaN,900")]
        [TestCase("1200,Infinity")]
        [TestCase("-1200,900")]
        public void ShouldRejectInvalidSerializedWindowSize(string serialized)
        {
            bool parsed = MonitorUtility.TryParseSize(serialized, out Vector2 actual);

            Assert.That(parsed, Is.False);
            Assert.That(actual, Is.EqualTo(default(Vector2)));
        }

        [TestCase(700f, 480f, true, TestName = "Unchanged temporary clamp")]
        [TestCase(701f, 480f, false, TestName = "User-resized width")]
        [TestCase(700f, 481f, false, TestName = "User-resized height")]
        public void ShouldRecognizeOnlyMatchingTemporaryClampSize(
            float currentWidth,
            float currentHeight,
            bool expected
        )
        {
            bool actual = MonitorUtility.IsSameSize(
                new Vector2(currentWidth, currentHeight),
                new Vector2(700f, 480f)
            );

            Assert.That(actual, Is.EqualTo(expected));
        }

        [TestCase(false, false, true, TestName = "New floating window")]
        [TestCase(true, false, false, TestName = "Previously placed floating window")]
        [TestCase(false, true, false, TestName = "Restored docked window")]
        [TestCase(true, true, false, TestName = "Previously placed docked window")]
        public void ShouldApplyInitialPlacementOnlyForNewFloatingWindow(
            bool initialSizeApplied,
            bool isDocked,
            bool expected
        )
        {
            bool actual = MonitorUtility.ShouldApplyInitialPlacement(initialSizeApplied, isDocked);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void ShouldReturnPreferredRectWhenTwoProvidersAreUsable()
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
        public void ShouldReturnFallbackRectWhenPreferredProviderFails()
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
        public void ShouldReturnFalseWhenBothProvidersFail()
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
        public void ShouldReturnFallbackRectWhenPreferredRectIsInvalid(
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
        public void ShouldReturnFallbackRectWhenPreferredProviderThrows()
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
