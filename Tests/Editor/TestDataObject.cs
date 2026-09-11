namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using WallstopStudios.DataVisualizer;

    internal sealed class TestDataObject : BaseDataObject
    {
        public void InvokeValidation()
        {
            OnValidate();
        }
    }
}
