using System.Windows;
using Wpf.Ui.Controls;

namespace LoopbackTranscriber.Models;

public sealed class SummaryLine
{
    public required string Text { get; init; }

    public required bool IsBullet { get; init; }

    public bool IsTopic { get; init; }

    public int Level { get; init; }

    public Thickness LineMargin => IsTopic ? new Thickness(0, 6, 0, 2) : new Thickness(Level * 18, 2, 0, 2);

    public FontWeight TextWeight => IsTopic ? FontWeights.SemiBold : FontWeights.Normal;
}

public sealed class SummarySection
{
    public required string Heading { get; init; }

    public required SymbolRegular Icon { get; init; }

    public required bool HasHeading { get; init; }

    public required IReadOnlyList<SummaryLine> Lines { get; init; }
}
