using AutomationPlatform.Contracts;

namespace AutomationPlatform.Engine;

/// <summary>
/// Faz uma verificação aparecer como diagnóstico comum, para poder rodá-la sozinha
/// (ex.: <c>run printing.spooler</c>) com o mesmo Engine, relatório e CLI.
/// </summary>
public sealed class CheckDiagnostic<TFacts>(ICheck<TFacts> check) : IDiagnostic
{
    public string Id => check.Id;
    public string DisplayName => check.DisplayName;

    public async Task<DiagnosticOutcome> RunAsync(IProgress<DiagnosticProgress> progress, CancellationToken cancellationToken)
    {
        var result = await check.RunAsync(progress, cancellationToken);
        return new DiagnosticOutcome
        {
            OverallStatus = ToOverall(result),
            Summary = result.Summary,
            Findings = result.Findings,
            Evidence = result.Evidence,
            Sources = result.Sources,
            Limitations = result.Limitations,
            Recommendations = result.Recommendations
        };
    }

    private static OverallStatus ToOverall(CheckResult<TFacts> result) => result.Verdict switch
    {
        CheckVerdict.Healthy => OverallStatus.NoProblemDetected,
        CheckVerdict.Problem when result.Findings.Any(f => f.Confidence == Confidence.Confirmed) => OverallStatus.ProblemConfirmed,
        CheckVerdict.Problem => OverallStatus.ProblemProbable,
        _ => OverallStatus.Inconclusive
    };
}
