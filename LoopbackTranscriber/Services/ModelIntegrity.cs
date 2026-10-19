using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace LoopbackTranscriber.Services;

internal static class ModelIntegrity
{
    private const string MarkerSuffix = ".verified.json";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    private sealed class VerificationMarker
    {
        public string Sha256 { get; set; } = string.Empty;
        public long Length { get; set; }
        public long LastWriteTimeUtcTicks { get; set; }
    }

    public static async Task VerifySha256Async(string path, string expectedHex, string modelName,
        IProgress<UserMessage>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(UserMessage.FromKey("ProgressVerifyingModel"));

        string actual;
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                         1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            actual = Convert.ToHexString(hash);
        }

        if (!string.Equals(actual, expectedHex, StringComparison.OrdinalIgnoreCase))
        {
            AppLog.Error($"ModelIntegrity: SHA256 mismatch for {modelName}. Expected {expectedHex}, got {actual}.");
            throw UserError.InvalidData("ErrorModelHashMismatch", modelName);
        }

        AppLog.Info($"ModelIntegrity: SHA256 of {modelName} verified.");
    }

    public static async Task<bool> EnsureVerifiedAsync(string path, string expectedHex, string modelName,
        IProgress<UserMessage>? progress, CancellationToken cancellationToken)
    {
        var gate = Gates.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            if (HasValidMarker(path, expectedHex))
            {
                return true;
            }

            AppLog.Info($"ModelIntegrity: {modelName} has no valid verification marker; checking its SHA256");

            try
            {
                await VerifySha256Async(path, expectedHex, modelName, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                DeleteCorruptModel(path, modelName);
                return false;
            }

            WriteMarker(path, expectedHex);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public static void WriteMarker(string path, string expectedHex)
    {
        try
        {
            var info = new FileInfo(path);
            var marker = new VerificationMarker
            {
                Sha256 = expectedHex,
                Length = info.Length,
                LastWriteTimeUtcTicks = info.LastWriteTimeUtc.Ticks
            };

            File.WriteAllText(path + MarkerSuffix, JsonSerializer.Serialize(marker));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"ModelIntegrity: could not save the verification marker for \"{path}\"; it will be verified again on next use", ex);
        }
    }

    private static bool HasValidMarker(string path, string expectedHex)
    {
        var markerPath = path + MarkerSuffix;

        try
        {
            if (!File.Exists(markerPath))
            {
                return false;
            }

            var marker = JsonSerializer.Deserialize<VerificationMarker>(File.ReadAllText(markerPath));
            var info = new FileInfo(path);

            return marker is not null
                   && string.Equals(marker.Sha256, expectedHex, StringComparison.OrdinalIgnoreCase)
                   && marker.Length == info.Length
                   && marker.LastWriteTimeUtcTicks == info.LastWriteTimeUtc.Ticks;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"ModelIntegrity: unreadable verification marker \"{markerPath}\"", ex);
            return false;
        }
    }

    private static void DeleteCorruptModel(string path, string modelName)
    {
        try
        {
            File.Delete(path);
            FileCleanup.TryDelete(path + MarkerSuffix, "ModelIntegrity");
            AppLog.Error($"ModelIntegrity: {modelName} was corrupt and has been deleted so it can be downloaded again (\"{path}\")");
        }
        catch (Exception ex)
        {
            AppLog.Warn($"ModelIntegrity: could not delete the corrupt model \"{path}\"", ex);
            throw UserError.InvalidData(ex, "ErrorModelHashMismatch", modelName);
        }
    }
}
