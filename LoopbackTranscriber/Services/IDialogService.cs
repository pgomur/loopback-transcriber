using System.Windows;

namespace LoopbackTranscriber.Services;

public interface IDialogService
{
    bool Confirm(string message, string title);

    void ShowError(string message, string title);

    void ShowInfo(string message, string title);
}

public sealed class WpfDialogService : IDialogService
{
    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public void ShowError(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
