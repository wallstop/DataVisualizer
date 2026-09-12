namespace WallstopStudios.DataVisualizer.Tests.Runtime
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.DataVisualizer;

    public sealed class BaseDataObjectLifecycleTests
    {
        private static readonly (string PreviousTitle, string ExpectedTitle)[] CloneTitleCases =
        {
            ("Sword", "Sword (Clone)"),
            ("Sword (Clone)", "Sword (Clone 1)"),
            ("Sword (Clone 1)", "Sword (Clone 2)"),
            ("Sword (Clone 99)", "Sword (Clone 100)"),
            ("(Clone)", "(Clone) (Clone)"),
            (" (Clone)", " (Clone 1)"),
            ("Sword (Clone 2 extra)", "Sword (Clone 2 extra) (Clone)"),
        };

        private static readonly (string Input, string Expected)[] TitleCases =
        {
            (null, string.Empty),
            ("   ", string.Empty),
            ("Flame Sword", "Flame Sword"),
        };

        private static readonly (string Input, string Expected)[] DescriptionCases =
        {
            (null, string.Empty),
            ("   ", string.Empty),
            ("Primary loadout", "Primary loadout"),
        };

        [UnityTest]
        public IEnumerator ShouldRunInsidePlayModeWhenPlayModeSuiteExecutes()
        {
            yield return null;

            Assert.IsTrue(Application.isPlaying);
        }

        [UnityTest]
        public IEnumerator ShouldIncrementCloneTitleWhenAfterCloneRuns()
        {
            yield return null;

            foreach ((string previousTitle, string expectedTitle) in CloneTitleCases)
            {
                PlayModeDataObject previous = ScriptableObject.CreateInstance<PlayModeDataObject>();
                PlayModeDataObject clone = ScriptableObject.CreateInstance<PlayModeDataObject>();
                previous.TitleValue = previousTitle;

                clone.AfterClone(previous);

                Assert.AreEqual(
                    expectedTitle,
                    clone.TitleValue,
                    "clone title after cloning previous with title '" + previousTitle + "'"
                );
                Object.Destroy(clone);
                Object.Destroy(previous);
            }
        }

        [UnityTest]
        public IEnumerator ShouldKeepTitleWhenPreviousIsNotBaseDataObject()
        {
            yield return null;

            ScriptableObject stranger = ScriptableObject.CreateInstance<ScriptableObject>();
            PlayModeDataObject clone = ScriptableObject.CreateInstance<PlayModeDataObject>();
            clone.TitleValue = "Original Title";

            clone.AfterClone(stranger);

            Assert.AreEqual("Original Title", clone.TitleValue);
            Object.Destroy(clone);
            Object.Destroy(stranger);
        }

        [UnityTest]
        public IEnumerator ShouldClearAssetGuidWhenBeforeCloneRuns()
        {
            yield return null;

            PlayModeDataObject previous = ScriptableObject.CreateInstance<PlayModeDataObject>();
            previous.AssetGuidValue = "previous-guid";
            PlayModeDataObject clone = ScriptableObject.CreateInstance<PlayModeDataObject>();
            clone.AssetGuidValue = "stale-guid";

            clone.BeforeClone(previous);

            Assert.AreEqual(string.Empty, clone.AssetGuidValue);
            Object.Destroy(clone);
            Object.Destroy(previous);
        }

        [UnityTest]
        public IEnumerator ShouldNormalizeTitleWhenAssigned()
        {
            yield return null;

            foreach ((string input, string expected) in TitleCases)
            {
                PlayModeDataObject dataObject =
                    ScriptableObject.CreateInstance<PlayModeDataObject>();

                dataObject.Title = input;

                Assert.AreEqual(
                    expected,
                    dataObject.TitleValue,
                    "stored title for input '" + input + "'"
                );
                Assert.AreEqual(
                    expected,
                    dataObject.Title,
                    "displayed title for input '" + input + "'"
                );
                Object.Destroy(dataObject);
            }
        }

        [UnityTest]
        public IEnumerator ShouldNormalizeDescriptionWhenAssigned()
        {
            yield return null;

            foreach ((string input, string expected) in DescriptionCases)
            {
                PlayModeDataObject dataObject =
                    ScriptableObject.CreateInstance<PlayModeDataObject>();

                dataObject.Description = input;

                Assert.AreEqual(
                    expected,
                    dataObject.DescriptionValue,
                    "stored description for input '" + input + "'"
                );
                Assert.AreEqual(
                    expected,
                    dataObject.Description,
                    "displayed description for input '" + input + "'"
                );
                Object.Destroy(dataObject);
            }
        }

        [UnityTest]
        public IEnumerator ShouldInvokeOverriddenLifecycleHooks()
        {
            yield return null;

            PlayModeDataObject dataObject = ScriptableObject.CreateInstance<PlayModeDataObject>();

            dataObject.BeforeCreate();
            dataObject.AfterCreate();
            dataObject.BeforeRename("Renamed Name");
            dataObject.AfterRename("Renamed Name");

            Assert.AreEqual(1, dataObject.BeforeCreateInvocations);
            Assert.AreEqual(1, dataObject.AfterCreateInvocations);
            Assert.AreEqual(1, dataObject.BeforeRenameInvocations);
            Assert.AreEqual(1, dataObject.AfterRenameInvocations);
            Assert.AreEqual("Renamed Name", dataObject.LastBeforeRenameName);
            Object.Destroy(dataObject);
        }

        [UnityTest]
        public IEnumerator ShouldReturnZeroWhenComparingInstanceToItself()
        {
            yield return null;

            PlayModeDataObject dataObject = ScriptableObject.CreateInstance<PlayModeDataObject>();

            Assert.AreEqual(0, dataObject.CompareTo(dataObject));
            Object.Destroy(dataObject);
        }

        [UnityTest]
        public IEnumerator ShouldReturnPositiveWhenComparedAgainstNull()
        {
            yield return null;

            PlayModeDataObject dataObject = ScriptableObject.CreateInstance<PlayModeDataObject>();

            Assert.AreEqual(1, dataObject.CompareTo(null));
            Object.Destroy(dataObject);
        }

        [UnityTest]
        public IEnumerator ShouldOrderByTitleAscendingWhenTitlesDiffer()
        {
            yield return null;

            PlayModeDataObject first = ScriptableObject.CreateInstance<PlayModeDataObject>();
            first.TitleValue = "Alpha";
            PlayModeDataObject second = ScriptableObject.CreateInstance<PlayModeDataObject>();
            second.TitleValue = "Beta";

            Assert.That(first.CompareTo(second), Is.Negative);
            Assert.That(second.CompareTo(first), Is.Positive);
            Object.Destroy(first);
            Object.Destroy(second);
        }

        [UnityTest]
        public IEnumerator ShouldUseAssetNameWhenTitlesMatch()
        {
            yield return null;

            PlayModeDataObject first = ScriptableObject.CreateInstance<PlayModeDataObject>();
            first.TitleValue = "Same Title";
            first.name = "Alpha";
            PlayModeDataObject second = ScriptableObject.CreateInstance<PlayModeDataObject>();
            second.TitleValue = "Same Title";
            second.name = "Beta";

            Assert.That(first.CompareTo(second), Is.Negative);
            Assert.That(second.CompareTo(first), Is.Positive);
            Object.Destroy(first);
            Object.Destroy(second);
        }

        [UnityTest]
        public IEnumerator ShouldUseAssetGuidWhenNameAndTitleMatch()
        {
            yield return null;

            PlayModeDataObject first = ScriptableObject.CreateInstance<PlayModeDataObject>();
            first.TitleValue = "Same Title";
            first.name = "Same Name";
            first.AssetGuidValue = "a-guid";
            PlayModeDataObject second = ScriptableObject.CreateInstance<PlayModeDataObject>();
            second.TitleValue = "Same Title";
            second.name = "Same Name";
            second.AssetGuidValue = "b-guid";

            Assert.That(first.CompareTo(second), Is.Negative);
            Assert.That(second.CompareTo(first), Is.Positive);
            Object.Destroy(first);
            Object.Destroy(second);
        }

        [UnityTest]
        public IEnumerator ShouldUseDescriptionWhenIdentityAndNameMatch()
        {
            yield return null;

            PlayModeDataObject first = ScriptableObject.CreateInstance<PlayModeDataObject>();
            first.TitleValue = "Same Title";
            first.name = "Same Name";
            first.AssetGuidValue = "same-guid";
            first.DescriptionValue = "Alpha";
            PlayModeDataObject second = ScriptableObject.CreateInstance<PlayModeDataObject>();
            second.TitleValue = "Same Title";
            second.name = "Same Name";
            second.AssetGuidValue = "same-guid";
            second.DescriptionValue = "Beta";

            Assert.That(first.CompareTo(second), Is.Negative);
            Assert.That(second.CompareTo(first), Is.Positive);
            Object.Destroy(first);
            Object.Destroy(second);
        }
    }
}
