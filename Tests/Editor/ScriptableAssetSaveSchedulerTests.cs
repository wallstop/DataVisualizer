#pragma warning disable CS0618 // Type or member is obsolete
namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Services;

    [TestFixture]
    public sealed class ScriptableAssetSaveSchedulerTests
    {
        [Test]
        public void ScheduleActionFlushesAndUpdatesDiagnostics()
        {
            ScriptableAssetSaveScheduler scheduler = new ScriptableAssetSaveScheduler(0.05d);
            try
            {
                bool invoked = false;
                scheduler.Schedule(() => invoked = true);

                Assert.That(scheduler.PendingActionCount, Is.EqualTo(1));
                Assert.That(scheduler.LastFlushTime.HasValue, Is.False);

                scheduler.Flush();

                Assert.That(invoked, Is.True);
                Assert.That(scheduler.PendingActionCount, Is.EqualTo(0));
                Assert.That(scheduler.LastFlushTime.HasValue, Is.True);
            }
            finally
            {
                scheduler.Dispose();
            }
        }

        [Test]
        public void ScheduleAssetSaveFlushClearsPendingState()
        {
            ScriptableAssetSaveScheduler scheduler = new ScriptableAssetSaveScheduler(0.05d);
            try
            {
                scheduler.ScheduleAssetDatabaseSave();

                Assert.That(scheduler.IsAssetDatabaseSavePending, Is.True);
                Assert.That(scheduler.NextScheduledFlushTime.HasValue, Is.True);

                scheduler.Flush();

                Assert.That(scheduler.IsAssetDatabaseSavePending, Is.False);
                Assert.That(scheduler.LastFlushTime.HasValue, Is.True);
                Assert.That(scheduler.NextScheduledFlushTime.HasValue, Is.False);
            }
            finally
            {
                scheduler.Dispose();
            }
        }
    }
}
#pragma warning restore CS0618 // Type or member is obsolete
