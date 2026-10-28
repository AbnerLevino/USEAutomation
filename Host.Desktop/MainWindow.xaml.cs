using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;
using AutomationPlatform.Platform.Windows;

namespace AutomationPlatform.Host.Desktop;

/// <summary>
/// Janela de chat SOMENTE de leitura: o usuário não digita, só escolhe um botão. Ao abrir, ela NÃO
/// inicia nada: só mostra a saudação e as opções. As falas do computador aparecem como num assistente:
/// pontinhos de "digitando" que viram a própria fala, texto surgindo palavra por palavra, cada fala
/// subindo e aparecendo com suavidade. A janela não fecha sozinha. Sem regra de negócio aqui.
/// </summary>
public partial class MainWindow : Window
{
    // Modo demonstração: "--demo <cenario>" (ou ASSISTENTE_DEMO) troca o Windows por dados falsos.
    private static readonly string? DemoName = ReadDemoName();

    private readonly ChatRunner _runner = new(new DiagnosticEngine(CreateRegistry()));
    private readonly CancellationTokenSource _closing = new();

    private static string? ReadDemoName()
    {
        var args = Environment.GetCommandLineArgs();
        var at = Array.FindIndex(args, a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : Environment.GetEnvironmentVariable("ASSISTENTE_DEMO");
    }

    private static DiagnosticRegistry CreateRegistry() =>
        DemoName is null
            ? PrintingComposition.CreateRegistry()
            : PrintingComposition.CreateDemoRegistry(DemoName)
              ?? throw new InvalidOperationException(
                  $"Cenário de demonstração desconhecido: '{DemoName}'. Use um destes: {string.Join(", ", DemoPrintingProvider.Scenarios.Keys)}.");

    // ASSISTENTE_RITMO=instantaneo existe só para os testes automáticos da janela.
    private readonly ChatPacing _pacing =
        Environment.GetEnvironmentVariable("ASSISTENTE_RITMO") == "instantaneo" ? ChatPacing.Instant : ChatPacing.Natural;

    public MainWindow()
    {
        InitializeComponent();
        if (DemoName is not null)
        {
            Title += " (DEMONSTRAÇÃO)";
            NewBubble(fromComputer: true, out var warning);
            warning.Text = $"⚠ MODO DEMONSTRAÇÃO: os dados abaixo são FALSOS (cenário \"{DemoName}\"). Nada do seu computador é lido.";
            warning.FontWeight = FontWeights.SemiBold;
        }
        NewBubble(fromComputer: true, out var greeting);
        greeting.Text = "Olá! Escolha uma das opções abaixo e eu mostro, passo a passo, o que estou verificando.";
        Closing += (_, _) => _closing.Cancel();
    }

    private void OnFullClick(object sender, RoutedEventArgs e) =>
        _ = StartAsync(FullPrintingDiagnostic.DiagnosticId, "Diagnóstico completo");

    private void OnNotPrintingClick(object sender, RoutedEventArgs e) =>
        _ = StartAsync(NotPrintingScenario.DiagnosticId, "Impressora não está imprimindo");

    private async Task StartAsync(string diagnosticId, string whatTheUserChose)
    {
        SetButtons(enabled: false);
        NewBubble(fromComputer: false, out var choice);
        choice.Text = whatTheUserChose;

        // Dá um instante para a escolha "ser lida" antes de o computador começar a digitar.
        await Wait(_pacing.AfterUserMessage);

        var channel = Channel.CreateUnbounded<ChatMessage>();
        var producer = Task.Run(async () =>
        {
            try { await _runner.RunAsync(diagnosticId, m => channel.Writer.TryWrite(m), _closing.Token); }
            finally { channel.Writer.TryComplete(); }
        });

        try
        {
            string? previous = null;
            await foreach (var message in channel.Reader.ReadAllAsync(_closing.Token))
            {
                // Tempo para ler a fala anterior; só então a próxima começa a ser digitada.
                await Wait(_pacing.Reading(previous));
                previous = message.Text + message.Detail;

                // Como num chatbot: aparecem os pontinhos, e a MESMA bolha vira a fala.
                var typing = NewTypingBubble();
                await Wait(_pacing.Thinking(message.Text + message.Detail));

                if (message.Kind == ChatKind.Narration)
                {
                    var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#111827") };
                    typing.Child = text;
                    await StreamAsync(text, message.Text);
                }
                else
                {
                    Chat.Children.Remove(typing);
                    await ShowCardAsync(message);
                }
            }
            await producer;
        }
        catch (OperationCanceledException) { /* a janela foi fechada pelo usuário */ }
        finally { SetButtons(enabled: true); }
    }

    private Task Wait(TimeSpan time) => time > TimeSpan.Zero ? Task.Delay(time, _closing.Token) : Task.CompletedTask;

    /// <summary>Faz o texto aparecer palavra por palavra, com a rolagem acompanhando.</summary>
    private async Task StreamAsync(TextBlock target, string text)
    {
        foreach (var token in ChatPacing.Tokens(text))
        {
            Motion.AppendFading(target, token);
            SmoothScroll.ToEnd(Scroll);
            await Wait(_pacing.Delay(token));
        }
    }

    /// <summary>Tudo o que já está na conversa fica mais apagado quando uma fala nova chega.</summary>
    private void DimPrevious()
    {
        foreach (var child in Chat.Children)
            if (child is Border border) Motion.Dim(border.Child);
    }

    private async Task ShowCardAsync(ChatMessage message)
    {
        DimPrevious();
        var color = message.Tone switch
        {
            ChatTone.Good => "#16A34A",
            ChatTone.Problem => "#DC2626",
            ChatTone.Error => "#B45309",
            _ => "#6B7280"
        };

        var body = new StackPanel();
        var card = new Border
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
        };
        Chat.Children.Add(card);
        Motion.Enter(card);
        SmoothScroll.ToEnd(Scroll);

        var headline = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 16, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap };
        body.Children.Add(headline);
        await StreamAsync(headline, message.Text);

