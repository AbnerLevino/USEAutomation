using AutomationPlatform.Contracts;

namespace AutomationPlatform.Host.Desktop.Guided;

/// <summary>
/// Um item da fila única que a tela consome NA ORDEM: eventos do fluxo e perguntas ao usuário na mesma fila.
/// Assim uma pergunta só aparece depois de a tela mostrar tudo que veio antes dela.
/// </summary>
public abstract class ScreenItem;

public sealed class EventItem(GuidedEvent @event) : ScreenItem
{
    public GuidedEvent Event { get; } = @event;
}

/// <summary>Uma pergunta que espera resposta: a tela completa <see cref="Reply"/> quando o usuário clica.</summary>
public abstract class PromptItem<T> : ScreenItem
{
    public TaskCompletionSource<T> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class ConfirmItem(ConfirmationPrompt prompt) : PromptItem<bool>
{
    public ConfirmationPrompt Prompt { get; } = prompt;
}

public sealed class ChooseItem(string question, IReadOnlyList<Choice> options) : PromptItem<Choice?>
{
    public string Question { get; } = question;
    public IReadOnlyList<Choice> Options { get; } = options;
}

public sealed class PhysicalItem(string instruction) : PromptItem<PhysicalActionReply>
{
    public string Instruction { get; } = instruction;
}

public sealed class ResolvedItem(string question) : PromptItem<ResolutionAnswer>
{
    public string Question { get; } = question;
}
