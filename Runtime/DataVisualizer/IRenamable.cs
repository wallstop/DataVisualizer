namespace WallstopStudios.DataVisualizer
{
    public interface IRenamable
    {
        public void BeforeRename(string newName);
        public void AfterRename(string newName);
    }
}
