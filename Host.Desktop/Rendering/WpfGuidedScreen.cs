using System.Windows;
using System.Windows.Controls;
using AutomationPlatform.Contracts;
using AutomationPlatform.Host.Desktop.Chat;
using AutomationPlatform.Host.Desktop.Guided;

namespace AutomationPlatform.Host.Desktop.Rendering;

/// <summary>
/// A tela real do fluxo guiado: traduz cada pedido do apresentador em bolhas, quadro e botões do
/// <see cref="ChatView"/>. Só desenha; as regras ficam em <see cref="GuidedPresenter"/>.
/// </summary>
public sealed class WpfGuidedScreen(ChatView view, Border boardHost) : IGuidedScreen
{
    private BoardView? _board;
    private string? _previousSpeech;

    public async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        // Tempo para ler a fala anterior; só então a próxima começa a ser digitada.
        await view.WaitAsync(view.Pacing.Reading(_previousSpeech));
        _previousSpeech = text;

        // Como num chatbot: aparecem os pontinhos, e a MESMA bolha vira a fala.
        var typing = view.ShowTyping();
        await view.WaitAsync(view.Pacing.Thinking(text));
        await view.SpeakAsync(typing, text);
    }

    public void ShowBoard(GuidedBoard board)
    {
        // O quadro mora num lugar fixo da janela (embaixo); o log da conversa é que rola.
        if (_board is null)
        {
            _board = new BoardView();
            boardHost.Child = _board.Root;
            boardHost.Visibility = Visibility.Visible;
        }
        _board.Update(board);
    }

    public Task<bool> ConfirmAsync(ConfirmationPrompt prompt, CancellationToken cancellationToken) =>
        view.AskAsync(GuidedSpeech.ConfirmText(prompt),
        [
            new ChatButton<bool>(GuidedSpeech.Yes, true, Primary: true),
            new ChatButton<bool>(GuidedSpeech.NotNow, false)
        ]);

    public Task<Choice?> ChooseAsync(string question, IReadOnlyList<Choice> options, CancellationToken cancellationToken) =>
        view.AskAsync<Choice?>(question,
        [
            .. options.Select(o => new ChatButton<Choice?>(o.Label, o, Primary: true)),
            new ChatButton<Choice?>(GuidedSpeech.Cancel, null)
        ]);

    public Task<PhysicalActionReply> RequestPhysicalAsync(string instruction, CancellationToken cancellationToken) =>
        view.AskAsync(instruction,
        [
            new ChatButton<PhysicalActionReply>(GuidedSpeech.Done, PhysicalActionReply.Done, Primary: true),
            new ChatButton<PhysicalActionReply>(GuidedSpeech.CouldNot, PhysicalActionReply.Skipped),
            new ChatButton<PhysicalActionReply>(GuidedSpeech.Cancel, PhysicalActionReply.Cancel)
        ]);

    public Task<ResolutionAnswer> AskResolvedAsync(string question, CancellationToken cancellationToken) =>
        view.AskAsync(question,
        [
            new ChatButton<ResolutionAnswer>(GuidedSpeech.Worked, ResolutionAnswer.Worked, Primary: true),
            new ChatButton<ResolutionAnswer>(GuidedSpeech.StillBroken, ResolutionAnswer.StillNotWorking),
            new ChatButton<ResolutionAnswer>(GuidedSpeech.CannotTest, ResolutionAnswer.CannotTest)
        ]);

    public Task<ServiceFeedback> CollectFeedbackAsync(CancellationToken cancellationToken) => view.AskFeedbackAsync();

    public Task ShowClosedAsync(CancellationToken cancellationToken) => SpeakAsync(GuidedSpeech.Closed, cancellationToken);

    public async Task ShowConclusionAsync(ChatMessage conclusion, CancellationToken cancellationToken)
    {
        await view.WaitAsync(view.Pacing.Reading(_previousSpeech));
        await view.ShowCardAsync(conclusion);
    }
}
