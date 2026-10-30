using AutomationPlatform.Contracts;

namespace AutomationPlatform.Tests;

/// <summary>
/// Interação com respostas programadas, na ordem em que as perguntas chegam. Pergunta sem resposta
/// programada falha o teste: o fluxo não pode perguntar nada que o teste não esperava.
/// </summary>
public sealed class FakeUserInteraction : IUserInteraction
{
    private readonly Queue<bool> _confirmations = new();
    private readonly Queue<string?> _choices = new();
    private readonly Queue<PhysicalActionReply> _physical = new();
    private readonly Queue<ResolutionAnswer> _resolved = new();

    public List<ConfirmationPrompt> Confirmed { get; } = [];
    public List<string> Questions { get; } = [];
    public List<string> PhysicalRequests { get; } = [];

    public FakeUserInteraction WillConfirm(params bool[] answers) { foreach (var a in answers) _confirmations.Enqueue(a); return this; }
    /// <summary>Id da opção escolhida; null = o usuário cancelou.</summary>
    public FakeUserInteraction WillChoose(params string?[] ids) { foreach (var i in ids) _choices.Enqueue(i); return this; }
    public FakeUserInteraction WillDoPhysical(params PhysicalActionReply[] replies) { foreach (var r in replies) _physical.Enqueue(r); return this; }
    public FakeUserInteraction WillAnswerResolved(params ResolutionAnswer[] answers) { foreach (var a in answers) _resolved.Enqueue(a); return this; }

    public Task<bool> ConfirmAsync(ConfirmationPrompt prompt, CancellationToken cancellationToken)
    {
        Confirmed.Add(prompt);
        return Task.FromResult(Next(_confirmations, "confirmação"));
    }

    public Task<Choice?> ChooseAsync(string question, IReadOnlyList<Choice> options, CancellationToken cancellationToken)
    {
        Questions.Add(question);
        var id = Next(_choices, "escolha");
        if (id is null) return Task.FromResult<Choice?>(null);
        return Task.FromResult<Choice?>(options.Single(o => o.Id == id));
    }

    public Task<PhysicalActionReply> RequestPhysicalActionAsync(string instruction, CancellationToken cancellationToken)
    {
        PhysicalRequests.Add(instruction);
        return Task.FromResult(Next(_physical, "ação física"));
    }

    public Task<ResolutionAnswer> AskIfResolvedAsync(string question, CancellationToken cancellationToken)
    {
        Questions.Add(question);
        return Task.FromResult(Next(_resolved, "fechamento"));
    }

    private static T Next<T>(Queue<T> queue, string kind) =>
        queue.Count > 0 ? queue.Dequeue() : throw new InvalidOperationException($"Pergunta de {kind} sem resposta programada.");
}
