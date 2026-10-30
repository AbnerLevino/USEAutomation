using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AutomationPlatform.Contracts;
using AutomationPlatform.Host.Desktop.Guided;

namespace AutomationPlatform.Host.Desktop.Rendering;

/// <summary>
/// Desenha o quadro ao vivo: as impressoras como cartões e a lista das etapas da investigação.
/// Só desenha o que o <see cref="GuidedBoard"/> diz; não decide nada. Fica fixo na parte de baixo da
/// janela (quem o hospeda é a janela), enquanto o log da conversa rola em cima. Compacto: cartões lado
/// a lado e etapas em duas colunas, para sobrar espaço ao log.
/// </summary>
public sealed class BoardView
{
    // Ícones da fonte de símbolos do próprio Windows (Segoe MDL2 Assets): impressora e documento (fila virtual).
    private const string PrinterGlyph = "";
    private const string VirtualGlyph = "";

    private readonly WrapPanel _printers = new();
    private readonly UniformGrid _steps = new() { Columns = 2, Margin = new Thickness(0, 8, 0, 0) };

    public BoardView()
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "O que estou vendo", FontWeight = FontWeights.SemiBold, Foreground = ChatView.Brush("#374151") });
        body.Children.Add(_printers);
        body.Children.Add(_steps);

        Root = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
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
            Text = printer.Name,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ChatView.Brush("#111827")
        });
        if (tags.Count > 0)
            text.Children.Add(new TextBlock { Text = string.Join(", ", tags), FontSize = 11, Foreground = ChatView.Brush("#6B7280") });
        text.Children.Add(new TextBlock { Text = printer.StateText, Foreground = ChatView.Brush(color), FontSize = 12, TextWrapping = TextWrapping.Wrap });

        var row = new DockPanel();
        row.Children.Add(new TextBlock
        {
            Text = printer.IsVirtual ? VirtualGlyph : PrinterGlyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 24,
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
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 8, 8, 0),
            Width = 232,
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

        var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 8, 0), FontSize = 13 };
        line.Inlines.Add(new System.Windows.Documents.Run(mark + "  ") { Foreground = ChatView.Brush(color), FontWeight = FontWeights.Bold });
        line.Inlines.Add(new System.Windows.Documents.Run(step.Label) { Foreground = ChatView.Brush("#111827") });
        if (step.Status == StepStatus.Running)
            line.Inlines.Add(new System.Windows.Documents.Run("  …") { Foreground = ChatView.Brush("#9CA3AF") });
        return line;
    }
}
