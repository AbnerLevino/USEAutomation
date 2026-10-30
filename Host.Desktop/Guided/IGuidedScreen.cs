using AutomationPlatform.Contracts;
using AutomationPlatform.Host.Desktop.Chat;

namespace AutomationPlatform.Host.Desktop.Guided;

/// <summary>
/// O que o apresentador precisa que a tela saiba fazer. A implementação real (WPF) desenha; a dos testes
/// só anota o que foi pedido. Assim a ordem e as regras da apresentação são testadas sem janela.
/// </summary>
public interface IGuidedScreen
{
    Task SpeakAsync(string text, CancellationToken cancellationToken);

    /// <summary>Mostra o quadro (cria na primeira vez, atualiza nas seguintes).</summary>
    void ShowBoard(GuidedBoard board);

    Task<bool> ConfirmAsync(ConfirmationPrompt prompt, CancellationToken cancellationToken);
    Task<Choice?> ChooseAsync(string question, IReadOnlyList<Choice> options, CancellationToken cancellationToken);
    Task<PhysicalActionReply> RequestPhysicalAsync(string instruction, CancellationToken cancellationToken);
    Task<ResolutionAnswer> AskResolvedAsync(string question, CancellationToken cancellationToken);

    Task ShowConclusionAsync(ChatMessage conclusion, CancellationToken cancellationToken);
}
