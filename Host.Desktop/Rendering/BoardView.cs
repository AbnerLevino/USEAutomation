using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutomationPlatform.Contracts;
using AutomationPlatform.Host.Desktop.Guided;

namespace AutomationPlatform.Host.Desktop.Rendering;

/// <summary>
/// Desenha o quadro ao vivo: as impressoras como cartões e a lista das etapas da investigação.
/// Só desenha o que o <see cref="GuidedBoard"/> diz; não decide nada. A raiz é um Grid (e não um Border)
/// para o esmaecimento das falas antigas não apagar o quadro, que precisa continuar legível.
/// </summary>
public sealed class BoardView
{
    // Ícones da fonte de símbolos do próprio Windows (Segoe MDL2 Assets): impressora e documento (fila virtual).
    private const string PrinterGlyph = "";
    private const string VirtualGlyph = "";

    private readonly StackPanel _printers = new();
    private readonly StackPanel _steps = new() { Margin = new Thickness(0, 10, 0, 0) };

    public BoardView()
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "O que estou vendo", FontWeight = FontWeights.SemiBold, Foreground = ChatView.Brush("#374151") });
        body.Children.Add(_printers);
        body.Children.Add(_steps);

        Root = new Grid
        {
            Margin = new Thickness(0, 4, 0, 12),
            MaxWidth = 480,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                new Border
                {
                    Background = ChatView.Brush("#FFFFFF"),
                    BorderBrush = ChatView.Brush("#E5E7EB"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14, 10, 14, 12),
                    Child = body
                }
            }
        };
    }

    public FrameworkElement Root { get; }

    public void Update(GuidedBoard board)
    {
        _printers.Children.Clear();
        foreach (var printer in board.Printers) _printers.Children.Add(PrinterCard(printer));

        _steps.Children.Clear();
        foreach (var step in board.Steps) _steps.Children.Add(StepLine(step));
    }

    private static FrameworkElement PrinterCard(PrinterRow printer)
    {
        var color = printer.State switch
        {
            PrinterCardState.Ok => "#16A34A",
            PrinterCardState.Problem => "#DC2626",
            _ => "#9CA3AF"
        };

        var tags = new List<string>();
        if (printer.IsVirtual) tags.Add("virtual");
        if (printer.IsDefault == true) tags.Add("padrão");

        var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        text.Children.Add(new TextBlock
        {
            Text = tags.Count == 0 ? printer.Name : $"{printer.Name}  ·  {string.Join(", ", tags)}",
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ChatView.Brush("#111827")
        });
        text.Children.Add(new TextBlock { Text = printer.StateText, Foreground = ChatView.Brush(color), FontSize = 12 });

        var row = new DockPanel();
        row.Children.Add(new TextBlock
        {
            Text = printer.IsVirtual ? VirtualGlyph : PrinterGlyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 26,
            Foreground = ChatView.Brush(color),
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(text);

        return new Border
        {
            BorderBrush = ChatView.Brush(color),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Background = ChatView.Brush("#F9FAFB"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 8, 0, 0),
            Child = row
        };
    }

    private static FrameworkElement StepLine(StepRow step)
    {
        var (mark, color) = step.Status switch
        {
            StepStatus.Healthy => ("✓", "#16A34A"),
            StepStatus.Problem => ("✗", "#DC2626"),
            StepStatus.NotEvaluable => ("?", "#B45309"),
            _ => ("•", "#9CA3AF")
        };

        var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), FontSize = 13 };
        line.Inlines.Add(new System.Windows.Documents.Run(mark + "  ") { Foreground = ChatView.Brush(color), FontWeight = FontWeights.Bold });
        line.Inlines.Add(new System.Windows.Documents.Run(step.Label) { Foreground = ChatView.Brush("#111827") });
        if (step.Status == StepStatus.Running)
            line.Inlines.Add(new System.Windows.Documents.Run("  verificando…") { Foreground = ChatView.Brush("#9CA3AF") });
        return line;
    }
}
