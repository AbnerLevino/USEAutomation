using System.Threading.Channels;
using AutomationPlatform.Contracts;

namespace AutomationPlatform.Host.Desktop.Guided;

/// <summary>
/// A interação da janela: cada pergunta do fluxo vira um item na fila da tela e espera o clique do usuário.
/// Não desenha nada (a tela é quem mostra os botões). Se o fluxo for cancelado ou a janela fechar,
/// a pergunta pendente é cancelada junto: nada fica esperando para sempre.
/// </summary>
public sealed class WindowInteraction(ChannelWriter<ScreenItem> queue) : IUserInteraction
{
    public Task<bool> ConfirmAsync(ConfirmationPrompt prompt, CancellationToken cancellationToken) =>
        AskAsync(new ConfirmItem(prompt), cancellationToken);

    public Task<Choice?> ChooseAsync(string question, IReadOnlyList<Choice> options, CancellationToken cancellationToken) =>
        AskAsync(new ChooseItem(question, options), cancellationToken);

    public Task<PhysicalActionReply> RequestPhysicalActionAsync(string instruction, CancellationToken cancellationToken) =>
        AskAsync(new PhysicalItem(instruction), cancellationToken);

    public Task<ResolutionAnswer> AskIfResolvedAsync(string question, CancellationToken cancellationToken) =>
        AskAsync(new ResolvedItem(question), cancellationToken);

    private async Task<T> AskAsync<T>(PromptItem<T> item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var registration = cancellationToken.Register(() => item.Reply.TrySetCanceled(cancellationToken));
        if (!queue.TryWrite(item)) throw new OperationCanceledException("A tela já não está recebendo perguntas.");
        return await item.Reply.Task;
    }
}
