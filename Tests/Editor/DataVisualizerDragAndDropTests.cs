namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using Data;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class DataVisualizerDragAndDropTests
    {
        private const string TestFolderPath = "Assets/TempDataVisualizerDragTests";

        private sealed class DummyScriptableObject : ScriptableObject { }

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TestFolderPath))
            {
                AssetDatabase.CreateFolder("Assets", "TempDataVisualizerDragTests");
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestFolderPath))
            {
                AssetDatabase.DeleteAsset(TestFolderPath);
            }
        }

        [Test]
        public void NormalizeGhostInsertIndexClampsWhenGhostBeyondEnd()
        {
            VisualElement container = new();
            container.Add(new VisualElement());
            container.Add(new VisualElement());
            container.Add(new VisualElement());

            VisualElement ghost = new();
            container.Add(ghost);

            int result = DataVisualizer.NormalizeGhostInsertIndex(container, ghost, 10);
            Assert.AreEqual(3, result);
        }

        [Test]
        public void NormalizeGhostInsertIndexClampsWhenGhostNotInContainer()
        {
            VisualElement container = new();
            container.Add(new VisualElement());
            container.Add(new VisualElement());

            VisualElement ghost = new();

            int result = DataVisualizer.NormalizeGhostInsertIndex(container, ghost, 5);
            Assert.AreEqual(2, result);
        }

        [Test]
        public void PerformObjectDropMovesFirstItemToEnd()
        {
            DummyScriptableObject itemOne =
                ScriptableObject.CreateInstance<DummyScriptableObject>();
            DummyScriptableObject itemTwo =
                ScriptableObject.CreateInstance<DummyScriptableObject>();
            DummyScriptableObject itemThree =
                ScriptableObject.CreateInstance<DummyScriptableObject>();

            itemOne.name = "One";
            itemTwo.name = "Two";
            itemThree.name = "Three";

            string itemOnePath = Path.Combine(TestFolderPath, "ItemOne.asset");
            string itemTwoPath = Path.Combine(TestFolderPath, "ItemTwo.asset");
            string itemThreePath = Path.Combine(TestFolderPath, "ItemThree.asset");

            AssetDatabase.CreateAsset(itemOne, itemOnePath);
            AssetDatabase.CreateAsset(itemTwo, itemTwoPath);
            AssetDatabase.CreateAsset(itemThree, itemThreePath);
            AssetDatabase.SaveAssets();

            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;

                dataVisualizer._suppressObjectListReloadForTests = true;

                List<ScriptableObject> displayed = dataVisualizer._displayedObjects;
                displayed.Clear();
                displayed.Add(itemOne);
                displayed.Add(itemTwo);
                displayed.Add(itemThree);

                List<ScriptableObject> filtered = dataVisualizer._filteredObjects;
                filtered.Clear();
                filtered.AddRange(displayed);

                List<ScriptableObject> selected = dataVisualizer._selectedObjects;
                selected.Clear();
                selected.AddRange(displayed);

                dataVisualizer._selectedObject = itemThree;
                dataVisualizer._draggedData = itemOne;
                dataVisualizer._draggedElement = new VisualElement();

                VisualElement ghost = new() { userData = 3 };
                dataVisualizer._inPlaceGhost = ghost;

                dataVisualizer._activeDragType = DataVisualizer.DragType.Object;
                dataVisualizer._currentDisplayStartIndex = 0;

                ListView listView = new()
                {
                    itemsSource = displayed,
                    makeItem = () => new VisualElement(),
                    bindItem = (_, _) => { },
                };
                dataVisualizer._objectListView = listView;

                DataVisualizerSettings settings =
                    ScriptableObject.CreateInstance<DataVisualizerSettings>();
                settings.persistStateInSettingsAsset = true;
#pragma warning disable CS0618 // Type or member is obsolete
                dataVisualizer._settings = settings;
#pragma warning restore CS0618 // Type or member is obsolete

#pragma warning disable CS0618 // Type or member is obsolete
                dataVisualizer._userState = new DataVisualizerUserState();
#pragma warning restore CS0618 // Type or member is obsolete

                dataVisualizer._userStateFilePath = Path.Combine(
                    Application.temporaryCachePath,
                    "DataVisualizerUserState_Test.json"
                );

                Dictionary<string, List<Type>> scriptableTypes = new Dictionary<string, List<Type>>
                {
                    {
                        "DummyNamespace",
                        new List<Type> { typeof(DummyScriptableObject) }
                    },
                };
                Dictionary<string, int> namespaceOrder = new Dictionary<string, int>
                {
                    { "DummyNamespace", 0 },
                };

                dataVisualizer._scriptableObjectTypes = scriptableTypes;
                dataVisualizer._namespaceOrder = namespaceOrder;

                NamespaceController controller = new(scriptableTypes, namespaceOrder)
                {
                    _selectedType = typeof(DummyScriptableObject),
                };
                dataVisualizer._namespaceController = controller;

                dataVisualizer.PerformObjectDrop();

                CollectionAssert.AreEqual(new[] { itemTwo, itemThree, itemOne }, filtered);
                CollectionAssert.AreEqual(new[] { itemTwo, itemThree, itemOne }, displayed);
                CollectionAssert.AreEqual(new[] { itemTwo, itemThree, itemOne }, selected);
                Assert.AreEqual(1, listView.selectedIndex);

                MethodInfo makeRowMethod = typeof(DataVisualizer).GetMethod(
                    "MakeObjectRow",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                MethodInfo bindRowMethod = typeof(DataVisualizer).GetMethod(
                    "BindObjectRow",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );

                List<string> titles = new List<string>();
                for (int index = 0; index < displayed.Count; index++)
                {
                    VisualElement row = (VisualElement)makeRowMethod.Invoke(dataVisualizer, null);
                    bindRowMethod.Invoke(dataVisualizer, new object[] { row, index });
                    Label title = row.Q<Label>(name: "object-item-label");
                    titles.Add(title?.text ?? string.Empty);
                }

                Assert.AreEqual(new[] { itemTwo.name, itemThree.name, itemOne.name }, titles);
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
                AssetDatabase.DeleteAsset(itemOnePath);
                AssetDatabase.DeleteAsset(itemTwoPath);
                AssetDatabase.DeleteAsset(itemThreePath);
                AssetDatabase.SaveAssets();
                Object.DestroyImmediate(itemOne);
                Object.DestroyImmediate(itemTwo);
                Object.DestroyImmediate(itemThree);
            }
        }
    }
}
