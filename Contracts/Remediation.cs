using System.Text.Json.Serialization;

namespace AutomationPlatform.Contracts;

/// <summary>Quanto uma ação mexe no computador. Define quem pode executá-la e o que o aviso precisa dizer.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionLevel
{
    /// <summary>Usuário comum, reversível.</summary>
    N1,
    /// <summary>Exige administrador, reversível.</summary>
    N2,
    /// <summary>Exige administrador e pode perder dados do usuário (ex.: limpar a fila): aviso explícito.</summary>
    N3
}

/// <summary>Como terminou UMA ação. Só <see cref="Resolved"/> permite dizer "resolvido".</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionStatus
{
    /// <summary>Executou e a leitura posterior, independente, confirmou o efeito.</summary>
    Resolved,
    /// <summary>Executou, mas a leitura posterior mostrou que o problema continua.</summary>
    NotResolved,
    /// <summary>Executou, mas não foi possível verificar o efeito: nada se afirma.</summary>
    Inconclusive,
    /// <summary>O usuário não confirmou. Nada foi alterado.</summary>
    NotConfirmed,
    /// <summary>A condição para agir não existe (mais). Nada foi alterado.</summary>
    PreconditionFailed,
    /// <summary>A execução falhou ou foi recusada pelo executor.</summary>
    ExecutionFailed,
    /// <summary>A aprovação do administrador (UAC) foi recusada ou falhou. Nada foi alterado.</summary>
    ElevationDenied
}

/// <summary>O que foi tentado fazer. <see cref="PrinterName"/> null = vale para o sistema todo.</summary>
public sealed record ActionTarget(string? PrinterName = null);

public sealed record PreconditionResult(bool Met, string? Reason = null)
{
    public static PreconditionResult Ok { get; } = new(true);
    public static PreconditionResult Not(string reason) => new(false, reason);
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExecutionOutcome
{
    Done,
    Failed,
    /// <summary>O executor se recusou (ação fora da lista, parâmetro inválido).</summary>
    Refused,
    ElevationDenied
}

public sealed record ExecutionResult(ExecutionOutcome Outcome, string? Detail = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VerificationOutcome
{
    Resolved,
    NotResolved,
    /// <summary>A leitura não deu para concluir (fonte indisponível, por exemplo).</summary>
    Inconclusive
}

public sealed record VerificationResult(VerificationOutcome Outcome, string Detail);

/// <summary>
/// Uma correção pequena, nomeada e verificável. O pipeline chama, nesta ordem:
/// pré-condição → (confirmação do usuário) → execução → verificação posterior.
/// A verificação PRECISA ler o estado de novo, de forma independente da execução.
/// </summary>
public interface IRemediationAction
{
    /// <summary>Identificador fixo da lista de ações (ex.: "printing.spooler.restart").</summary>
    string Id { get; }

    string DisplayName { get; }

    ActionLevel Level { get; }

    /// <summary>O texto da confirmação: o que será feito e o que pode ser perdido, em linguagem de usuário.</summary>
    string DescribeForConfirmation(ActionTarget target);

    Task<PreconditionResult> CheckPreconditionAsync(ActionTarget target, CancellationToken cancellationToken);

    Task<ExecutionResult> ExecuteAsync(ActionTarget target, CancellationToken cancellationToken);

    Task<VerificationResult> VerifyAsync(ActionTarget target, CancellationToken cancellationToken);
}

/// <summary>O que o usuário vê antes de autorizar uma alteração.</summary>
public sealed record ConfirmationPrompt(string ActionId, string Title, string Text, ActionLevel Level)
{
    /// <summary>true = a ação pode apagar algo do usuário: a interface deve avisar com destaque.</summary>
    public bool WarnsDataLoss => Level == ActionLevel.N3;

    /// <summary>true = pode abrir o aviso do Windows pedindo um administrador.</summary>
    public bool MayNeedAdministrator => Level >= ActionLevel.N2;
}

/// <summary>Como o pipeline pergunta ao usuário. Terminal e janela implementam; testes usam uma versão falsa.</summary>
public interface IConfirmation
{
    Task<bool> ConfirmAsync(ConfirmationPrompt prompt, CancellationToken cancellationToken);
}

/// <summary>Registro de auditoria de UMA ação, mesmo quando nada foi alterado.</summary>
public sealed record ActionRecord
{
    public required string ActionId { get; init; }
    public required string DisplayName { get; init; }
    public required ActionLevel Level { get; init; }
    public string? PrinterName { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required ActionStatus Status { get; init; }

    /// <summary>O usuário autorizou a alteração?</summary>
    public required bool Confirmed { get; init; }

    /// <summary>Texto curto e legível do que aconteceu, para o usuário e para o suporte.</summary>
    public required string Summary { get; init; }

    public string? PreconditionDetail { get; init; }
    public string? ExecutionDetail { get; init; }
    public string? VerificationDetail { get; init; }
}

/// <summary>Destino do registro de auditoria.</summary>
public interface IAuditLog
{
    void Append(ActionRecord record);
}
