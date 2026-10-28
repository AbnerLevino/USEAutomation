using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>O que o Windows reporta sobre uma fila: offline, pausada ou erro do equipamento.</summary>
/// <param name="EquipmentError">Descrição do erro do equipamento (papel, toner, tampa...); null = nenhum reportado.</param>
public sealed record PrinterAssessment(
    PrinterStateInfo Printer, bool IsVirtual, bool Offline, bool Paused, string? EquipmentError)
{
    public bool HasIssue => Offline || Paused || EquipmentError is not null;
}

/// <summary>
/// Verifica se as filas estão offline, pausadas ou com erro do equipamento. Só lê.
/// Cada condição é o que o PRÓPRIO Windows reporta; o que ele não informa não é presumido.
/// Filas virtuais são avaliadas e rotuladas, pois também podem estar pausadas.
/// </summary>
public sealed class PrinterStateCheck(IPrintSystemProvider provider) : ICheck<IReadOnlyList<PrinterAssessment>>
{
    public const string CheckId = "printing.printer-state";
    private const string SourceName = "Estado das filas (WMI Win32_Printer)";

    // ExtendedPrinterStatus
    private const int ExtendedOffline = 7;
    private const int ExtendedPaused = 8;
    private const int ExtendedError = 9;
    // DetectedErrorState
    private const int ErrorStateOffline = 9;

    public string Id => CheckId;
    public string DisplayName => "Estado das impressoras (offline, pausada, erro)";

    public async Task<CheckResult<IReadOnlyList<PrinterAssessment>>> RunAsync(
        IProgress<DiagnosticProgress> progress, CancellationToken cancellationToken)
    {
        CheckProgress.Say(progress, "Vou perguntar ao Windows o estado de cada fila: se está offline, pausada ou com erro no equipamento.");
        var result = await provider.GetPrinterStatesAsync(cancellationToken);

        DataSourceResult[] sources = [new() { Name = SourceName, Status = result.Status, Detail = result.Detail }];
        string[] limitations =
        [
            "Só é considerado o que o Windows reporta; uma impressora de rede inalcançável pode não aparecer como offline.",
            "Ausência de erro reportado não prova que a impressora funciona."
        ];

        if (!result.IsConsulted)
        {
            CheckProgress.Say(progress, $"Não consegui consultar as filas ({CheckProgress.Describe(result.Status)}), então não dá para avaliar.");
            return NotEvaluable("Não foi possível consultar o estado das filas; nenhuma conclusão foi tirada.", sources, limitations);
        }

        if (result.Items.Count == 0)
        {
            CheckProgress.Say(progress, "Não há filas de impressão instaladas para avaliar.");
            return NotEvaluable("Nenhuma fila de impressão instalada; nada a avaliar.", sources, limitations);
        }

        var assessments = result.Items.Select(Assess).ToList();
        var evidence = new List<Evidence>();
        var findings = new List<Finding>();
        var recommendations = new List<Recommendation>();

        foreach (var a in assessments)
        {
            var evidenceId = $"ev-{evidence.Count + 1}";
            evidence.Add(new Evidence { Id = evidenceId, Source = SourceName, Description = Describe(a) });

            AddFinding(a.Offline, "offline",
                $"A fila '{a.Printer.Name}' está offline segundo o Windows.",
                Severity.High,
                $"Confira se '{a.Printer.Name}' está ligada e conectada. Se estiver, desmarque \"Usar impressora offline\" na fila.");
            AddFinding(a.Paused, "pausada",
                $"A fila '{a.Printer.Name}' está pausada: nada será impresso até ser retomada.",
                Severity.High,
                $"Abra a fila de '{a.Printer.Name}' e escolha retomar a impressão.");
            if (a.EquipmentError is { } error)
                AddFinding(true, "erro-equipamento",
                    $"A impressora '{a.Printer.Name}' reporta: {error}.",
                    IsWarningOnly(a.Printer.DetectedErrorState) ? Severity.Medium : Severity.High,
                    $"Verifique o equipamento '{a.Printer.Name}': {error}.");

            void AddFinding(bool condition, string kind, string description, Severity severity, string recommendation)
            {
                if (!condition) return;
                var findingId = $"f-{findings.Count + 1}";
                CheckProgress.Say(progress, description);
                findings.Add(new Finding
                {
                    Id = findingId,
                    Title = $"Fila '{a.Printer.Name}': {kind}",
                    Description = description,
                    Severity = severity,
                    Confidence = Confidence.Confirmed,
                    EvidenceIds = [evidenceId]
                });
                recommendations.Add(new Recommendation { FindingId = findingId, Text = recommendation });
            }
        }

        var withIssue = assessments.Count(a => a.HasIssue);
        CheckProgress.Say(progress, withIssue == 0
            ? $"Nenhuma das {assessments.Count} fila(s) reporta offline, pausa ou erro."
            : $"{withIssue} de {assessments.Count} fila(s) reportam algum problema.");

        return new CheckResult<IReadOnlyList<PrinterAssessment>>
        {
            Verdict = withIssue > 0 ? CheckVerdict.Problem : CheckVerdict.Healthy,
            Summary = withIssue > 0
                ? $"{withIssue} de {assessments.Count} fila(s) de impressão reportam offline, pausa ou erro do equipamento."
                : $"Nenhuma das {assessments.Count} fila(s) de impressão reporta offline, pausa ou erro.",
            Facts = assessments,
            Findings = findings, Evidence = evidence, Sources = sources,
            Limitations = limitations, Recommendations = recommendations
        };
    }

