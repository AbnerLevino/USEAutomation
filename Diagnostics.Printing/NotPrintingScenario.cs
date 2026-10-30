using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>
/// Cenário "Impressora não está imprimindo": roda as seis verificações, cruza as evidências e aponta a
/// causa provável com as demais causas e os efeitos dela. SOMENTE LEITURA: sugere, não executa.
/// Sem causa encontrada o resultado é Inconclusive, nunca "está tudo bem".
/// </summary>
public sealed class NotPrintingScenario(PrintingSnapshotCollector collector) : IScenario
{
    public const string DiagnosticId = "printing.not-printing";

    public string Id => DiagnosticId;
    public string DisplayName => "Impressora não está imprimindo";
    public string Symptom => "Impressora não está imprimindo";

    public static NotPrintingScenario Create(IPrintSystemProvider system, IPrintDeviceProvider devices) =>
        new(PrintingSnapshotCollector.Create(system, devices));

    /// <summary>Análise tipada (causas, ações candidatas), usada pelas fases de correção e interação.</summary>
    public async Task<(PrintingSnapshot Snapshot, ScenarioAnalysis Analysis)> AnalyzeAsync(
        IProgress<DiagnosticProgress> progress, CancellationToken cancellationToken, IProgress<GuidedEvent>? events = null)
    {
        CheckProgress.Say(progress, "Vou investigar por que a impressora não imprime: reúno várias verificações e depois cruzo os resultados.");
        var snapshot = await collector.CollectAsync(progress, cancellationToken, events);

        CheckProgress.Say(progress, "Agora vou cruzar o que encontrei para separar a causa dos seus efeitos.");
        var analysis = CauseAnalyzer.Analyze(snapshot, TargetMode.DefaultOrPhysical);

        CheckProgress.Say(progress, analysis.Primary is { } p
            ? $"Causa provável: {p.Title}."
            : "Não encontrei nenhuma causa nas verificações que fiz.");
        return (snapshot, analysis);
    }

    public async Task<DiagnosticOutcome> RunAsync(IProgress<DiagnosticProgress> progress, CancellationToken cancellationToken)
    {
        var (snapshot, analysis) = await AnalyzeAsync(progress, cancellationToken);
        return AnalysisOutcomeBuilder.Build(snapshot, analysis, "Sintoma investigado: impressora não está imprimindo.");
    }
}
