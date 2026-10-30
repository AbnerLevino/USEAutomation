namespace AutomationPlatform.Contracts;

/// <summary>Versão do formato JSON. Muda quando o formato muda de forma incompatível.</summary>
public static class ReportSchema
{
    // 1.1 acrescentou "actions"; 1.2 acrescentou "closing". Relatórios anteriores continuam válidos: o campo novo vem vazio.
    public const string CurrentVersion = "1.2";
}

/// <summary>Resultado completo de uma execução. É o que sai em JSON.</summary>
public sealed record DiagnosticReport
{
    public string SchemaVersion { get; init; } = ReportSchema.CurrentVersion;
    public required string DiagnosticId { get; init; }
    public required ExecutionStatus ExecutionStatus { get; init; }
    public required OverallStatus OverallStatus { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<Finding> Findings { get; init; } = [];
    public IReadOnlyList<Evidence> Evidence { get; init; } = [];
    public IReadOnlyList<DataSourceResult> Sources { get; init; } = [];
    public IReadOnlyList<string> Limitations { get; init; } = [];
    public IReadOnlyList<Recommendation> Recommendations { get; init; } = [];

    /// <summary>Correções tentadas (confirmadas ou não) a partir deste diagnóstico. Vazio = nenhuma tentativa.</summary>
    public IReadOnlyList<ActionRecord> Actions { get; init; } = [];

    /// <summary>Como o fluxo guiado terminou e em que se baseia (resposta do usuário ou leitura objetiva). Null = não houve fluxo guiado.</summary>
    public FlowClosing? Closing { get; init; }

    /// <summary>Descrição técnica do erro quando a execução não foi <see cref="ExecutionStatus.Completed"/>.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>Um problema ou condição encontrada.</summary>
public sealed record Finding
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required Severity Severity { get; init; }
    public required Confidence Confidence { get; init; }

    /// <summary>Ids das <see cref="Evidence"/> que sustentam este achado.</summary>
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
}

/// <summary>Um fato observado, com a fonte de onde veio.</summary>
public sealed record Evidence
{
    public required string Id { get; init; }
    public required string Source { get; init; }
    public required string Description { get; init; }
}

/// <summary>Uma fonte de dados e o que aconteceu ao consultá-la.</summary>
public sealed record DataSourceResult
{
    public required string Name { get; init; }
    public required SourceStatus Status { get; init; }
    public string? Detail { get; init; }
}

/// <summary>Próxima ação sugerida. Na V1 é só texto: nada é executado.</summary>
public sealed record Recommendation
{
    public required string Text { get; init; }

    /// <summary>Achado ao qual a recomendação se refere, se houver.</summary>
    public string? FindingId { get; init; }
}
