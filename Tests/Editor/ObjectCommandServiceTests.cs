namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using System;
    using Events;
    using NUnit.Framework;
    using Services;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class ObjectCommandServiceTests
    {
        private sealed class DummyScriptableObject : ScriptableObject { }

        [Test]
        public void MoveToTopPublishesEvent()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;
                DataVisualizerEventHub hub = new DataVisualizerEventHub();
                ObjectCommandService service = new ObjectCommandService(dataVisualizer, hub);

                DummyScriptableObject obj =
                    ScriptableObject.CreateInstance<DummyScriptableObject>();
                Button button = new Button { userData = obj };

                ObjectMoveToTopRequestedEvent captured = null;
                using System.IDisposable subscription =
                    hub.Subscribe<ObjectMoveToTopRequestedEvent>(evt => captured = evt);

                service.MoveToTop(button);

                Assert.NotNull(captured);
                Assert.AreSame(obj, captured.DataObject);
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }

        [Test]
        public void RenamePublishesEvent()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;
                DataVisualizerEventHub hub = new DataVisualizerEventHub();
                ObjectCommandService service = new ObjectCommandService(dataVisualizer, hub);

                DummyScriptableObject obj =
                    ScriptableObject.CreateInstance<DummyScriptableObject>();
                Button button = new Button { userData = obj };

                ObjectRenameRequestedEvent captured = null;
                using IDisposable subscription = hub.Subscribe<ObjectRenameRequestedEvent>(evt =>
                    captured = evt
                );

                service.Rename(button);

                Assert.NotNull(captured);
                Assert.AreSame(obj, captured.DataObject);
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }
    }
}
