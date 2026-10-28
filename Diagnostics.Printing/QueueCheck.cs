using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>Resumo dos trabalhos de uma fila. Nunca contém nomes de documentos.</summary>
/// <param name="StuckJobs">Trabalhos que o próprio Windows marca como erro, em exclusão ou bloqueados na fila do dispositivo.</param>
public sealed record PrinterQueueSummary(string PrinterName, int TotalJobs, int StuckJobs);

/// <summary>
/// Verifica se há trabalhos presos nas filas. Só lê. Regras:
/// <list type="number">
/// <item>Fonte não consultada → NotEvaluable.</item>
/// <item>Algum trabalho em erro, em exclusão ou bloqueado → Problem (confirmado pelo Windows).</item>
/// <item>Sem trabalhos presos → Healthy. Trabalho parado porque a fila está pausada ou offline NÃO é "preso":
/// o Windows nem o marca (StatusMask 0); a causa é vista em <see cref="PrinterStateCheck"/> e cruzada pelo cenário.</item>
/// </list>
/// </summary>
public sealed class QueueCheck(IPrintSystemProvider provider) : ICheck<IReadOnlyList<PrinterQueueSummary>>
{
    public const string CheckId = "printing.queue";
    private const string SourceName = "Trabalhos de impressão (WMI Win32_PrintJob)";

    private const PrintJobStatus StuckMask = PrintJobStatus.Error | PrintJobStatus.Deleting | PrintJobStatus.BlockedDeviceQueue;

    public string Id => CheckId;
    public string DisplayName => "Fila de impressão (trabalhos presos)";

    public async Task<CheckResult<IReadOnlyList<PrinterQueueSummary>>> RunAsync(
        IProgress<DiagnosticProgress> progress, CancellationToken cancellationToken)
    {
        CheckProgress.Say(progress, "Vou conferir se há trabalhos presos nas filas de impressão. Só olho o estado, nunca o nome dos documentos.");
        var result = await provider.GetPrintJobsAsync(cancellationToken);

        DataSourceResult[] sources = [new() { Name = SourceName, Status = result.Status, Detail = result.Detail }];
        string[] limitations =
        [
            "Só são considerados presos os trabalhos que o Windows marca como erro, em exclusão ou bloqueados; não há limite de tempo.",
            "Quando a FILA está pausada ou offline, o Windows não marca isso nos trabalhos (observado no Windows real); essa causa só aparece na verificação do estado das filas.",
            "Fila vazia ou sem trabalhos presos não prova que a impressão funciona."
        ];

        if (!result.IsConsulted)
        {
            CheckProgress.Say(progress, $"Não consegui consultar as filas ({CheckProgress.Describe(result.Status)}), então não dá para avaliar.");
            return new CheckResult<IReadOnlyList<PrinterQueueSummary>>
            {
                Verdict = CheckVerdict.NotEvaluable,
                Summary = "Não foi possível consultar os trabalhos de impressão; nenhuma conclusão foi tirada.",
                Sources = sources, Limitations = limitations,
                Recommendations = [new Recommendation { Text = "Tente executar novamente. Se persistir, verifique o serviço WMI e as permissões de leitura do usuário." }]
            };
        }

        var summaries = result.Items
            .GroupBy(j => j.PrinterName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new PrinterQueueSummary(
                g.Key,
                g.Count(),
                g.Count(j => j.Status is { } s && (s & StuckMask) != 0)))
            .ToList();

        var evidence = new List<Evidence>();
        var findings = new List<Finding>();
        var recommendations = new List<Recommendation>();

        foreach (var queue in summaries)
        {
            var evidenceId = $"ev-{evidence.Count + 1}";
            evidence.Add(new Evidence
            {
                Id = evidenceId,
                Source = SourceName,
                Description = $"Fila '{queue.PrinterName}': {queue.TotalJobs} trabalho(s); {queue.StuckJobs} marcado(s) pelo Windows como preso(s)."
            });

            if (queue.StuckJobs == 0) continue;

            var findingId = $"f-{findings.Count + 1}";
            CheckProgress.Say(progress, $"A fila '{queue.PrinterName}' tem {queue.StuckJobs} trabalho(s) preso(s).");
            findings.Add(new Finding
            {
                Id = findingId,
                Title = $"Trabalhos presos na fila '{queue.PrinterName}'",
                Description = $"O Windows marca {queue.StuckJobs} de {queue.TotalJobs} trabalho(s) como em erro, em exclusão ou bloqueado(s).",
                Severity = Severity.High,
                Confidence = Confidence.Confirmed,
                EvidenceIds = [evidenceId]
            });
            recommendations.Add(new Recommendation
            {
                FindingId = findingId,
                Text = $"Abra a fila de '{queue.PrinterName}' e cancele os trabalhos com erro. Se não saírem, o serviço de impressão pode precisar ser reiniciado."
            });
        }

        var total = summaries.Sum(s => s.TotalJobs);
        var stuck = summaries.Sum(s => s.StuckJobs);
        CheckProgress.Say(progress, total == 0 ? "Nenhuma fila tem trabalhos." : $"Há {total} trabalho(s) nas filas, {stuck} preso(s).");

        return new CheckResult<IReadOnlyList<PrinterQueueSummary>>
        {
            Verdict = stuck > 0 ? CheckVerdict.Problem : CheckVerdict.Healthy,
            Summary = stuck > 0
                ? $"{stuck} trabalho(s) preso(s) encontrado(s) nas filas de impressão."
                : total == 0 ? "Nenhum trabalho nas filas de impressão." : $"{total} trabalho(s) nas filas, nenhum preso.",
            Facts = summaries,
            Findings = findings, Evidence = evidence, Sources = sources,
            Limitations = limitations, Recommendations = recommendations
        };
    }
}
