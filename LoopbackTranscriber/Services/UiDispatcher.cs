using System.Windows;

namespace LoopbackTranscriber.Services;

public interface IUiDispatcher
{
    void Run(Action action);
}

public sealed class WpfUiDispatcher : IUiDispatcher
{
    public static WpfUiDispatcher Instance { get; } = new();

    public void Run(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}
