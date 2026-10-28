using AutomationPlatform.Contracts;
using AutomationPlatform.Engine;

namespace AutomationPlatform.Host.Desktop;

/// <summary>
/// Roda um diagnóstico e entrega, em tempo real, o que o computador "fala" (a narração) e, no fim,
/// o cartão de conclusão. Não conhece a tela: quem chama decide como e quando exibir cada mensagem.
/// </summary>
public sealed class ChatRunner(DiagnosticEngine engine)
{
    public async Task RunAsync(string diagnosticId, Action<ChatMessage> onMessage, CancellationToken cancellationToken)
    {
        var progress = new SyncProgress(p => onMessage(new ChatMessage(ChatKind.Narration, p.Message)));
        var report = await engine.RunAsync(diagnosticId, progress, cancellationToken: cancellationToken);
        onMessage(ReportToChat.Conclusion(report));
    }

    // Progress<T> entrega em outra thread e pode embaralhar a ordem; este é síncrono e previsível.
    private sealed class SyncProgress(Action<DiagnosticProgress> handler) : IProgress<DiagnosticProgress>
    {
        public void Report(DiagnosticProgress value) => handler(value);
    }
}
