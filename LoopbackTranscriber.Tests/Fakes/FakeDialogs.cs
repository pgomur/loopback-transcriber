namespace LoopbackTranscriber.Tests.Fakes;

internal sealed class FakeDialogs(bool confirm = true) : IDialogService
{
    public List<string> Errors { get; } = [];

    public List<string> Infos { get; } = [];

    public int Confirmations { get; private set; }

    public bool ConfirmAnswer { get; set; } = confirm;

    public DialogAnswer? YesNoCancelAnswer { get; set; }

    public bool Confirm(string message, string title)
    {
        Confirmations++;
        return ConfirmAnswer;
    }

    public DialogAnswer AskYesNoCancel(string message, string title)
    {
        Confirmations++;
        return YesNoCancelAnswer ?? (ConfirmAnswer ? DialogAnswer.Yes : DialogAnswer.Cancel);
    }

    public void ShowError(string message, string title) => Errors.Add(message);

    public void ShowInfo(string message, string title) => Infos.Add(message);
}
