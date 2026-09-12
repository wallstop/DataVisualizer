namespace WallstopStudios.DataVisualizer.Tests.Runtime
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.DataVisualizer;

    public sealed class BaseDataObjectSerializationTests
    {
        [UnityTest]
        public IEnumerator ShouldRoundTripSerializedFieldsThroughJsonUtility()
        {
            yield return null;

            PlayModeDataObject source = ScriptableObject.CreateInstance<PlayModeDataObject>();
            source.AssetGuidValue = "0123456789abcdef0123456789abcdef";
            source.TitleValue = "Flame Sword";
            source.DescriptionValue = "Primary loadout";
            PlayModeDataObject target = ScriptableObject.CreateInstance<PlayModeDataObject>();

            string json = JsonUtility.ToJson(source);
            JsonUtility.FromJsonOverwrite(json, target);

            Assert.AreEqual(source.AssetGuidValue, target.AssetGuidValue);
            Assert.AreEqual(source.TitleValue, target.TitleValue);
            Assert.AreEqual(source.DescriptionValue, target.DescriptionValue);
            Assert.AreEqual(source.Id, target.Id);
            Object.Destroy(source);
            Object.Destroy(target);
        }

        [UnityTest]
        public IEnumerator ShouldKeepDefaultsWhenSerializedFieldsAreMissing()
        {
            yield return null;

            PlayModeDataObject target = ScriptableObject.CreateInstance<PlayModeDataObject>();

            JsonUtility.FromJsonOverwrite("{}", target);

            Assert.IsNull(target.AssetGuidValue);
            Assert.AreEqual(string.Empty, target.TitleValue);
            Assert.AreEqual(string.Empty, target.DescriptionValue);
            Object.Destroy(target);
        }
    }
}
