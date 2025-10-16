namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using Controllers;
    using Events;
    using NUnit.Framework;
    using Scaffolding;
    using State;
    using UnityEngine;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class ObjectCommandDispatcherIntegrationTests
    {
        private sealed class DummyScriptableObject : ScriptableObject { }

        [Test]
        public void MoveToTopEventReordersSelection()
        {
            using DataVisualizerTestHarness harness = new DataVisualizerTestHarness();
            harness.ConfigureNamespace("TestNamespace", typeof(DummyScriptableObject));
            harness.InitializeWindow();

            NamespacePanelController controller = harness.Window._namespacePanelController;
            Assert.IsNotNull(controller, "Namespace panel controller should be initialized.");
            controller.SelectType(typeof(DummyScriptableObject));

            DummyScriptableObject first = ScriptableObject.CreateInstance<DummyScriptableObject>();
            DummyScriptableObject second = ScriptableObject.CreateInstance<DummyScriptableObject>();
            try
            {
                harness.Window._selectedObjects.Clear();
                ObjectListState listState = harness.Window.ObjectListState;
                listState.ClearFiltered();
                harness.Window._selectedObjects.Add(first);
                harness.Window._selectedObjects.Add(second);
                listState.FilteredObjectsBuffer.Add(first);
                listState.FilteredObjectsBuffer.Add(second);

                // Simplify BuildObjectsView path for the test to avoid UI dependencies.
                harness.Window._objectListView = null;

                harness.Window._eventHub.Publish(new ObjectMoveToTopRequestedEvent(second));

                Assert.AreSame(
                    second,
                    harness.Window._selectedObjects[0],
                    "Dispatcher should move requested object to the top of the selection list."
                );
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }
    }
}
