using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>
/// Investiga o que o Windows reconhece em impressão e se algum dispositivo reporta erro.
/// Só lê dados. Regras (ver docs/DIAGNOSTIC-REPORT.md):
/// <list type="number">
/// <item>Algum dispositivo com código de erro != 0 → ProblemConfirmed (o próprio Windows reportou).</item>
/// <item>Dispositivo USB sem erro, mas nenhuma fila física/de rede instalada → ProblemProbable (falta o driver?).</item>
/// <item>Dispositivo USB sem erro (e fila física presente ou fila não verificável) → NoProblemDetected.</item>
/// <item>Só itens virtuais / de sistema, nenhum dispositivo, ou fonte indisponível → Inconclusive.</item>
/// </list>
/// Filas e dispositivos virtuais são listados (rotulados), mas não contam como prova de impressora real.
/// </summary>
public sealed class PrinterRecognitionDiagnostic : IDiagnostic
{
    public const string DiagnosticId = "printing.device-recognition";

    private const string PnpSourceName = "Dispositivos Plug and Play (WMI Win32_PnPEntity)";
    private const string PrintersSourceName = "Filas de impressão instaladas (WMI Win32_Printer)";

    private readonly IPrintDeviceProvider _provider;

    public PrinterRecognitionDiagnostic(IPrintDeviceProvider provider) => _provider = provider;

    public string Id => DiagnosticId;
    public string DisplayName => "Reconhecimento de impressora pelo Windows";

