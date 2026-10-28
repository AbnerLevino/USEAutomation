using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <param name="DefaultName">Nome da impressora padrão; null = nenhuma marcada.</param>
/// <param name="DefaultIsVirtual">Se a padrão é virtual (PDF, XPS...); null = não há padrão.</param>
/// <param name="WindowsManagesDefault">Se o Windows troca o padrão sozinho; null = não foi possível saber.</param>
/// <param name="PhysicalQueueCount">Filas físicas ou de rede instaladas.</param>
public sealed record DefaultPrinterFacts(string? DefaultName, bool? DefaultIsVirtual, bool? WindowsManagesDefault, int PhysicalQueueCount);

/// <summary>
/// Verifica a impressora padrão. Só lê. Regras:
/// <list type="number">
/// <item>Sem filas, ou nenhuma fila informa se é padrão → NotEvaluable.</item>
/// <item>Nenhuma fila marcada como padrão → Problem (confirmado).</item>
/// <item>Padrão virtual existindo fila física → Problem (provável): a impressão pode estar indo para o PDF.</item>
/// <item>Caso contrário → Healthy.</item>
/// </list>
/// </summary>
public sealed class DefaultPrinterCheck(IPrintSystemProvider provider) : ICheck<DefaultPrinterFacts>
{
    public const string CheckId = "printing.default-printer";
    private const string PrintersSource = "Filas de impressão (WMI Win32_Printer)";
    private const string ModeSource = "Modo da impressora padrão (registro do usuário)";

    public string Id => CheckId;
    public string DisplayName => "Impressora padrão";

    public async Task<CheckResult<DefaultPrinterFacts>> RunAsync(IProgress<DiagnosticProgress> progress, CancellationToken cancellationToken)
    {
        CheckProgress.Say(progress, "Vou ver qual é a impressora padrão e se o Windows a troca sozinho.");
        var printers = await provider.GetPrinterStatesAsync(cancellationToken);
        var mode = await provider.GetDefaultPrinterModeAsync(cancellationToken);

        DataSourceResult[] sources =
        [
            new() { Name = PrintersSource, Status = printers.Status, Detail = printers.Detail },
            new() { Name = ModeSource, Status = mode.Status, Detail = mode.Detail }
        ];
        var limitations = new List<string> { "Ser a impressora padrão não prova que ela está funcionando." };

        bool? windowsManages = mode.IsConsulted && mode.Items.Count > 0 ? mode.Items[0].WindowsManagesDefault : null;
        if (windowsManages is null)
            limitations.Add("Não foi possível saber se o Windows troca a impressora padrão sozinho.");
        else if (windowsManages == true)
            limitations.Add("O Windows gerencia a impressora padrão e pode trocá-la sozinho para a última usada.");

        if (!printers.IsConsulted)
        {
            CheckProgress.Say(progress, $"Não consegui consultar as filas ({CheckProgress.Describe(printers.Status)}), então não dá para avaliar.");
            return NotEvaluable("Não foi possível consultar as filas de impressão; nenhuma conclusão foi tirada.", sources, limitations);
        }

        if (printers.Items.Count == 0 || printers.Items.All(p => p.IsDefault is null))
        {
            CheckProgress.Say(progress, "O Windows não informou qual fila é a padrão, então não dá para avaliar.");
            return NotEvaluable("O Windows não informou qual fila é a padrão; nenhuma conclusão foi tirada.", sources, limitations);
        }

        var physical = printers.Items.Count(p => !PrinterClassifier.IsVirtualPort(p.PortName));
        var standard = printers.Items.FirstOrDefault(p => p.IsDefault == true);
        var facts = new DefaultPrinterFacts(
            standard?.Name,
            standard is null ? null : PrinterClassifier.IsVirtualPort(standard.PortName),
            windowsManages, physical);

        var evidence = new List<Evidence>
        {
            new()
            {
                Id = "ev-1", Source = PrintersSource,
                Description = standard is null
                    ? $"Nenhuma das {printers.Items.Count} fila(s) está marcada como padrão."
                    : $"Impressora padrão: '{standard.Name}' (porta {standard.PortName ?? "não informada"}; {(facts.DefaultIsVirtual == true ? "virtual" : "física ou de rede")})."
            }
        };
        if (windowsManages is { } managed)
            evidence.Add(new Evidence
            {
                Id = "ev-2", Source = ModeSource,
                Description = managed ? "O Windows gerencia a impressora padrão." : "A impressora padrão foi fixada pelo usuário."
            });

        if (standard is null)
        {
            CheckProgress.Say(progress, "Nenhuma impressora está definida como padrão.");
            return Problem(facts, "Nenhuma impressora está definida como padrão.",
                Confidence.Confirmed, Severity.Medium,
                "O Windows não tem nenhuma fila marcada como padrão.",
                "Defina a impressora desejada como padrão em Configurações > Impressoras e scanners.",
                evidence, sources, limitations);
        }

        if (facts.DefaultIsVirtual == true && physical > 0)
        {
            CheckProgress.Say(progress, $"A padrão é virtual ('{standard.Name}') e existe impressora física instalada. Isso é um indício, não uma prova.");
            return Problem(facts, $"A impressora padrão é virtual ('{standard.Name}'), mas há impressora física instalada.",
                Confidence.Probable, Severity.Medium,
                $"A padrão '{standard.Name}' não leva a um equipamento; documentos enviados sem escolher a impressora não sairão no papel.",
                "Defina a impressora física como padrão em Configurações > Impressoras e scanners.",
                evidence, sources, limitations);
        }

        CheckProgress.Say(progress, $"A impressora padrão é '{standard.Name}'.");
        return new CheckResult<DefaultPrinterFacts>
        {
            Verdict = CheckVerdict.Healthy,
            Summary = $"A impressora padrão é '{standard.Name}'.",
            Facts = facts, Evidence = evidence, Sources = sources, Limitations = limitations
        };
    }

    private static CheckResult<DefaultPrinterFacts> Problem(
        DefaultPrinterFacts facts, string summary, Confidence confidence, Severity severity,
        string description, string recommendation,
        List<Evidence> evidence, DataSourceResult[] sources, List<string> limitations) =>
        new()
        {
            Verdict = CheckVerdict.Problem,
            Summary = summary,
            Facts = facts,
            Findings =
            [
                new Finding
                {
                    Id = "f-1", Title = "Impressora padrão", Description = description,
                    Severity = severity, Confidence = confidence, EvidenceIds = ["ev-1"]
                }
            ],
            Evidence = evidence, Sources = sources, Limitations = limitations,
            Recommendations = [new Recommendation { FindingId = "f-1", Text = recommendation }]
        };

    private static CheckResult<DefaultPrinterFacts> NotEvaluable(string summary, DataSourceResult[] sources, List<string> limitations) =>
        new()
        {
            Verdict = CheckVerdict.NotEvaluable,
            Summary = summary,
            Sources = sources, Limitations = limitations,
            Recommendations = [new Recommendation { Text = "Tente executar novamente. Se persistir, verifique o serviço WMI e as permissões de leitura do usuário." }]
        };
}
