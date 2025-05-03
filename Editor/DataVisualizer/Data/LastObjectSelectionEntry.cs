namespace WallstopStudios.DataVisualizer.Editor.Editor.DataVisualizer.Data
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