    public async Task<DiagnosticOutcome> RunAsync(IProgress<DiagnosticProgress> progress, CancellationToken cancellationToken)
    {
        Say(progress, "Vou começar perguntando ao Windows quais dispositivos de impressão ele reconhece.");
        var devices = await _provider.GetPrintDevicesAsync(cancellationToken);

        Say(progress, "Agora vou conferir quais filas de impressão estão instaladas.");
        var printers = await _provider.GetInstalledPrintersAsync(cancellationToken);

        var sources = new List<DataSourceResult>
        {
            new() { Name = PnpSourceName, Status = devices.Status, Detail = devices.Detail },
            new() { Name = PrintersSourceName, Status = printers.Status, Detail = printers.Detail }
        };
        var limitations = BaseLimitations();

        if (!devices.IsConsulted)
        {
            Say(progress, $"Não consegui consultar os dispositivos ({Describe(devices.Status)}), então não dá para concluir nada.");
            limitations.Add($"A fonte de dispositivos não pôde ser verificada: {Describe(devices.Status)}.");
            return Inconclusive(
                "Não foi possível consultar os dispositivos de impressão; nenhuma conclusão foi tirada.",
                sources, [], limitations,
                "Tente executar novamente. Se o problema persistir, verifique se o serviço WMI está ativo e se o usuário tem permissão de leitura.");
        }

        if (!printers.IsConsulted)
            limitations.Add($"As filas de impressão não puderam ser verificadas: {Describe(printers.Status)}.");

        var evidence = new List<Evidence>();
        var findings = new List<Finding>();
        var recommendations = new List<Recommendation>();

        // --- Filas instaladas: listadas todas, rotuladas como virtuais ou físicas/de rede ---
        var virtualQueues = printers.Items.Where(PrinterClassifier.IsVirtualQueue).ToList();
        var physicalQueues = printers.Items.Where(p => !PrinterClassifier.IsVirtualQueue(p)).ToList();
        if (printers.IsConsulted)
        {
            Say(progress, $"Há {printers.Items.Count} fila(s) instalada(s): {virtualQueues.Count} virtual(is) e {physicalQueues.Count} física(s) ou de rede.");
            foreach (var queue in printers.Items)
            {
                var kind = PrinterClassifier.IsVirtualQueue(queue) ? "virtual" : "física ou de rede";
                var port = queue.PortName is null ? "porta não informada" : $"porta {queue.PortName}";
                evidence.Add(new Evidence
                {
                    Id = $"ev-{evidence.Count + 1}",
                    Source = PrintersSourceName,
                    Description = $"Fila '{queue.Name}' ({port}): {kind}."
                });
            }
        }

        // --- Dispositivos Plug and Play: todos listados, com o código de erro ---
        var usbDevicesWithoutError = new List<PrintDeviceInfo>();
        if (devices.Items.Count > 0)
            Say(progress, $"O Windows reconhece {devices.Items.Count} dispositivo(s) de impressão. Vou analisar o estado de cada um.");

        foreach (var device in devices.Items)
        {
            var evidenceId = $"ev-{evidence.Count + 1}";
            var code = device.ErrorCode is null ? "não informado" : device.ErrorCode.ToString();
            evidence.Add(new Evidence
            {
                Id = evidenceId,
                Source = PnpSourceName,
                Description = $"Dispositivo '{device.Name}' ({device.DeviceId}; {PrinterClassifier.DescribeDevice(device)}): código de erro {code}."
            });

            if (device.ErrorCode is not (null or 0))
            {
                var meaning = DeviceErrorCodes.Describe(device.ErrorCode.Value);
                Say(progress, $"'{device.Name}' reporta o código {device.ErrorCode}: {meaning}.");
                var findingId = $"f-{findings.Count + 1}";
                findings.Add(new Finding
                {
                    Id = findingId,
                    Title = $"Dispositivo com erro: {device.Name}",
                    Description = $"O Windows reporta o código {device.ErrorCode}: {meaning}.",
                    Severity = Severity.High,
                    Confidence = Confidence.Confirmed,
                    EvidenceIds = [evidenceId]
                });
                recommendations.Add(new Recommendation
                {
                    FindingId = findingId,
                    Text = $"Abra o Gerenciador de Dispositivos, localize '{device.Name}' e verifique o estado do driver (código {device.ErrorCode})."
                });
            }
            else if (device.ErrorCode == 0 && PrinterClassifier.IsUsbDevice(device))
            {
                usbDevicesWithoutError.Add(device);
            }
        }

        if (findings.Count > 0)
        {
            return new DiagnosticOutcome
            {
                OverallStatus = OverallStatus.ProblemConfirmed,
                Summary = $"{findings.Count} de {devices.Items.Count} dispositivo(s) de impressão reportam erro ao Windows.",
                Findings = findings, Evidence = evidence, Sources = sources,
                Limitations = limitations, Recommendations = recommendations
            };
        }

        var queueSummary = printers.IsConsulted
            ? $" Filas instaladas: {printers.Items.Count} ({virtualQueues.Count} virtual(is), {physicalQueues.Count} física(s) ou de rede)."
            : "";

        // Equipamento USB reconhecido sem erro, mas nenhuma fila física: indício de driver/instalação faltando.
        if (usbDevicesWithoutError.Count > 0 && printers.IsConsulted && physicalQueues.Count == 0)
        {
            Say(progress, "Há um dispositivo USB de impressão sem erro, mas nenhuma fila física instalada para ele. Isso é um indício, não uma prova.");
            var usbEvidence = evidence.First(e => e.Description.Contains(usbDevicesWithoutError[0].DeviceId));
            return new DiagnosticOutcome
            {
                OverallStatus = OverallStatus.ProblemProbable,
                Summary = "O Windows reconhece um dispositivo USB de impressão sem erro, mas não há fila de impressão física instalada para ele." + queueSummary,
                Findings =
                [
                    new Finding
                    {
                        Id = "f-1",
                        Title = "Dispositivo USB reconhecido sem fila de impressão",
                        Description = "Há dispositivo USB de impressão sem erro, mas nenhuma impressora física instalada. Pode faltar o driver de impressão.",
                        Severity = Severity.Medium,
                        Confidence = Confidence.Probable,
                        EvidenceIds = [usbEvidence.Id]
                    }
                ],
                Evidence = evidence, Sources = sources, Limitations = limitations,
                Recommendations =
                [
                    new Recommendation
                    {
                        FindingId = "f-1",
                        Text = "Verifique se o driver da impressora está instalado e adicione a impressora em Configurações > Impressoras e scanners."
                    }
                ]
            };
        }

        // Equipamento USB reconhecido sem erro (fila física presente ou não verificável).
        if (usbDevicesWithoutError.Count > 0)
        {
            Say(progress, "Nenhum dispositivo USB de impressão reporta erro nas verificações que fiz.");
            limitations.Add("Ausência de erro nas fontes consultadas não prova que a impressão funciona.");
            return new DiagnosticOutcome
            {
                OverallStatus = OverallStatus.NoProblemDetected,
                Summary = $"{usbDevicesWithoutError.Count} dispositivo(s) USB de impressão reconhecido(s), nenhum com erro reportado nas verificações realizadas." + queueSummary,
                Evidence = evidence, Sources = sources, Limitations = limitations
            };
        }

        // Nenhuma impressora física comprovada: só itens virtuais/de sistema, filas de rede ou nada.
        Say(progress, "Não encontrei nenhuma impressora física comprovada, então não dá para concluir sobre o equipamento.");
        limitations.Add("Itens virtuais (PDF, XPS, fax) e filas de sistema não comprovam que existe uma impressora física.");
        limitations.Add("Impressoras de rede e compartilhadas podem funcionar sem aparecer como dispositivo local; a conectividade de rede não é verificada.");
        var networkNote = physicalQueues.Count > 0
            ? " Há fila(s) com porta física ou de rede, mas o equipamento não pôde ser comprovado por este diagnóstico."
            : "";
        return Inconclusive(
            "Nenhuma impressora física foi detectada pelo Windows." + queueSummary + networkNote + " Isso não confirma falha de hardware.",
            sources, evidence, limitations,
            "Confirme se a impressora está ligada e conectada. Se for USB, teste outra porta ou cabo e execute o diagnóstico de novo.");
    }

    private static DiagnosticOutcome Inconclusive(
        string summary, List<DataSourceResult> sources, List<Evidence> evidence, List<string> limitations, string recommendation) =>
        new()
        {
            OverallStatus = OverallStatus.Inconclusive,
            Summary = summary,
            Evidence = evidence,
            Sources = sources,
            Limitations = limitations,
            Recommendations = [new Recommendation { Text = recommendation }]
        };

    private static List<string> BaseLimitations() =>
    [
        "Foram consultados apenas dispositivos Plug and Play de impressão e as filas instaladas; outras fontes (spooler, portas, rede) não foram verificadas."
    ];

    private static string Describe(SourceStatus status) => status switch
    {
        SourceStatus.PermissionDenied => "permissão negada",
        SourceStatus.Unavailable => "fonte indisponível",
        SourceStatus.Failed => "falha na consulta",
        _ => "consultada"
    };

    private static void Say(IProgress<DiagnosticProgress> progress, string message) =>
        progress.Report(new DiagnosticProgress(message, DateTimeOffset.UtcNow));
}
