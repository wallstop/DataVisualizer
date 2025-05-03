namespace WallstopStudios.DataVisualizer.Editor.Data
{
    using System;

    [Serializable]
    public sealed class LastObjectSelectionEntry
    {
        public string typeFullName = string.Empty;
        public string objectGuid = string.Empty;

        public LastObjectSelectionEntry Clone()
        {
            return new LastObjectSelectionEntry
            {
                typeFullName = typeFullName,
                objectGuid = objectGuid,
            };
        }
    }
}
