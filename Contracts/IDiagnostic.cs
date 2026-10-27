namespace AutomationPlatform.Contracts;

/// <summary>Contrato de uma automação de diagnóstico.</summary>
public interface IDiagnostic
{
    /// <summary>Identificador estável, ex.: "printing.device-recognition".</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>
    /// Executa a investigação. Deve respeitar o <paramref name="cancellationToken"/>.
    /// Pode lançar exceção: o Engine a converte em relatório (não se esconde falha).
    /// </summary>
    Task<DiagnosticOutcome> RunAsync(IProgress<DiagnosticProgress> progress, CancellationToken cancellationToken);
}

/// <summary>
/// O que o diagnóstico conclui. O Engine completa com horários e estado de
/// execução para formar o <see cref="DiagnosticReport"/>.
/// </summary>
public sealed record DiagnosticOutcome
{
    public required OverallStatus OverallStatus { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<Finding> Findings { get; init; } = [];
    public IReadOnlyList<Evidence> Evidence { get; init; } = [];
    public IReadOnlyList<DataSourceResult> Sources { get; init; } = [];
    public IReadOnlyList<string> Limitations { get; init; } = [];
    public IReadOnlyList<Recommendation> Recommendations { get; init; } = [];
}

/// <summary>Uma mensagem sobre o que o sistema está fazendo agora.</summary>
public sealed record DiagnosticProgress(string Message, DateTimeOffset TimestampUtc);
