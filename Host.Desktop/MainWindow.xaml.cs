using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;
using AutomationPlatform.Platform.Windows;

namespace AutomationPlatform.Host.Desktop;

/// <summary>
/// Janela de chat SOMENTE de leitura: o usuário não digita, só escolhe um botão. As falas do computador
/// chegam em tempo real, uma de cada vez, com "digitando…" entre elas. Sem regra de negócio aqui.
/// </summary>
public partial class MainWindow : Window
{
    // Tempo entre uma fala e outra, só para dar para acompanhar o que o computador está fazendo.
    private static readonly TimeSpan Pace = TimeSpan.FromMilliseconds(550);

    private readonly ChatRunner _runner = new(new DiagnosticEngine(PrintingComposition.CreateRegistry()));
    private readonly CancellationTokenSource _closing = new();

    public MainWindow()
    {
        InitializeComponent();
        AddBubble("Olá! Escolha uma opção abaixo e eu mostro, passo a passo, o que estou verificando.", fromComputer: true);
        Closing += (_, _) => _closing.Cancel();
    }

    private void OnFullClick(object sender, RoutedEventArgs e) =>
        _ = StartAsync(FullPrintingDiagnostic.DiagnosticId, "Diagnóstico completo");

    private void OnNotPrintingClick(object sender, RoutedEventArgs e) =>
        _ = StartAsync(NotPrintingScenario.DiagnosticId, "Impressora não está imprimindo");

    private async Task StartAsync(string diagnosticId, string whatTheUserChose)
    {
        SetButtons(enabled: false);
        AddBubble(whatTheUserChose, fromComputer: false);

        var channel = Channel.CreateUnbounded<ChatMessage>();
        var producer = Task.Run(async () =>
        {
            try { await _runner.RunAsync(diagnosticId, m => channel.Writer.TryWrite(m), _closing.Token); }
            finally { channel.Writer.TryComplete(); }
        });

        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync(_closing.Token))
            {
                var typing = AddBubble("digitando…", fromComputer: true, subtle: true);
                await Task.Delay(Pace, _closing.Token);
                Chat.Children.Remove(typing);
                Show(message);
            }
            await producer;
        }
        catch (OperationCanceledException) { /* janela fechada */ }
        finally { SetButtons(enabled: true); }
    }

    private void Show(ChatMessage message)
    {
        if (message.Kind == ChatKind.Narration) AddBubble(message.Text, fromComputer: true);
        else AddCard(message);
    }

    private Border AddBubble(string text, bool fromComputer, bool subtle = false)
    {
        var bubble = new Border
        {
            Background = Brush(fromComputer ? "#FFFFFF" : "#2563EB"),
            BorderBrush = Brush("#E5E7EB"),
            BorderThickness = new Thickness(fromComputer ? 1 : 0),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = 440,
            HorizontalAlignment = fromComputer ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush(fromComputer ? (subtle ? "#9CA3AF" : "#111827") : "#FFFFFF"),
                FontStyle = subtle ? FontStyles.Italic : FontStyles.Normal
            }
        };
        Chat.Children.Add(bubble);
        Scroll.ScrollToEnd();
        return bubble;
    }

    private void AddCard(ChatMessage message)
    {
        var color = message.Tone switch
        {
            ChatTone.Good => "#16A34A",
            ChatTone.Problem => "#DC2626",
            ChatTone.Error => "#B45309",
            _ => "#6B7280"
        };

        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = message.Text, FontWeight = FontWeights.SemiBold, FontSize = 16, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(message.Detail))
            body.Children.Add(new TextBlock { Text = message.Detail, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = Brush("#374151") });
        foreach (var line in message.Lines ?? [])
            body.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = Brush("#111827") });

        Chat.Children.Add(new Border
        {
            Background = Brush("#FFFFFF"),
            BorderBrush = Brush(color),
            BorderThickness = new Thickness(4, 0, 0, 0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 12),
            Margin = new Thickness(0, 4, 0, 12),
            MaxWidth = 480,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = body
        });
        Scroll.ScrollToEnd();
    }

    private void SetButtons(bool enabled) => FullButton.IsEnabled = NotPrintingButton.IsEnabled = enabled;

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
