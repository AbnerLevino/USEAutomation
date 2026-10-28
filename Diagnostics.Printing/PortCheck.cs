using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

public enum PortKind
{
    /// <summary>Não leva a equipamento: PDF, XPS, arquivo, fax.</summary>
    Virtual,
    Usb,
    /// <summary>Porta TCP/IP configurada com endereço.</summary>
    NetworkTcp,
    /// <summary>Descoberta de serviços da Web (WSD).</summary>
    NetworkWsd,
    /// <summary>Impressora compartilhada por outro computador (\\servidor\fila).</summary>
    SharedConnection,
    /// <summary>LPT ou COM.</summary>
    LegacyLocal,
    Other,
    /// <summary>A fila não informa porta.</summary>
    None
}

/// <param name="Address">Endereço da porta de rede quando houver; null caso contrário.</param>
public sealed record PrinterPortInfo(string PrinterName, string? PortName, PortKind Kind, string? Address);

/// <summary>
/// Descreve a porta de cada fila (só configuração; nenhum teste de rede). Regras:
/// <list type="number">
/// <item>Fonte de filas não consultada, ou sem filas → NotEvaluable.</item>
/// <item>Fila física ou de rede sem porta informada → Problem (confirmado).</item>
/// <item>Caso contrário → Healthy: as portas estão configuradas (isso não prova que respondem).</item>
/// </list>
/// </summary>
public sealed class PortCheck(IPrintSystemProvider provider) : ICheck<IReadOnlyList<PrinterPortInfo>>
{
    public const string CheckId = "printing.port";
    private const string PrintersSource = "Filas de impressão (WMI Win32_Printer)";
    private const string PortsSource = "Portas TCP/IP (WMI Win32_TCPIPPrinterPort)";

    public string Id => CheckId;
    public string DisplayName => "Portas das impressoras (configuração)";

    public async Task<CheckResult<IReadOnlyList<PrinterPortInfo>>> RunAsync(
        IProgress<DiagnosticProgress> progress, CancellationToken cancellationToken)
    {
        CheckProgress.Say(progress, "Vou conferir a porta configurada em cada fila. Só leio a configuração; não testo a rede.");
        var printers = await provider.GetPrinterStatesAsync(cancellationToken);
        var ports = await provider.GetNetworkPortsAsync(cancellationToken);

        DataSourceResult[] sources =
        [
            new() { Name = PrintersSource, Status = printers.Status, Detail = printers.Detail },
            new() { Name = PortsSource, Status = ports.Status, Detail = ports.Detail }
        ];
        var limitations = new List<string>
        {
            "Só a configuração das portas foi lida; não foi testado se o equipamento ou a rede respondem.",
            "Porta configurada não prova que a impressora está acessível."
        };
        if (!ports.IsConsulted)
            limitations.Add($"As portas TCP/IP não puderam ser consultadas ({CheckProgress.Describe(ports.Status)}); endereços de rede podem faltar.");

        if (!printers.IsConsulted || printers.Items.Count == 0)
        {
            var why = printers.IsConsulted ? "não há filas instaladas" : CheckProgress.Describe(printers.Status);
            CheckProgress.Say(progress, $"Não dá para avaliar as portas ({why}).");
            return new CheckResult<IReadOnlyList<PrinterPortInfo>>
            {
                Verdict = CheckVerdict.NotEvaluable,
                Summary = "Não foi possível avaliar as portas das impressoras; nenhuma conclusão foi tirada.",
                Sources = sources, Limitations = limitations,
                Recommendations = [new Recommendation { Text = "Tente executar novamente. Se persistir, verifique o serviço WMI e as permissões de leitura do usuário." }]
            };
        }

        var byName = ports.IsConsulted
            ? ports.Items.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, NetworkPortInfo>(StringComparer.OrdinalIgnoreCase);

        var infos = printers.Items.Select(p => Classify(p, byName)).ToList();
        var evidence = new List<Evidence>();
        var findings = new List<Finding>();
        var recommendations = new List<Recommendation>();

        foreach (var info in infos)
        {
            var evidenceId = $"ev-{evidence.Count + 1}";
            var port = info.PortName is null ? "sem porta informada" : $"porta {info.PortName}";
            var address = info.Address is null ? "" : $", endereço {info.Address}";
            evidence.Add(new Evidence
            {
                Id = evidenceId, Source = PrintersSource,
                Description = $"Fila '{info.PrinterName}': {port} ({Describe(info.Kind)}{address})."
            });

            if (info.Kind != PortKind.None) continue;

            var findingId = $"f-{findings.Count + 1}";
            CheckProgress.Say(progress, $"A fila '{info.PrinterName}' não informa nenhuma porta.");
            findings.Add(new Finding
            {
                Id = findingId,
                Title = $"Fila '{info.PrinterName}' sem porta",
                Description = $"O Windows não informa porta para a fila '{info.PrinterName}'; ela não consegue enviar trabalhos a um equipamento.",
                Severity = Severity.High, Confidence = Confidence.Confirmed, EvidenceIds = [evidenceId]
            });
            recommendations.Add(new Recommendation
            {
                FindingId = findingId,
                Text = $"Abra as propriedades de '{info.PrinterName}', aba Portas, e escolha a porta correta do equipamento."
            });
        }

        CheckProgress.Say(progress, findings.Count == 0
            ? $"As {infos.Count} fila(s) têm porta configurada."
            : $"{findings.Count} fila(s) sem porta configurada.");

        return new CheckResult<IReadOnlyList<PrinterPortInfo>>
        {
            Verdict = findings.Count > 0 ? CheckVerdict.Problem : CheckVerdict.Healthy,
            Summary = findings.Count > 0
                ? $"{findings.Count} de {infos.Count} fila(s) de impressão não têm porta configurada."
                : $"As {infos.Count} fila(s) de impressão têm porta configurada.",
            Facts = infos,
            Findings = findings, Evidence = evidence, Sources = sources,
            Limitations = limitations, Recommendations = recommendations
        };
    }

