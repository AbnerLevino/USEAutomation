using System.Threading.Channels;
using System.Windows;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;
using AutomationPlatform.Host.Desktop.Chat;
using AutomationPlatform.Host.Desktop.Composition;
using AutomationPlatform.Host.Desktop.Rendering;

namespace AutomationPlatform.Host.Desktop;

/// <summary>
/// Janela de chat SOMENTE de leitura: o usuário não digita, só escolhe um botão. Ao abrir, ela NÃO
/// inicia nada: só mostra a saudação e as opções. As falas do computador aparecem como num assistente
/// (quem desenha é o <see cref="ChatView"/>). A janela não fecha sozinha. Sem regra de negócio aqui.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ChatRunner _runner = new(new DiagnosticEngine(AppComposition.CreateRegistry()));
    private readonly CancellationTokenSource _closing = new();
    private readonly ChatView _view;

    public MainWindow()
    {
        InitializeComponent();
        _view = new ChatView(Scroll, ChatPanel, AppComposition.Pacing, _closing.Token);

        if (AppComposition.DemoName is { } demo)
        {
            Title += " (DEMONSTRAÇÃO)";
            _view.AddBubble(fromComputer: true, out var warning);
            warning.Text = $"⚠ MODO DEMONSTRAÇÃO: os dados abaixo são FALSOS (cenário \"{demo}\"). Nada do seu computador é lido.";
            warning.FontWeight = FontWeights.SemiBold;
        }
        _view.AddBubble(fromComputer: true, out var greeting);
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
        _view.AddBubble(fromComputer: false, out var choice);
        choice.Text = whatTheUserChose;

        // Dá um instante para a escolha "ser lida" antes de o computador começar a digitar.
        await _view.WaitAsync(_view.Pacing.AfterUserMessage);

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
                await _view.WaitAsync(_view.Pacing.Reading(previous));
                previous = message.Text + message.Detail;

                // Como num chatbot: aparecem os pontinhos, e a MESMA bolha vira a fala.
                var typing = _view.ShowTyping();
                await _view.WaitAsync(_view.Pacing.Thinking(message.Text + message.Detail));

                if (message.Kind == ChatKind.Narration)
                {
                    await _view.SpeakAsync(typing, message.Text);
                }
                else
                {
                    _view.Remove(typing);
                    await _view.ShowCardAsync(message);
                }
            }
            await producer;
        }
        catch (OperationCanceledException) { /* a janela foi fechada pelo usuário */ }
        finally { SetButtons(enabled: true); }
    }

    private void SetButtons(bool enabled) => FullButton.IsEnabled = NotPrintingButton.IsEnabled = enabled;
}
