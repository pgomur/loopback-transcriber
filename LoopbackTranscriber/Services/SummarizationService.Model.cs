using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LLama;
using LLama.Common;
using LLama.Sampling;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public sealed partial class SummarizationService
{
    private bool IsLoaded(string modelPath)
    {
        lock (_loadLock)
        {
            return _executor is not null &&
                   string.Equals(_loadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void UnloadIfIdle()
    {
        lock (_loadLock)
        {
            if (_weights is null)
            {
                return;
            }

            Debug.WriteLine("[LoopbackTranscriber] SummarizationService: summary model unloaded from RAM after inactivity");

            DisposeExecutorContext(_executor);
            _weights.Dispose();
            _weights = null;
            _executor = null;
            _loadedModelPath = null;
        }

        MemoryTrimmer.Release();
    }

    private static void DisposeExecutorContext(StatelessExecutor? executor)
    {
        if (executor is null)
        {
            return;
        }

        try
        {
            (executor.GetType().GetProperty("Context")?.GetValue(executor) as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Warn("SummarizationService.DisposeExecutorContext: could not release the model context", ex);
        }
    }

    private StatelessExecutor EnsureLoaded(string modelPath)
    {
        lock (_loadLock)
        {
            if (_executor is not null && string.Equals(_loadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                return _executor;
            }

            DisposeExecutorContext(_executor);
            _weights?.Dispose();

            var threadCount = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);

            const int offloadAllLayersToGpu = 999;

            var parameters = new ModelParams(modelPath)
            {
                ContextSize = ContextSize,
                GpuLayerCount = offloadAllLayersToGpu,
                Threads = threadCount
            };

            _weights = LLamaWeights.LoadFromFile(parameters);
            _executor = new StatelessExecutor(_weights, parameters);
            _loadedModelPath = modelPath;

            return _executor;
        }
    }
}
