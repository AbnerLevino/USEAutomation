using System.Text.Json.Serialization;

namespace AutomationPlatform.Contracts;

/// <summary>
/// Algo que aconteceu durante um fluxo guiado, em forma que uma tela consegue desenhar
/// (cartões de impressora, etapas, causa). A narração em frases continua existindo; estes
/// eventos a complementam. Terminal e janela consomem os mesmos eventos.
/// </summary>
public abstract record GuidedEvent;

/// <summary>Uma frase de narração ("Vou olhar o serviço de impressão...").</summary>
public sealed record Narration(string Message) : GuidedEvent;

/// <summary>As impressoras que o Windows listou, para a tela mostrar os cartões.</summary>
public sealed record PrintersFound(IReadOnlyList<PrinterCard> Printers) : GuidedEvent;

/// <param name="IsVirtual">true = fila que não leva a um equipamento (PDF, XPS, fax).</param>
/// <param name="IsDefault">true = impressora padrão; null = o Windows não informou.</param>
/// <param name="StateText">O estado em linguagem de usuário ("pronta", "pausada", "offline"...).</param>
public sealed record PrinterCard(string Name, bool IsVirtual, bool? IsDefault, PrinterCardState State, string StateText);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PrinterCardState
{
    /// <summary>Ainda não se sabe, ou o Windows não informou.</summary>
    Unknown,
    /// <summary>Nada de errado nas verificações feitas (não prova que funciona).</summary>
    Ok,
    Problem
}

/// <summary>Uma impressora ganhou um novo estado depois do cruzamento das verificações.</summary>
public sealed record PrinterStateChanged(string Name, PrinterCardState State, string StateText) : GuidedEvent;

public sealed record StepStarted(string Key, string Label) : GuidedEvent;

public sealed record StepFinished(string Key, string Label, CheckVerdict Verdict, string Summary) : GuidedEvent;

/// <param name="CandidateActionId">Ação que resolveria a causa; null = só orientação ao usuário.</param>
public sealed record CauseFound(
    string Code, string Title, string Description, string Guidance, string? PrinterName, string? CandidateActionId)
    : GuidedEvent;

/// <summary>As verificações não acharam causa. Isso não prova que a impressão funciona.</summary>
public sealed record NoCauseFound(string Message) : GuidedEvent;

/// <summary>Uma correção foi tentada (ou recusada); o registro traz o resultado verificado.</summary>
public sealed record ActionFinished(ActionRecord Record) : GuidedEvent;

/// <summary>O fim do fluxo, com a conclusão e em que ela se baseia.</summary>
public sealed record FlowClosed(FlowClosing Closing) : GuidedEvent;

/// <summary>Como o fluxo terminou do ponto de vista do problema do usuário.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FlowResult
{
    Resolved,
    NotResolved,
    /// <summary>Não há como afirmar nada (nenhuma causa achada, ou nada a verificar).</summary>
    Inconclusive,
    /// <summary>O usuário parou o fluxo. Nada mais foi alterado.</summary>
    Cancelled
}

/// <summary>Em que se baseia a conclusão: na resposta do usuário ou na leitura objetiva do sistema.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClosingBasis
{
    /// <summary>O fluxo terminou sem chegar a perguntar nem verificar.</summary>
    None,
    UserAnswer,
    ObjectiveVerification
}

public sealed record FlowClosing(FlowResult Result, ClosingBasis Basis, string Message);

/// <summary>Executa uma ação do começo ao fim (pré-condição, confirmação, execução, verificação). O pipeline a implementa.</summary>
public interface IActionRunner
{
    Task<ActionRecord> RunAsync(
        IRemediationAction action, ActionTarget target, IConfirmation confirmation, CancellationToken cancellationToken = default);
}

/// <summary>A lista fixa de ações conhecidas, por id. Quem não está na lista não executa.</summary>
public interface IActionCatalog
{
    IRemediationAction? Find(string actionId);
}

/// <summary>
/// O que o usuário diz ao fim do atendimento. Tudo opcional: quem não avalia nem comenta fica com campos nulos.
/// Use <see cref="Create"/> para normalizar o que vem da tela.
/// </summary>
/// <param name="Rating">Nota de 1 a 5; null = não avaliou.</param>
/// <param name="Comment">Comentário livre, sem espaços nas pontas e com no máximo <see cref="MaxCommentLength"/> letras; null = não comentou.</param>
public sealed record ServiceFeedback(int? Rating, string? Comment)
{
    public const int MaxCommentLength = 500;

    public static ServiceFeedback Create(int? rating, string? comment)
    {
        var text = comment?.Trim();
        if (string.IsNullOrEmpty(text)) text = null;
        else if (text.Length > MaxCommentLength) text = text[..MaxCommentLength];
        return new ServiceFeedback(rating is >= 1 and <= 5 ? rating : null, text);
    }

    public bool IsEmpty => Rating is null && Comment is null;
}

/// <summary>
/// Para onde vai o relatório quando o atendimento termina. Hoje é só memória; depois pode ser uma pasta de
/// rede, e-mail ou o USE (por mecanismo oficial), sem mudar quem produz o relatório.
/// </summary>
public interface IReportSink
{
    Task SubmitAsync(DiagnosticReport report, CancellationToken cancellationToken);
}
