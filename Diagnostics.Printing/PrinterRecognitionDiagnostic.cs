using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>
/// Investiga se o Windows reconhece dispositivos de impressão e se algum reporta erro.
/// Só lê dados. Regras (ver docs/DIAGNOSTIC-REPORT.md):
/// <list type="bullet">
/// <item>Dispositivo com código de erro != 0 → ProblemConfirmed (o próprio Windows reportou).</item>
/// <item>Dispositivo reconhecido sem erro, mas nenhuma fila instalada → ProblemProbable (indício indireto).</item>
/// <item>Dispositivo reconhecido sem erro e fila presente (ou fila não verificável) → NoProblemDetected.</item>
/// <item>Nenhum dispositivo, ou fonte de dispositivos indisponível → Inconclusive.</item>
/// </list>
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
                sources, limitations,
                "Tente executar novamente. Se o problema persistir, verifique se o serviço WMI está ativo e se o usuário tem permissão de leitura.");
        }

        if (!printers.IsConsulted)
            limitations.Add($"As filas de impressão não puderam ser verificadas: {Describe(printers.Status)}.");

        if (devices.Items.Count == 0)
        {
            Say(progress, "O Windows não listou nenhum dispositivo de impressão. Isso por si só não prova defeito.");
            limitations.Add("Impressoras de rede, compartilhadas e virtuais podem funcionar sem aparecer como dispositivo local.");
            var queues = printers.IsConsulted ? $" Há {printers.Items.Count} fila(s) de impressão instalada(s)." : "";
            return Inconclusive(
                "O Windows não reconheceu nenhum dispositivo de impressão local." + queues + " Isso não confirma falha de hardware.",
                sources, limitations,
                "Confirme se a impressora está ligada e conectada. Se for USB, teste outra porta ou cabo e execute o diagnóstico de novo.");
        }

        Say(progress, $"Encontrei {devices.Items.Count} dispositivo(s) de impressão. Vou analisar o estado de cada um.");

        var evidence = new List<Evidence>();
        var findings = new List<Finding>();
        var recommendations = new List<Recommendation>();

        foreach (var device in devices.Items)
        {
            var evidenceId = $"ev-{evidence.Count + 1}";
            var code = device.ErrorCode is null ? "não informado" : device.ErrorCode.ToString();
            evidence.Add(new Evidence
            {
                Id = evidenceId,
                Source = PnpSourceName,
                Description = $"Dispositivo '{device.Name}' ({device.DeviceId}): código de erro {code}."
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

        if (printers.IsConsulted && printers.Items.Count == 0)
        {
            Say(progress, "O dispositivo está sem erro, mas não há nenhuma fila de impressão instalada. Isso é um indício, não uma prova.");
            return new DiagnosticOutcome
            {
                OverallStatus = OverallStatus.ProblemProbable,
                Summary = "O Windows reconhece o dispositivo sem erro, mas não há fila de impressão instalada para ele.",
                Findings =
                [
                    new Finding
                    {
                        Id = "f-1",
                        Title = "Dispositivo reconhecido sem fila de impressão",
                        Description = "Há dispositivo de impressão sem erro, mas nenhuma impressora instalada. Pode faltar o driver de impressão.",
                        Severity = Severity.Medium,
                        Confidence = Confidence.Probable,
                        EvidenceIds = [evidence[0].Id]
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

        Say(progress, "Nenhum dispositivo reporta erro nas verificações que fiz.");
        limitations.Add("Ausência de erro nas fontes consultadas não prova que a impressão funciona.");
        return new DiagnosticOutcome
        {
            OverallStatus = OverallStatus.NoProblemDetected,
            Summary = $"{devices.Items.Count} dispositivo(s) de impressão reconhecido(s), nenhum com erro reportado nas verificações realizadas.",
            Evidence = evidence, Sources = sources, Limitations = limitations
        };
    }

    private static DiagnosticOutcome Inconclusive(string summary, List<DataSourceResult> sources, List<string> limitations, string recommendation) =>
        new()
        {
            OverallStatus = OverallStatus.Inconclusive,
            Summary = summary,
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
