namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class DataProcessorTests : IDataProcessor
    {
        public string Name => nameof(DataProcessorTests);

        public string Description => string.Empty;

        public IEnumerable<Type> Accepts => Array.Empty<Type>();

        private static IEnumerable<ScriptableObject> StreamObjects(int count)
        {
            for (int index = 0; index < count; index++)
            {
                yield return null;
            }
        }

        [Test]
        public void ShouldUseCollectionCountWhenObjectsAreMaterialized()
        {
            IDataProcessor processor = this;

            int count = processor.WillEffect(
                typeof(ScriptableObject),
                new ScriptableObject[] { null, null, null }
            );

            Assert.AreEqual(3, count);
        }

        [Test]
        public void ShouldCountStreamingObjectsWhenObjectsAreDeferred()
        {
            IDataProcessor processor = this;

            int count = processor.WillEffect(typeof(ScriptableObject), StreamObjects(4));

            Assert.AreEqual(4, count);
        }

        [Test]
        public void ShouldRejectNullObjectsWhenCountingAffectedObjects()
        {
            IDataProcessor processor = this;

            Assert.Throws<ArgumentNullException>(() =>
                processor.WillEffect(typeof(ScriptableObject), null)
            );
        }

        public void Process(Type type, IEnumerable<ScriptableObject> objects) { }
    }
}
