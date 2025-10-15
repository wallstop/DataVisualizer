namespace WallstopStudios.DataVisualizer.Editor.Services.ObjectCommands
{
    using System;
    using Events;

    internal interface IObjectCommand
    {
        IDisposable Subscribe(DataVisualizerEventHub eventHub);
    }
}
