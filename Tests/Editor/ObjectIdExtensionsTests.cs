namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Extensions;

    public sealed class ObjectIdExtensionsTests
    {
        [Test]
        public void GetObjectIdStringIsNonEmptyStableAndUnique()
        {
            ScriptableObject first = ScriptableObject.CreateInstance<ScriptableObject>();
            ScriptableObject second = ScriptableObject.CreateInstance<ScriptableObject>();
            try
            {
                string firstId = first.GetObjectIdString();
                string secondId = second.GetObjectIdString();

                Assert.That(firstId, Is.Not.Null.And.Not.Empty);
                Assert.That(secondId, Is.Not.Null.And.Not.Empty);
                Assert.AreEqual(
                    firstId,
                    first.GetObjectIdString(),
                    "The id must be stable across repeated calls on the same object."
                );
                Assert.AreNotEqual(
                    firstId,
                    secondId,
                    "Distinct objects must produce distinct ids."
                );
#if UNITY_6000_4_OR_NEWER
                Assert.IsTrue(
                    ulong.TryParse(firstId, out _),
                    "The EntityId branch must produce a numeric (ulong) id string."
                );
#else
                Assert.IsTrue(
                    int.TryParse(firstId, out _),
                    "The legacy InstanceID branch must produce a numeric (int) id string."
                );
#endif
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }
    }
}
