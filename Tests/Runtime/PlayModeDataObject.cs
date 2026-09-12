namespace WallstopStudios.DataVisualizer.Tests.Runtime
{
    using WallstopStudios.DataVisualizer;

    internal sealed class PlayModeDataObject : BaseDataObject
    {
        internal string AssetGuidValue
        {
            get { return _assetGuid; }
            set { _assetGuid = value; }
        }

        internal string TitleValue
        {
            get { return _title; }
            set { _title = value; }
        }

        internal string DescriptionValue
        {
            get { return _description; }
            set { _description = value; }
        }

        internal int BeforeCreateInvocations;

        internal int AfterCreateInvocations;

        internal int BeforeRenameInvocations;

        internal int AfterRenameInvocations;

        internal string LastBeforeRenameName;

        public override void BeforeCreate()
        {
            BeforeCreateInvocations++;
        }

        public override void AfterCreate()
        {
            AfterCreateInvocations++;
            base.AfterCreate();
        }

        public override void BeforeRename(string newName)
        {
            BeforeRenameInvocations++;
            LastBeforeRenameName = newName;
        }

        public override void AfterRename(string newName)
        {
            AfterRenameInvocations++;
            base.AfterRename(newName);
        }
    }
}
