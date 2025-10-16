namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using System;
    using Controllers;
    using Events;
    using NUnit.Framework;
    using Scaffolding;
    using UnityEngine;
    using UnityEngine.UIElements;

    [TestFixture]
    public sealed class DataVisualizerWindowScaffoldingTests
    {
        private sealed class DummyScriptableObject : ScriptableObject { }

        [Test]
        public void CreateGUI_BuildsOuterSplitView()
        {
            using (DataVisualizerTestHarness harness = new DataVisualizerTestHarness())
            {
                harness.InitializeWindow();
                VisualElement root = harness.Window.rootVisualElement;
                Assert.IsNotNull(root, "Root visual element should not be null after CreateGUI.");
                VisualElement outerSplitView = root.Q<VisualElement>("outer-split-view");
                Assert.IsNotNull(outerSplitView, "Outer split view should be created.");
            }
        }

        [Test]
        public void TypeSelectionPublishesEvent()
        {
            using (DataVisualizerTestHarness harness = new DataVisualizerTestHarness())
            {
                harness.ConfigureNamespace("TestNamespace", typeof(DummyScriptableObject));
                harness.InitializeWindow();

                DataVisualizerEventHub eventHub = harness.Window._eventHub;
                Assert.IsNotNull(eventHub, "Event hub should be initialized.");

                bool eventRaised = false;
                IDisposable subscription = eventHub.Subscribe<TypeSelectedEvent>(_ =>
                    eventRaised = true
                );
                try
                {
                    NamespacePanelController controller = harness.Window._namespacePanelController;
                    Assert.IsNotNull(
                        controller,
                        "Namespace panel controller should be initialized."
                    );

                    controller.SelectType(typeof(DummyScriptableObject));
                    Assert.IsTrue(
                        eventRaised,
                        "Selecting a type should publish a TypeSelectedEvent."
                    );
                }
                finally
                {
                    subscription.Dispose();
                }
            }
        }
    }
}
