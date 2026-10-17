using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LoopbackTranscriber.Models;
using LoopbackTranscriber.Services;

namespace LoopbackTranscriber.ViewModels;

public sealed partial class HistorySearchState : ObservableObject
{
    private static readonly CompareInfo Comparer = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions MatchOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    private readonly Func<IReadOnlyList<TranscriptHistoryItem>> _itemsProvider;
    private readonly Action _onChanged;
    private readonly DispatcherTimer _debounceTimer;
    private CancellationTokenSource? _cancellation;

    public HistorySearchState(Func<IReadOnlyList<TranscriptHistoryItem>> itemsProvider, Action onChanged)
    {
        _itemsProvider = itemsProvider;
        _onChanged = onChanged;
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounceTimer.Tick += async (_, _) =>
        {
            _debounceTimer.Stop();
            await RunSearchAsync();
        };
    }

    [ObservableProperty]
    private string _text = string.Empty;

    public HashSet<string>? Matches { get; private set; }

    partial void OnTextChanged(string value)
    {
        _debounceTimer.Stop();

        if (string.IsNullOrWhiteSpace(value))
        {
            _cancellation?.Cancel();
            ClearMatches();
            return;
        }

        _debounceTimer.Start();
    }

    public async void Recompute()
    {
        _debounceTimer.Stop();

        if (string.IsNullOrWhiteSpace(Text))
        {
            _cancellation?.Cancel();
            ClearMatches();
            return;
        }

        await RunSearchAsync();
    }

    private void ClearMatches()
    {
        if (Matches is null)
        {
            return;
        }

        Matches = null;
        _onChanged();
    }

    private async Task RunSearchAsync()
    {
        var query = Text.Trim();
        if (query.Length == 0)
        {
            ClearMatches();
            return;
        }

        var terms = ParseTerms(query);
        if (terms.Count == 0)
        {
            ClearMatches();
            return;
        }

        _cancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var token = cancellation.Token;

        var candidates = _itemsProvider().Select(SearchCandidate.From).ToList();

        try
        {
            var matches = await Task.Run(() => FindMatches(candidates, terms, token), token);

            if (token.IsCancellationRequested || !string.Equals(Text.Trim(), query, StringComparison.Ordinal))
            {
                return;
            }

            Matches = matches;
            _onChanged();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Warn("HistorySearchState.RunSearchAsync: fallo al buscar en el historial", ex);
        }
    }

    public static List<string> ParseTerms(string query)
    {
        var terms = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        void Flush()
        {
            if (current.Length > 0)
            {
                terms.Add(current.ToString());
                current.Clear();
            }
        }

        foreach (var c in query)
        {
            if (c == '"')
            {
                Flush();
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                Flush();
            }
            else
            {
                current.Append(c);
            }
        }

        Flush();

        return terms.Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
    }

    private static bool Contains(string? source, string term) =>
        !string.IsNullOrEmpty(source) && Comparer.IndexOf(source, term, MatchOptions) >= 0;

    private static HashSet<string> FindMatches(List<SearchCandidate> candidates, List<string> terms, CancellationToken token)
    {
        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();

            var pending = terms.Where(t => !Contains(candidate.Header, t) && !Contains(candidate.Summary, t)).ToList();

            if (pending.Count > 0)
            {
                var transcript = TryReadTranscript(candidate.TranscriptPath);
                pending = pending.Where(t => !Contains(transcript, t)).ToList();
            }

            if (pending.Count == 0)
            {
                matches.Add(candidate.Key);
            }
        }

        return matches;
    }

    private static string? TryReadTranscript(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"HistorySearchState: no se pudo leer \"{path}\" para buscar", ex);
            return null;
        }
    }

    private sealed record SearchCandidate(string Key, string Header, string? Summary, string? TranscriptPath)
    {
        public static SearchCandidate From(TranscriptHistoryItem item)
        {
            var extension = Path.GetExtension(item.FilePath);
            var readable = string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase);

            var header = string.Create(CultureInfo.InvariantCulture,
                $"{item.DisplayTitle} {item.AppName} {item.Timestamp:dd/MM/yyyy HH:mm} {item.Timestamp:yyyy-MM-dd}");

            return new SearchCandidate(item.FilePath, header, item.SummaryText, readable ? item.FilePath : null);
        }
    }
}
