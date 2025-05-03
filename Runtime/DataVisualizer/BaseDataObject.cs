using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(assemblyName: "WallstopStudios.DataVisualizer.Editor")]

namespace WallstopStudios.DataVisualizer
{
    using Sirenix.OdinInspector;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Serialization;
    using UnityEngine.UIElements;

    public abstract class BaseDataObject :
#if ODIN_INSPECTOR
        SerializedScriptableObject
#else
        ScriptableObject
#endif
    {
        public virtual string Id => _assetGuid;

        public virtual string Title
        {
            get
            {
                string title = _title;
                return string.IsNullOrWhiteSpace(title) ? name : title;
            }
        }

        public virtual string Description => _description;

        [Header("Base Data")]
        [FormerlySerializedAs("initialGuid")]
#if ODIN_INSPECTOR
        [ReadOnly]
#endif
        [SerializeField]
        protected internal string _assetGuid;

        [FormerlySerializedAs("title")]
        [SerializeField]
        protected internal string _title = string.Empty;

        [FormerlySerializedAs("description")]
        [SerializeField]
        [TextArea]
        protected string _description = string.Empty;

        protected internal virtual void OnValidate()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_assetGuid))
            {
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(this);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            _assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
            EditorUtility.SetDirty(this);
#endif
        }

        public virtual VisualElement BuildGUI(DataVisualizerGUIContext context)
        {
            return null;
        }
    }
}
