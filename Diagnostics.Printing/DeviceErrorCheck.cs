using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>
/// Verifica se o Windows reporta erro (Gerenciador de Dispositivos) em algum dispositivo de impressão.
/// Só lê. É a parte "erro de dispositivo" do diagnóstico <c>printing.device-recognition</c>, em peça
/// reutilizável por cenários. Regras:
/// <list type="number">
/// <item>Fonte não consultada, ou nenhum dispositivo reconhecido → NotEvaluable.</item>
/// <item>Algum dispositivo com código de erro diferente de 0 → Problem (confirmado pelo Windows).</item>
/// <item>Caso contrário → Healthy (código null = o Windows não informou; não conta como erro).</item>
/// </list>
/// </summary>
public sealed class DeviceErrorCheck(IPrintDeviceProvider provider) : ICheck<IReadOnlyList<PrintDeviceInfo>>
{
    public const string CheckId = "printing.device-error";
    private const string SourceName = "Dispositivos Plug and Play (WMI Win32_PnPEntity)";

    public string Id => CheckId;
    public string DisplayName => "Erro em dispositivos de impressão";

    public async Task<CheckResult<IReadOnlyList<PrintDeviceInfo>>> RunAsync(
        IProgress<DiagnosticProgress> progress, CancellationToken cancellationToken)
    {
        CheckProgress.Say(progress, "Vou perguntar ao Windows se algum dispositivo de impressão reporta erro.");
        var devices = await provider.GetPrintDevicesAsync(cancellationToken);

        DataSourceResult[] sources = [new() { Name = SourceName, Status = devices.Status, Detail = devices.Detail }];
        string[] limitations = ["Ausência de erro reportado não prova que o dispositivo funciona."];

        if (!devices.IsConsulted || devices.Items.Count == 0)
        {
            var why = devices.IsConsulted ? "nenhum dispositivo de impressão reconhecido" : CheckProgress.Describe(devices.Status);
            CheckProgress.Say(progress, $"Não dá para avaliar ({why}).");
            return new CheckResult<IReadOnlyList<PrintDeviceInfo>>
            {
                Verdict = CheckVerdict.NotEvaluable,
                Summary = devices.IsConsulted
                    ? "O Windows não reconhece nenhum dispositivo de impressão; nada a avaliar."
                    : "Não foi possível consultar os dispositivos de impressão; nenhuma conclusão foi tirada.",
                Sources = sources, Limitations = limitations,
                Recommendations = [new Recommendation { Text = "Confirme se a impressora está ligada e conectada e execute de novo." }]
            };
        }

        var evidence = new List<Evidence>();
        var findings = new List<Finding>();
        var recommendations = new List<Recommendation>();
        var faulty = new List<PrintDeviceInfo>();

        foreach (var device in devices.Items)
        {
            var evidenceId = $"ev-{evidence.Count + 1}";
            var code = device.ErrorCode is null ? "não informado" : device.ErrorCode.ToString();
            evidence.Add(new Evidence
            {
                Id = evidenceId, Source = SourceName,
                Description = $"Dispositivo '{device.Name}' ({device.DeviceId}): código de erro {code}."
            });

            if (device.ErrorCode is null or 0) continue;

            faulty.Add(device);
            var meaning = DeviceErrorCodes.Describe(device.ErrorCode.Value);
            var findingId = $"f-{findings.Count + 1}";
            CheckProgress.Say(progress, $"'{device.Name}' reporta o código {device.ErrorCode}: {meaning}.");
            findings.Add(new Finding
            {
                Id = findingId,
                Title = $"Dispositivo com erro: {device.Name}",
                Description = $"O Windows reporta o código {device.ErrorCode}: {meaning}.",
                Severity = Severity.High, Confidence = Confidence.Confirmed, EvidenceIds = [evidenceId]
            });
            recommendations.Add(new Recommendation
            {
                FindingId = findingId,
                Text = $"Abra o Gerenciador de Dispositivos, localize '{device.Name}' e verifique o estado do driver (código {device.ErrorCode})."
            });
        }

        return new CheckResult<IReadOnlyList<PrintDeviceInfo>>
        {
            Verdict = faulty.Count > 0 ? CheckVerdict.Problem : CheckVerdict.Healthy,
            Summary = faulty.Count > 0
                ? $"{faulty.Count} de {devices.Items.Count} dispositivo(s) de impressão reportam erro ao Windows."
                : $"Nenhum dos {devices.Items.Count} dispositivo(s) de impressão reporta erro.",
            Facts = faulty,
            Findings = findings, Evidence = evidence, Sources = sources,
            Limitations = limitations, Recommendations = recommendations
        };
    }
}
