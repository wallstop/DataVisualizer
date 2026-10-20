namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DataVisualizer.Editor.UI;

    public sealed class ObjectListSelectionInputTests
    {
        private static void SendDown(VisualElement target, EventModifiers modifiers)
        {
            Event mouse = new()
            {
                type = EventType.MouseDown,
                button = 0,
                modifiers = modifiers,
            };
            using PointerDownEvent down = PointerDownEvent.GetPooled(mouse);
            down.target = target;
            target.SendEvent(down);
        }

        private static void SendUp(VisualElement target)
        {
            Event mouse = new()
            {
                type = EventType.MouseUp,
                button = 0,
                mousePosition = target.worldBound.center,
            };
            using PointerUpEvent up = PointerUpEvent.GetPooled(mouse);
            up.target = target;
            target.SendEvent(up);
        }

        [UnityTest]
        public IEnumerator ShouldOnlyAcceptExplicitDeselectGesturesAndExpireInputState()
        {
            using TestCleanupScope cleanup = new();
            LayoutTestWindow host = ScriptableObject.CreateInstance<LayoutTestWindow>();
            cleanup.Defer(host.Close);
            TestDataObject asset = ScriptableObject.CreateInstance<TestDataObject>();
            cleanup.Defer(() => Object.DestroyImmediate(asset));
            ListView list = new()
            {
                itemsSource = new[] { asset },
                fixedItemHeight = 40,
                makeItem = () =>
                {
                    VisualElement item = new() { name = "selection-input-row", style = { flexDirection = FlexDirection.Row } };
                    item.Add(new Label("Fixture") { name = "selection-input-label", style = { width = 100 } });
                    item.Add(new Button { name = "selection-input-action", text = "Action" });
                    return item;
                },
                bindItem = (element, _) => element.userData = asset,
                style = { height = 200 },
            };
            host.rootVisualElement.Add(list);
            host.ShowUtility();
            EditorSurfaceCapture.SettleLayout(host);
            VisualElement row = list.Q<Label>("selection-input-label");
            Button action = list.Q<Button>("selection-input-action");
            using ObjectListSelectionInput input = new(list, host.rootVisualElement);
            action.RegisterCallback<PointerDownEvent>(_ => input.BlockEmptySelection(), TrickleDown.TrickleDown);

            SendDown(row, EventModifiers.None);
            Assert.That(input.AllowsEmptySelection, Is.False, "plain row actions and drag refreshes must not clear the batch");
            SendUp(row);
            SendDown(row, EventModifiers.Control);
            Assert.That(input.AllowsEmptySelection, Is.True, "Ctrl-click can deselect the final row");
            SendUp(row);
            SendDown(action, EventModifiers.Control);
            Assert.That(input.AllowsEmptySelection, Is.False, "row controls cannot authorize an empty selection");
            SendUp(action);
            SendDown(list, EventModifiers.None);
            Assert.That(input.AllowsEmptySelection, Is.True, "blank space can deselect visible rows");
            using (PointerUpEvent up = PointerUpEvent.GetPooled())
            {
                up.target = host.rootVisualElement;
                host.rootVisualElement.SendEvent(up);
            }
            Assert.That(input.AllowsEmptySelection, Is.True, "late pointer-up notifications still belong to this gesture");
            SendDown(row, EventModifiers.Command);
            Assert.That(input.AllowsEmptySelection, Is.True);
            for (int frame = 0; frame < 10 && input.AllowsEmptySelection; frame++)
            {
                yield return null;
            }
            Assert.That(input.AllowsEmptySelection, Is.False, "authorization expires even if mouse capture bypasses release callbacks");
            SendDown(row, EventModifiers.Control);
            using (PointerCancelEvent cancel = PointerCancelEvent.GetPooled())
            {
                cancel.target = host.rootVisualElement;
                host.rootVisualElement.SendEvent(cancel);
            }
            for (int frame = 0; frame < 10 && input.AllowsEmptySelection; frame++)
            {
                yield return null;
            }
            Assert.That(input.AllowsEmptySelection, Is.False);
            input.Dispose();
            input.Dispose();
            SendDown(row, EventModifiers.Control);
            Assert.That(input.AllowsEmptySelection, Is.False, "disposed owners must remove input callbacks");
        }
    }
}
