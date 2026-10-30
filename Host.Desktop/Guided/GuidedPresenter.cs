using System.Threading.Channels;
using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Host.Desktop.Chat;

namespace AutomationPlatform.Host.Desktop.Guided;

/// <summary>
/// Liga o fluxo guiado à tela. Roda o fluxo em segundo plano e consome, NA ORDEM, tudo que ele produz
/// (eventos e perguntas) numa fila só, mostrando cada coisa pelo <see cref="IGuidedScreen"/> no ritmo da conversa.
/// No fim mostra a conclusão. Se a tela falhar ou for cancelada, o fluxo é cancelado junto.
/// </summary>
public sealed class GuidedPresenter(ChatPacing pacing)
{
    public async Task<DiagnosticReport> RunAsync(
        Func<IUserInteraction, GuidedNotPrintingFlow> createFlow, IGuidedScreen screen, CancellationToken cancellationToken)
    {
        var queue = Channel.CreateUnbounded<ScreenItem>(new UnboundedChannelOptions { SingleReader = true });
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var flow = createFlow(new WindowInteraction(queue.Writer));

        var running = Task.Run(async () =>
        {
            try { return await flow.RunAsync(new QueueProgress(queue.Writer), linked.Token); }
            finally { queue.Writer.TryComplete(); }
        }, CancellationToken.None);

        try
        {
            var board = new GuidedBoard();
            await foreach (var item in queue.Reader.ReadAllAsync(linked.Token))
                await ShowAsync(item, board, screen, linked.Token);

            var report = await running;
            await screen.ShowConclusionAsync(ReportToChat.GuidedConclusion(report), linked.Token);
            return report;
        }
        catch
        {
            // A tela falhou ou foi cancelada: solta o fluxo (que pode estar esperando uma resposta) e espera ele terminar.
            await linked.CancelAsync();
            try { await running; } catch { /* o erro que importa é o da tela */ }
            throw;
        }
    }

    private async Task ShowAsync(ScreenItem item, GuidedBoard board, IGuidedScreen screen, CancellationToken ct)
    {
        switch (item)
        {
            case EventItem { Event: var e }:
                if (board.Apply(e))
                {
                    screen.ShowBoard(board);
                    if (e is StepFinished or PrinterStateChanged && pacing.AfterStep > TimeSpan.Zero)
                        await Task.Delay(pacing.AfterStep, ct);
                }
                if (GuidedSpeech.For(e) is { } speech)
                    await screen.SpeakAsync(speech, ct);
                break;

            case ConfirmItem c:
                Complete(c.Reply, await screen.ConfirmAsync(c.Prompt, ct));
                break;
            case ChooseItem c:
                Complete(c.Reply, await screen.ChooseAsync(c.Question, c.Options, ct));
                break;
            case PhysicalItem p:
                Complete(p.Reply, await screen.RequestPhysicalAsync(p.Instruction, ct));
                break;
            case ResolvedItem r:
                Complete(r.Reply, await screen.AskResolvedAsync(r.Question, ct));
                break;
        }
    }

    private static void Complete<T>(TaskCompletionSource<T> reply, T answer) => reply.TrySetResult(answer);

    /// <summary>Entrega os eventos do fluxo à fila, um a um e na ordem (Progress&lt;T&gt; poderia embaralhar).</summary>
    private sealed class QueueProgress(ChannelWriter<ScreenItem> queue) : IProgress<GuidedEvent>
    {
        public void Report(GuidedEvent value) => queue.TryWrite(new EventItem(value));
    }
}
