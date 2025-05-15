namespace WallstopStudios.DataVisualizer
{
    using UnityEngine;

    public interface IDuplicable
    {
        public void BeforeClone(ScriptableObject previous);
        public void AfterClone(ScriptableObject previous);
    }
}
