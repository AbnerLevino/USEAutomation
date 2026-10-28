namespace AutomationPlatform.Contracts;

/// <summary>Conclusão de uma verificação pequena (pergunta: "esta parte está saudável?").</summary>
public enum CheckVerdict
{
    /// <summary>O que foi verificado está bem (não prova que o resto está).</summary>
    Healthy,
    /// <summary>Há evidência de problema nesta parte.</summary>
    Problem,
    /// <summary>Não foi possível avaliar (fonte indisponível, sem permissão, sem dados).</summary>
    NotEvaluable
}

/// <summary>
/// Resultado de uma verificação. <typeparamref name="TFacts"/> são os fatos tipados que um
/// cenário usa para cruzar causas (ex.: "o spooler está parado").
/// </summary>
public sealed record CheckResult<TFacts>
{
    public required CheckVerdict Verdict { get; init; }
    public required string Summary { get; init; }
    public TFacts? Facts { get; init; }
    public IReadOnlyList<Finding> Findings { get; init; } = [];
    public IReadOnlyList<Evidence> Evidence { get; init; } = [];
    public IReadOnlyList<DataSourceResult> Sources { get; init; } = [];
    public IReadOnlyList<string> Limitations { get; init; } = [];
    public IReadOnlyList<Recommendation> Recommendations { get; init; } = [];
}

/// <summary>
/// Uma pergunta pequena e SOMENTE LEITURA sobre o sistema, ex.: "o spooler está rodando?".
/// Verificações são as peças que os cenários combinam.
/// </summary>
public interface ICheck<TFacts>
{
    /// <summary>Identificador estável, ex.: "printing.spooler".</summary>
    string Id { get; }

    string DisplayName { get; }

    Task<CheckResult<TFacts>> RunAsync(IProgress<DiagnosticProgress> progress, CancellationToken cancellationToken);
}
