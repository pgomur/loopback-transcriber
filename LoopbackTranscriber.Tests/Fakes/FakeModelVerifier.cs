namespace LoopbackTranscriber.Tests.Fakes;

internal sealed class FakeModelVerifier : IModelVerifier
{
    public ModelVerificationResult Result { get; set; } = new(TranscriptionModelRemoved: false, SummaryModelRemoved: false);

    public Exception? Failure { get; set; }

    public int Calls { get; private set; }

    public Task<ModelVerificationResult> VerifyInstalledModelsAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Failure is null ? Task.FromResult(Result) : Task.FromException<ModelVerificationResult>(Failure);
    }
}
