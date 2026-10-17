using System.Text;
using System.Text.RegularExpressions;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace LoopbackTranscriber.Services;

internal sealed class LiveTranslatorConfig
{
    public string ModelPath { get; set; } = string.Empty;
    public int ThreadCount { get; set; } = 4;
}

internal sealed class LiveTranslateRequest
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;

    public string? Context { get; set; }

    public string Target { get; set; } = "English";
}

internal sealed class LiveTranslatorEngine : IDisposable
{
    private const int ContextSize = 2048;
    private const int OffloadAllLayersToGpu = 999;

    private static readonly Regex ThinkBlockRegex = new(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly LLamaWeights _weights;
    private readonly StatelessExecutor _executor;

    public LiveTranslatorEngine(LiveTranslatorConfig config)
    {
        var parameters = new ModelParams(config.ModelPath)
        {
            ContextSize = ContextSize,
            GpuLayerCount = OffloadAllLayersToGpu,
            Threads = Math.Clamp(config.ThreadCount, 2, 8)
        };

        _weights = LLamaWeights.LoadFromFile(parameters);
        _executor = new StatelessExecutor(_weights, parameters);
    }

    public async Task<string> TranslateAsync(string text, string? context, string target, CancellationToken cancellationToken = default)
    {
        var inferenceParams = new InferenceParams
        {
            MaxTokens = Math.Clamp(text.Length + 40, 64, 400),
            AntiPrompts = ["<|im_end|>", "</s>"],
            SamplingPipeline = new DefaultSamplingPipeline
            {
                Temperature = 0.2f,
                TopK = 20,
                TopP = 0.9f,
                RepeatPenalty = 1.0f
            }
        };

        var builder = new StringBuilder();
        await foreach (var token in _executor.InferAsync(BuildPrompt(text, context, target), inferenceParams, cancellationToken)
                           .ConfigureAwait(false))
        {
            builder.Append(token);
        }

        return Clean(builder.ToString());
    }

    private static string BuildPrompt(string text, string? context, string target)
    {
        var system =
            $"You are a professional simultaneous interpreter. Translate the user's text into {target}. " +
            "Reply with the translation only: no notes, no quotes, no explanations. Keep names, numbers and tone. " +
            "The text comes from live speech recognition and may be cut off mid-sentence or contain small errors: " +
            "translate it as faithfully as you can and add nothing. If it is already in " + target +
            ", repeat it unchanged. /no_think";

        var user = string.IsNullOrWhiteSpace(context)
            ? $"Text to translate:\n{text}"
            : $"Previous sentence (context only, do not translate it):\n{context}\n\nText to translate:\n{text}";

        return "<|im_start|>system\n" + system + "<|im_end|>\n" +
               "<|im_start|>user\n" + user + "<|im_end|>\n" +
               "<|im_start|>assistant\n<think>\n\n</think>\n\n";
    }

    private static string Clean(string raw)
    {
        var text = ThinkBlockRegex.Replace(raw, string.Empty);
        text = text.Replace("<|im_end|>", string.Empty).Trim();

        if (text.Length >= 2 && ((text[0] == '"' && text[^1] == '"') || (text[0] == '“' && text[^1] == '”')))
        {
            text = text[1..^1].Trim();
        }

        return text;
    }

    public void Dispose()
    {
        try
        {
            (_executor.GetType().GetProperty("Context")?.GetValue(_executor) as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Warn("LiveTranslatorEngine.Dispose: no se pudo liberar el contexto del modelo", ex);
        }

        _weights.Dispose();
    }
}
