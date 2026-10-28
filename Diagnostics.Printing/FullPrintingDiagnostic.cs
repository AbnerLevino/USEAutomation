using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>
/// Diagnóstico completo de impressão (o futuro botão "Diagnóstico completo"): roda todas as verificações
/// sobre TODAS as filas e resume. Diferente do cenário, que explica um sintoma: aqui "nada bloqueante"
/// significa NoProblemDetected (com a ressalva de que isso não prova o funcionamento), e verificações
/// que não puderam ser avaliadas tornam o resultado Inconclusive.
/// </summary>
public sealed class FullPrintingDiagnostic(PrintingSnapshotCollector collector) : IDiagnostic
{
    public const string DiagnosticId = "printing.full";

    public string Id => DiagnosticId;
    public string DisplayName => "Diagnóstico completo de impressão";

    public static FullPrintingDiagnostic Create(IPrintSystemProvider system, IPrintDeviceProvider devices) =>
        new(PrintingSnapshotCollector.Create(system, devices));

    public async Task<DiagnosticOutcome> RunAsync(IProgress<DiagnosticProgress> progress, CancellationToken cancellationToken)
    {
        CheckProgress.Say(progress, "Vou fazer o diagnóstico completo: todas as verificações, em todas as filas.");
        var snapshot = await collector.CollectAsync(progress, cancellationToken);
        var analysis = CauseAnalyzer.Analyze(snapshot, TargetMode.AllQueues);

        var parts = snapshot.Parts().ToList();
        var problems = parts.Count(p => p.Verdict == CheckVerdict.Problem);
        var notEvaluable = parts.Count(p => p.Verdict == CheckVerdict.NotEvaluable);
        var healthy = parts.Count - problems - notEvaluable;
        CheckProgress.Say(progress, $"Resumo das verificações: {healthy} sem problema, {problems} com problema, {notEvaluable} não avaliável(is).");

        var outcome = AnalysisOutcomeBuilder.Build(snapshot, analysis,
            $"Diagnóstico completo de impressão: {healthy} verificação(ões) sem problema, {problems} com problema, {notEvaluable} não avaliável(is).");

        // Sem causa bloqueante: o painel é de saúde, não de sintoma.
        if (analysis.Primary is null)
        {
            var hasWarning = analysis.Causes.Count > 0;
            outcome = outcome with
            {
                OverallStatus = notEvaluable > 0 ? OverallStatus.Inconclusive : OverallStatus.NoProblemDetected,
                // A sugestão de "falar com o suporte" é do cenário (sintoma sem causa); num painel saudável não cabe.
                Recommendations = notEvaluable > 0 ? outcome.Recommendations : outcome.Recommendations.Where(r => r.FindingId is not null).ToList(),
                Summary = outcome.Summary.Replace(
                    "Nenhuma causa foi encontrada nas verificações feitas; isso não prova que a impressão funciona.",
                    hasWarning
                        ? "Nada bloqueia a impressão nas verificações feitas, mas há avisos; isso não prova que a impressão funciona."
                        : "Nada bloqueia a impressão nas verificações feitas; isso não prova que a impressão funciona.")
            };
        }
        return outcome;
    }
}
