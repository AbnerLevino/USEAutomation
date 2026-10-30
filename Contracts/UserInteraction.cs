namespace AutomationPlatform.Contracts;

/// <summary>Uma opção de uma pergunta de escolha. <see cref="Id"/> é estável; <see cref="Label"/> é o que o usuário lê.</summary>
public sealed record Choice(string Id, string Label);

/// <summary>Resposta a um pedido de ação física ("ligue a impressora e me avise").</summary>
public enum PhysicalActionReply
{
    /// <summary>O usuário diz que fez: o motor deve conferir de novo, sem presumir que deu certo.</summary>
    Done,
    /// <summary>O usuário não conseguiu ou não quis fazer. Segue-se sem a ação.</summary>
    Skipped,
    /// <summary>O usuário quer parar tudo. Nada mais é alterado.</summary>
    Cancel
}

/// <summary>Resposta à pergunta final "a impressão voltou a funcionar?".</summary>
public enum ResolutionAnswer
{
    Worked,
    StillNotWorking,
    /// <summary>O usuário não pôde testar (ou cancelou a pergunta): vale só a verificação objetiva.</summary>
    CannotTest
}

/// <summary>
/// Como o motor conversa com o usuário. Terminal e janela implementam; os testes usam uma versão
/// com respostas programadas. Toda pergunta oferece saída segura: cancelar nunca altera nada.
/// Herda <see cref="IConfirmation"/> para o pipeline de ações aceitar qualquer interação.
/// </summary>
public interface IUserInteraction : IConfirmation
{
    /// <summary>Pergunta "qual?" com uma lista de opções. Devolve null quando o usuário cancela.</summary>
    Task<Choice?> ChooseAsync(string question, IReadOnlyList<Choice> options, CancellationToken cancellationToken);

    /// <summary>Pede algo que só uma pessoa faz (cabo, botão, papel) e espera ela responder.</summary>
    Task<PhysicalActionReply> RequestPhysicalActionAsync(string instruction, CancellationToken cancellationToken);

    /// <summary>Fechamento humano: só a resposta do usuário (ou a verificação objetiva) define o texto final.</summary>
    Task<ResolutionAnswer> AskIfResolvedAsync(string question, CancellationToken cancellationToken);
}