    internal static PrinterAssessment Assess(PrinterStateInfo p) => new(
        p,
        PrinterClassifier.IsVirtualPort(p.PortName),
        Offline: p.WorkOffline == true || p.ExtendedStatus == ExtendedOffline || p.DetectedErrorState == ErrorStateOffline,
        Paused: p.ExtendedStatus == ExtendedPaused,
        EquipmentError: EquipmentErrorText(p));

    private static string? EquipmentErrorText(PrinterStateInfo p) => p.DetectedErrorState switch
    {
        3 => "pouco papel",
        4 => "sem papel",
        5 => "pouco toner",
        6 => "sem toner",
        7 => "tampa ou porta aberta",
        8 => "papel atolado",
        10 => "o equipamento pede manutenção",
        11 => "bandeja de saída cheia",
        _ => p.ExtendedStatus == ExtendedError ? "o Windows reporta erro (sem detalhe)" : null
    };

    private static bool IsWarningOnly(int? detectedErrorState) => detectedErrorState is 3 or 5;

    private static string Describe(PrinterAssessment a)
    {
        var port = a.Printer.PortName is null ? "porta não informada" : $"porta {a.Printer.PortName}";
        var kind = a.IsVirtual ? "virtual" : "física ou de rede";
        var flags = new List<string>();
        if (a.Offline) flags.Add("offline");
        if (a.Paused) flags.Add("pausada");
        if (a.EquipmentError is { } e) flags.Add(e);
        var state = flags.Count == 0 ? "sem offline, pausa ou erro reportados" : string.Join(", ", flags);
        var standard = a.Printer.IsDefault == true ? "; impressora padrão" : "";
        return $"Fila '{a.Printer.Name}' ({port}; {kind}{standard}): {state}.";
    }

    private static CheckResult<IReadOnlyList<PrinterAssessment>> NotEvaluable(
        string summary, DataSourceResult[] sources, string[] limitations) =>
        new()
        {
            Verdict = CheckVerdict.NotEvaluable,
            Summary = summary,
            Sources = sources, Limitations = limitations,
            Recommendations = [new Recommendation { Text = "Tente executar novamente. Se persistir, verifique o serviço WMI e as permissões de leitura do usuário." }]
        };
}
