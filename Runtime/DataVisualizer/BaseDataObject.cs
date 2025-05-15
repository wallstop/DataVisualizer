using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(assemblyName: "WallstopStudios.DataVisualizer.Editor")]

namespace WallstopStudios.DataVisualizer
{
    using System;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Serialization;
    using UnityEngine.UIElements;
#if ODIN_INSPECTOR
    using Sirenix.OdinInspector;
#endif

    public abstract class BaseDataObject
        :
#if ODIN_INSPECTOR
        SerializedScriptableObject
#else
        ScriptableObject
#endif
            ,
            IComparable<BaseDataObject>,
            IDuplicable,
            ICreatable,
            IRenamable,
            IGUIProvider,
            IDisplayable
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
            TrySetAssetPath();
        }

        private void TrySetAssetPath()
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

        public int CompareTo(BaseDataObject other)
        {
            if (ReferenceEquals(this, other))
            {
                return 0;
            }

            if (other == null)
            {
                return 1;
            }

            int titleComparison = string.Compare(Title, other.Title, StringComparison.Ordinal);
            if (titleComparison != 0)
            {
                return titleComparison;
            }

            int nameComparison = string.Compare(name, other.name, StringComparison.Ordinal);
            if (nameComparison != 0)
            {
                return nameComparison;
            }

            int idComparison = string.Compare(Id, other.Id, StringComparison.Ordinal);
            if (idComparison != 0)
            {
                return idComparison;
            }

            return string.Compare(Description, other.Description, StringComparison.Ordinal);
        }

        public virtual void BeforeClone(ScriptableObject previous)
        {
            _assetGuid = string.Empty;
        }

        public virtual void AfterClone(ScriptableObject previous)
        {
            TrySetAssetPath();
            if (previous is BaseDataObject baseDataObject)
            {
                _title = baseDataObject.Title + " (Clone)";
            }
        }

        public virtual void BeforeCreate() { }

        public virtual void AfterCreate()
        {
            TrySetAssetPath();
        }

        public virtual void BeforeRename(string newName) { }

        public virtual void AfterRename(string newName) { }
    }
}