        var rest = new List<string>();
        if (!string.IsNullOrWhiteSpace(message.Detail)) rest.Add(message.Detail);
        rest.AddRange(message.Lines ?? []);
        foreach (var line in rest)
        {
            var block = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = Brush("#111827") };
            body.Children.Add(block);
            await StreamAsync(block, line);
        }
        SmoothScroll.ToEnd(Scroll);
    }

    /// <summary>Bolha do computador com os pontinhos de "digitando" animados.</summary>
    private Border NewTypingBubble()
    {
        DimPrevious();
        var bubble = BubbleShell(fromComputer: true);
        bubble.Child = Motion.TypingDots(Brush("#9CA3AF"));
        Chat.Children.Add(bubble);
        Motion.Enter(bubble);
        SmoothScroll.ToEnd(Scroll);
        return bubble;
    }

    private Border NewBubble(bool fromComputer, out TextBlock text)
    {
        text = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush(fromComputer ? "#111827" : "#FFFFFF") };
        DimPrevious();
        var bubble = BubbleShell(fromComputer);
        bubble.Child = text;
        Chat.Children.Add(bubble);
        Motion.Enter(bubble);
        SmoothScroll.ToEnd(Scroll);
        return bubble;
    }

    private static Border BubbleShell(bool fromComputer) => new()
    {
        Background = Brush(fromComputer ? "#FFFFFF" : "#2563EB"),
        BorderBrush = Brush("#E5E7EB"),
        BorderThickness = new Thickness(fromComputer ? 1 : 0),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(12, 8, 12, 8),
        Margin = new Thickness(0, 0, 0, 8),
        MaxWidth = 440,
        HorizontalAlignment = fromComputer ? HorizontalAlignment.Left : HorizontalAlignment.Right
    };

    private void SetButtons(bool enabled) => FullButton.IsEnabled = NotPrintingButton.IsEnabled = enabled;

    private static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
}
