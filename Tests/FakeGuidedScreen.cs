using AutomationPlatform.Contracts;
using AutomationPlatform.Host.Desktop.Chat;
using AutomationPlatform.Host.Desktop.Guided;

namespace AutomationPlatform.Tests;

/// <summary>
/// Tela falsa: anota, em ordem, tudo que o apresentador pediu e responde às perguntas com o roteiro do teste.
/// Pergunta sem resposta programada falha o teste.
/// </summary>
internal sealed class FakeGuidedScreen : IGuidedScreen
{
    private readonly Queue<bool> _confirm = new();
    private readonly Queue<string?> _choose = new();
    private readonly Queue<PhysicalActionReply> _physical = new();
    private readonly Queue<ResolutionAnswer> _resolved = new();

    /// <summary>O que a tela fez, na ordem: "speak:...", "board", "confirm", "choose", "physical", "resolved", "conclusion:...".</summary>
    public List<string> Log { get; } = [];
    public ChatMessage? Conclusion { get; private set; }
    public List<(int Printers, int Steps)> BoardSizes { get; } = [];

    /// <summary>Roda quando o usuário "faz" o que foi pedido (conserta o computador falso).</summary>
    public Action? OnPhysicalDone { get; set; }

    /// <summary>O que o usuário preenche na avaliação. Vazio por padrão (não avaliou).</summary>
    public ServiceFeedback Feedback { get; set; } = ServiceFeedback.Create(null, null);
    public bool FeedbackAsked { get; private set; }
    /// <summary>Faz a tela falhar em um tipo de pergunta, para provar que o fluxo não fica esperando.</summary>
    public Func<Exception?>? FailOnPhysical { get; set; }

    public FakeGuidedScreen WillConfirm(params bool[] a) { foreach (var x in a) _confirm.Enqueue(x); return this; }
    public FakeGuidedScreen WillChoose(params string?[] a) { foreach (var x in a) _choose.Enqueue(x); return this; }
    public FakeGuidedScreen WillDoPhysical(params PhysicalActionReply[] a) { foreach (var x in a) _physical.Enqueue(x); return this; }
    public FakeGuidedScreen WillAnswerResolved(params ResolutionAnswer[] a) { foreach (var x in a) _resolved.Enqueue(x); return this; }

    public Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        Log.Add($"speak:{text}");
        return Task.CompletedTask;
    }

    public void ShowBoard(GuidedBoard board)
    {
        Log.Add("board");
        BoardSizes.Add((board.Printers.Count, board.Steps.Count));
    }

    public Task<bool> ConfirmAsync(ConfirmationPrompt prompt, CancellationToken cancellationToken)
    {
        Log.Add("confirm");
        return Task.FromResult(Next(_confirm, "confirmação"));
    }

    public Task<Choice?> ChooseAsync(string question, IReadOnlyList<Choice> options, CancellationToken cancellationToken)
    {
        Log.Add("choose");
        var id = Next(_choose, "escolha");
        return Task.FromResult(id is null ? null : options.Single(o => o.Id == id));
    }

    public Task<PhysicalActionReply> RequestPhysicalAsync(string instruction, CancellationToken cancellationToken)
    {
        Log.Add("physical");
        if (FailOnPhysical?.Invoke() is { } failure) throw failure;
        var reply = Next(_physical, "ação física");
        if (reply == PhysicalActionReply.Done) OnPhysicalDone?.Invoke();
        return Task.FromResult(reply);
    }

    public Task<ResolutionAnswer> AskResolvedAsync(string question, CancellationToken cancellationToken)
    {
        Log.Add("resolved");
        return Task.FromResult(Next(_resolved, "fechamento"));
    }

    public Task<ServiceFeedback> CollectFeedbackAsync(CancellationToken cancellationToken)
    {
        FeedbackAsked = true;
        Log.Add("feedback");
        return Task.FromResult(Feedback);
    }

    public Task ShowClosedAsync(CancellationToken cancellationToken)
    {
        Log.Add("closed");
        return Task.CompletedTask;
    }

    public Task ShowConclusionAsync(ChatMessage conclusion, CancellationToken cancellationToken)
    {
        Conclusion = conclusion;
        Log.Add($"conclusion:{conclusion.Text}");
        return Task.CompletedTask;
    }

    private static T Next<T>(Queue<T> queue, string kind) =>
        queue.Count > 0 ? queue.Dequeue() : throw new InvalidOperationException($"Pergunta de {kind} sem resposta programada.");
}
