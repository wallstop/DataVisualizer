namespace WallstopStudios.DataVisualizer.Editor.Services
{
    using System;
    using System.Collections.Generic;
    using WallstopStudios.DataVisualizer;

    internal interface IDataProcessorRegistry
    {
        void Refresh();

        IReadOnlyList<IDataProcessor> GetAllProcessors();

        IReadOnlyList<IDataProcessor> GetCompatibleProcessors(Type type);
    }
}
