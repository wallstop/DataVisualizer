namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using UnityEditor;
    using UnityEngine;

    public sealed class ReadOnlyPrefabContentsScope : IDisposable
    {
        public GameObject Root { get; private set; }

        public ReadOnlyPrefabContentsScope(string path)
        {
            Root = PrefabUtility.LoadPrefabContents(path);
        }

        public void Dispose()
        {
            GameObject root = Root;
            Root = null;
            if (root == null)
            {
                return;
            }
            try
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }
    }
}
