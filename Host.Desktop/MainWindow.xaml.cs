using System.Windows;
using AutomationPlatform.Host.Desktop.Composition;
using AutomationPlatform.Host.Desktop.Guided;
using AutomationPlatform.Host.Desktop.Rendering;

namespace AutomationPlatform.Host.Desktop;

/// <summary>
/// Janela do assistente: o usuário não digita, só escolhe botões. Ao abrir, ela NÃO inicia nada: só mostra a
/// saudação e a opção. Quem conduz o fluxo é o <see cref="GuidedPresenter"/>; quem desenha é o
/// <see cref="WpfGuidedScreen"/>. A janela não fecha sozinha. Sem regra de negócio aqui.
/// </summary>
public partial class MainWindow : Window
{
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
        greeting.Text = "Olá! Se a sua impressora não está imprimindo, clique no botão abaixo. Eu mostro, passo a passo, o que estou verificando.";
        Closing += (_, _) => _closing.Cancel();
    }

    private void OnNotPrintingClick(object sender, RoutedEventArgs e) => _ = StartAsync();

    private async Task StartAsync()
    {
        NotPrintingButton.IsEnabled = false;
        _view.AddBubble(fromComputer: false, out var choice);
        choice.Text = "Impressora não imprime";

        // Dá um instante para a escolha "ser lida" antes de o computador começar a digitar.
        try
        {
            await _view.WaitAsync(_view.Pacing.AfterUserMessage);
            await new GuidedPresenter(_view.Pacing).RunAsync(AppComposition.CreateGuidedFlow, new WpfGuidedScreen(_view), _closing.Token);
        }
        catch (OperationCanceledException) { /* a janela foi fechada pelo usuário */ }
        finally { NotPrintingButton.IsEnabled = true; }
    }
}
