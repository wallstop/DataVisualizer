#pragma warning disable CS0618 // Type or member is obsolete
namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using System;
    using System.IO;
    using Data;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.DataVisualizer.Editor.Services;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class JsonUserStateRepositoryTests
    {
        [Test]
        public void SaveUserStateSchedulesSingleWriteUntilFlush()
        {
            string tempPath = Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString("N") + ".json"
            );
            ScriptableAssetSaveScheduler scheduler = new ScriptableAssetSaveScheduler(0.5d);
            try
            {
                JsonUserStateRepository repository = new JsonUserStateRepository(
                    tempPath,
                    scheduler
                );

                DataVisualizerUserState state = new DataVisualizerUserState();
                repository.SaveUserState(state);
                repository.SaveUserState(state);

                Assert.That(scheduler.PendingActionCount, Is.EqualTo(1));
                Assert.That(File.Exists(tempPath), Is.False);

                scheduler.Flush();

                Assert.That(scheduler.PendingActionCount, Is.EqualTo(0));
                Assert.That(File.Exists(tempPath), Is.True);
            }
            finally
            {
                scheduler.Dispose();
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        [Test]
        public void SaveSettingsSchedulesAssetDatabaseSave()
        {
            ScriptableAssetSaveScheduler scheduler = new ScriptableAssetSaveScheduler(0.5d);
            try
            {
                SettingsAssetStateRepository repository = new SettingsAssetStateRepository(
                    scheduler
                );
                DataVisualizerSettings settings =
                    ScriptableObject.CreateInstance<DataVisualizerSettings>();
                try
                {
                    repository.SaveSettings(settings);

                    Assert.That(scheduler.IsAssetDatabaseSavePending, Is.True);

                    scheduler.Flush();

                    Assert.That(scheduler.IsAssetDatabaseSavePending, Is.False);
                }
                finally
                {
                    Object.DestroyImmediate(settings);
                }
            }
            finally
            {
                scheduler.Dispose();
            }
        }
    }
}
#pragma warning restore CS0618 // Type or member is obsolete
