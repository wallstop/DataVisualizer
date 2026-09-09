namespace WallstopStudios.DataVisualizer.Tests.Runtime
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.DataVisualizer;

    public sealed class BaseDataObjectLifecycleTests
    {
        private TestDataObject clone;
        private TestDataObject source;

        [SetUp]
        public void SetUp()
        {
            source = ScriptableObject.CreateInstance<TestDataObject>();
            clone = ScriptableObject.CreateInstance<TestDataObject>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(clone);
        }

        [Test]
        public void Should_ExposeLifecycleContracts_WhenCreated()
        {
            Assert.That(source, Is.InstanceOf<ICreatable>());
            Assert.That(source, Is.InstanceOf<IDuplicable>());
            Assert.That(source, Is.InstanceOf<IRenamable>());
            Assert.That(source, Is.InstanceOf<IGUIProvider>());
            Assert.That(source, Is.InstanceOf<IDisplayable>());
        }

        [Test]
        public void Should_ClearIdentityAndIncrementTitle_WhenCloneHooksRun()
        {
            source.Title = "Sword";
            clone.SetAssetGuid("stale-guid");

            clone.BeforeClone(source);
            clone.AfterClone(source);

            Assert.That(clone.Id, Is.Empty);
            Assert.That(clone.Title, Is.EqualTo("Sword (Clone)"));
        }

        [Test]
        public void Should_PreserveSerializedAssetState_WhenStateRoundTrips()
        {
            source.SetAssetGuid("runtime-guid");
            source.Title = "Sword";
            source.Description = "A serialized runtime fixture.";

            string serializedState = JsonUtility.ToJson(source);
            JsonUtility.FromJsonOverwrite(serializedState, clone);

            Assert.That(clone.Id, Is.EqualTo("runtime-guid"));
            Assert.That(clone.Title, Is.EqualTo("Sword"));
            Assert.That(clone.Description, Is.EqualTo("A serialized runtime fixture."));
        }

        [TestCase("Sword (Clone)", "Sword (Clone 1)")]
        [TestCase("Sword (Clone 4)", "Sword (Clone 5)")]
        public void Should_ContinueCloneNumbering_WhenSourceAlreadyHasCloneSuffix(
            string sourceTitle,
            string expectedCloneTitle
        )
        {
            source.Title = sourceTitle;

            clone.BeforeClone(source);
            clone.AfterClone(source);

            Assert.That(clone.Title, Is.EqualTo(expectedCloneTitle));
        }

        private sealed class TestDataObject : BaseDataObject
        {
            public void SetAssetGuid(string value)
            {
                _assetGuid = value;
            }
        }
    }
}
