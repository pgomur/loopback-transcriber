using System.IO;
using System.Security.Cryptography;

namespace LoopbackTranscriber.Services;

internal static class ModelIntegrity
{
    public static async Task VerifySha256Async(string path, string expectedHex, string modelName,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(Strings.Instance.Get("ProgressVerifyingModel"));

        string actual;
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                         1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            actual = Convert.ToHexString(hash);
        }

        if (!string.Equals(actual, expectedHex, StringComparison.OrdinalIgnoreCase))
        {
            AppLog.Error($"ModelIntegrity: SHA256 incorrecto para {modelName}. Esperado {expectedHex}, obtenido {actual}.");
            throw new InvalidDataException(Strings.Instance.Format("ErrorModelHashMismatch", modelName));
        }

        AppLog.Info($"ModelIntegrity: SHA256 de {modelName} verificado.");
    }
}