    internal static PrinterPortInfo Classify(PrinterStateInfo printer, IReadOnlyDictionary<string, NetworkPortInfo> networkPorts)
    {
        var port = printer.PortName?.Trim();
        if (string.IsNullOrEmpty(port)) return new(printer.Name, null, PortKind.None, null);
        if (PrinterClassifier.IsVirtualPort(port)) return new(printer.Name, port, PortKind.Virtual, null);
        if (networkPorts.TryGetValue(port, out var net))
        {
            var address = net.HostAddress is null ? null : net.PortNumber is { } n ? $"{net.HostAddress}:{n}" : net.HostAddress;
            return new(printer.Name, port, PortKind.NetworkTcp, address);
        }
        if (port.StartsWith("WSD", StringComparison.OrdinalIgnoreCase)) return new(printer.Name, port, PortKind.NetworkWsd, null);
        if (port.StartsWith("USB", StringComparison.OrdinalIgnoreCase)) return new(printer.Name, port, PortKind.Usb, null);
        if (port.StartsWith(@"\\", StringComparison.Ordinal)) return new(printer.Name, port, PortKind.SharedConnection, null);
        if (port.StartsWith("LPT", StringComparison.OrdinalIgnoreCase) || port.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            return new(printer.Name, port, PortKind.LegacyLocal, null);
        return new(printer.Name, port, PortKind.Other, null);
    }

    private static string Describe(PortKind kind) => kind switch
    {
        PortKind.Virtual => "virtual",
        PortKind.Usb => "USB",
        PortKind.NetworkTcp => "rede TCP/IP",
        PortKind.NetworkWsd => "rede WSD",
        PortKind.SharedConnection => "compartilhada por outro computador",
        PortKind.LegacyLocal => "porta local antiga",
        PortKind.None => "sem porta",
        _ => "tipo não identificado"
    };
}
