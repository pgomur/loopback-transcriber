using LoopbackTranscriber.Services;

namespace LoopbackTranscriber;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var commandLineArgs = Environment.GetCommandLineArgs();
        if (WorkerHost.IsWorkerInvocation(commandLineArgs))
        {
            Environment.Exit(WorkerHost.Run(commandLineArgs));
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
