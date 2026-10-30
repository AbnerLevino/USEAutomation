using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>Transforma o cruzamento (verificações + análise) no <see cref="DiagnosticOutcome"/> que o Engine entende.</summary>
internal static class AnalysisOutcomeBuilder
{
    public static DiagnosticOutcome Build(PrintingSnapshot snapshot, ScenarioAnalysis analysis, string headline)
    {
        var parts = snapshot.Parts().ToList();

        // Evidências de todas as verificações, com prefixo para os ids não colidirem.
        var evidence = parts.SelectMany(p => p.Evidence.Select(e => e with { Id = $"{p.Key}:{e.Id}" })).ToList();

        // A mesma fonte pode ter sido consultada por mais de uma verificação: lista cada uma uma vez.
        var sources = parts.SelectMany(p => p.Sources)
            .GroupBy(s => s.Name).Select(g => g.First()).ToList();

        var limitations = parts.SelectMany(p => p.Limitations).Concat(analysis.Notes).Distinct().ToList();

        var findings = new List<Finding>();
        var recommendations = new List<Recommendation>();
        var primary = analysis.Primary;

        foreach (var cause in analysis.Causes)
        {
            var id = $"f-{findings.Count + 1}";
            var prefix = cause == primary ? "Causa provável: " : cause.IsBlocking ? "Também encontrado: " : "Atenção: ";
            findings.Add(new Finding
            {
                Id = id,
                Title = prefix + cause.Title,
                Description = cause.Description,
                Severity = cause.Severity,
                Confidence = cause.Confidence,
                EvidenceIds = cause.EvidenceIds
            });
            recommendations.Add(new Recommendation { FindingId = id, Text = cause.Guidance });
        }

        foreach (var consequence in analysis.Consequences)
            findings.Add(new Finding
            {
                Id = $"f-{findings.Count + 1}",
                Title = "Consequência",
                Description = consequence,
                Severity = Severity.Info,
                Confidence = Confidence.Probable
            });

        if (primary is null)
            recommendations.Add(new Recommendation
            {
                Text = "Nenhuma causa foi achada nas verificações feitas. Se o problema continua, imprima uma página de teste e procure o suporte; se a impressora é de rede, o alcance da rede ainda não é verificado."
            });

        var notEvaluable = parts.Count(p => p.Verdict == CheckVerdict.NotEvaluable);
        var summary = new List<string> { headline };
        if (analysis.TargetPrinters.Count > 0)
            summary.Add($"Fila(s) avaliada(s): {string.Join(", ", analysis.TargetPrinters.Select(n => $"'{n}'"))}.");
        if (primary is not null)
        {
            summary.Add($"Causa provável: {primary.Title}.");
            var others = analysis.Causes.Count(c => c != primary && c.IsBlocking);
            if (others > 0) summary.Add($"Há mais {others} causa(s) possível(is).");
        }
        else
            summary.Add("Nenhuma causa foi encontrada nas verificações feitas; isso não prova que a impressão funciona.");
        summary.AddRange(analysis.Consequences);
        if (notEvaluable > 0) summary.Add($"{notEvaluable} verificação(ões) não pôde(ram) ser avaliada(s).");

        return new DiagnosticOutcome
        {
            OverallStatus = primary is null ? OverallStatus.Inconclusive
                : primary.Confidence == Confidence.Confirmed ? OverallStatus.ProblemConfirmed : OverallStatus.ProblemProbable,
            Summary = string.Join(" ", summary),
            Findings = findings, Evidence = evidence, Sources = sources,
            Limitations = limitations, Recommendations = recommendations
        };
    }
}
