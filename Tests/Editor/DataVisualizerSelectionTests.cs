namespace WallstopStudios.DataVisualizer.Editor.Tests
{
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using Styles;
    using UnityEngine;
    using UnityEngine.UIElements;

    [TestFixture]
    public sealed class DataVisualizerSelectionTests
    {
        private sealed class DummyScriptableObject : ScriptableObject { }

        [Test]
        public void BindObjectRowSelectedObjectAssignsSelectedElement()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;
                List<ScriptableObject> displayedObjects = dataVisualizer._displayedObjects;

                DummyScriptableObject selectedObject =
                    ScriptableObject.CreateInstance<DummyScriptableObject>();
                try
                {
                    displayedObjects.Add(selectedObject);

                    dataVisualizer._selectedObject = selectedObject;
                    VisualElement row = dataVisualizer.MakeObjectRow();
                    dataVisualizer.BindObjectRow(row, 0);
                    VisualElement selectedElement = dataVisualizer._selectedElement;

                    Assert.AreSame(row, selectedElement);
                    Assert.IsTrue(row.ClassListContains(StyleConstants.SelectedClass));

                    Label titleLabel = row.Q<Label>(name: "object-item-label");
                    Assert.NotNull(titleLabel);
                    Assert.IsFalse(titleLabel.ClassListContains(StyleConstants.ClickableClass));
                }
                finally
                {
                    Object.DestroyImmediate(selectedObject);
                    displayedObjects.Clear();
                }
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }

        [Test]
        public void BindObjectRowReuseClearsPreviousSelectedElement()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;
                List<ScriptableObject> displayedObjects = dataVisualizer._displayedObjects;

                DummyScriptableObject selectedObject =
                    ScriptableObject.CreateInstance<DummyScriptableObject>();
                DummyScriptableObject replacementObject =
                    ScriptableObject.CreateInstance<DummyScriptableObject>();
                try
                {
                    displayedObjects.Add(selectedObject);
                    dataVisualizer._selectedObject = selectedObject;

                    VisualElement row = dataVisualizer.MakeObjectRow();
                    dataVisualizer.BindObjectRow(row, 0);

                    VisualElement selectedElement = dataVisualizer._selectedElement;
                    Assert.AreSame(row, selectedElement);

                    displayedObjects.Clear();
                    displayedObjects.Add(replacementObject);

                    dataVisualizer.BindObjectRow(row, 0);

                    selectedElement = dataVisualizer._selectedElement;
                    Assert.IsNull(selectedElement);
                    Assert.IsFalse(row.ClassListContains(StyleConstants.SelectedClass));

                    Label titleLabel = row.Q<Label>(name: "object-item-label");
                    Assert.NotNull(titleLabel);
                    Assert.IsTrue(titleLabel.ClassListContains(StyleConstants.ClickableClass));
                }
                finally
                {
                    Object.DestroyImmediate(selectedObject);
                    Object.DestroyImmediate(replacementObject);
                    displayedObjects.Clear();
                }
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }

        [Test]
        public void UnbindObjectRowClearsSelectedElement()
        {
            DataVisualizer dataVisualizer = ScriptableObject.CreateInstance<DataVisualizer>();
            try
            {
                dataVisualizer.hideFlags = HideFlags.HideAndDontSave;
                List<ScriptableObject> displayedObjects = dataVisualizer._displayedObjects;

                DummyScriptableObject selectedObject =
                    ScriptableObject.CreateInstance<DummyScriptableObject>();
                try
                {
                    displayedObjects.Add(selectedObject);
                    dataVisualizer._selectedObject = selectedObject;

                    VisualElement row = dataVisualizer.MakeObjectRow();
                    dataVisualizer.BindObjectRow(row, 0);
                    dataVisualizer.UnbindObjectRow(row, 0);
                    VisualElement selectedElement = dataVisualizer._selectedElement;

                    Assert.IsNull(selectedElement);
                    Assert.IsFalse(row.ClassListContains(StyleConstants.SelectedClass));

                    Label titleLabel = row.Q<Label>(name: "object-item-label");
                    Assert.NotNull(titleLabel);
                    Assert.IsTrue(titleLabel.ClassListContains(StyleConstants.ClickableClass));
                }
                finally
                {
                    Object.DestroyImmediate(selectedObject);
                    displayedObjects.Clear();
                }
            }
            finally
            {
                Object.DestroyImmediate(dataVisualizer);
            }
        }
    }
}
