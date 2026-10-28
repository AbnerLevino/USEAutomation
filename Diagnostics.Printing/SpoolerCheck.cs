using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>
/// Verifica se o serviço de impressão do Windows (Spooler) está em execução. Só lê.
/// Regras:
/// <list type="number">
/// <item>Fonte não consultada, serviço não encontrado ou estado desconhecido → NotEvaluable.</item>
/// <item>Em execução → Healthy (não prova que a fila processa os trabalhos).</item>
/// <item>Qualquer outro estado → Problem, confirmado pelo próprio Windows.</item>
/// </list>
/// </summary>
public sealed class SpoolerCheck(IPrintSystemProvider provider) : ICheck<ServiceInfo>
{
    public const string CheckId = "printing.spooler";
    private const string SourceName = "Serviço Spooler (WMI Win32_Service)";

    public string Id => CheckId;
    public string DisplayName => "Serviço de impressão (Spooler)";

    public async Task<CheckResult<ServiceInfo>> RunAsync(IProgress<DiagnosticProgress> progress, CancellationToken cancellationToken)
    {
        CheckProgress.Say(progress, "Vou conferir se o serviço de impressão do Windows (Spooler) está em execução.");
        var result = await provider.GetSpoolerServiceAsync(cancellationToken);

        DataSourceResult[] sources = [new() { Name = SourceName, Status = result.Status, Detail = result.Detail }];
        string[] limitations =
        [
            "As dependências do Spooler (como o serviço RPC) não foram verificadas.",
            "Serviço em execução não prova que a fila está processando os trabalhos."
        ];

        if (!result.IsConsulted)
        {
            CheckProgress.Say(progress, $"Não consegui consultar o serviço ({CheckProgress.Describe(result.Status)}), então não dá para avaliar.");
            return NotEvaluable("Não foi possível consultar o serviço de impressão; nenhuma conclusão foi tirada.", sources, limitations);
        }

        var service = result.Items.FirstOrDefault();
        if (service is null)
        {
            CheckProgress.Say(progress, "O Windows não listou o serviço Spooler; não dá para avaliar.");
            return NotEvaluable("O Windows não devolveu o serviço Spooler na consulta; nenhuma conclusão foi tirada.", sources, limitations);
        }

        if (service.State == ServiceState.Unknown)
        {
            CheckProgress.Say(progress, "O Windows não informou o estado do Spooler; não dá para avaliar.");
            return NotEvaluable("O estado do serviço Spooler não foi informado; nenhuma conclusão foi tirada.", sources, limitations, service);
        }

        var mode = service.StartMode is null ? "início não informado" : $"início {service.StartMode}";
        Evidence[] evidence =
        [
            new() { Id = "ev-1", Source = SourceName, Description = $"Serviço '{service.Name}': estado {service.State}; {mode}." }
        ];

        if (service.State == ServiceState.Running)
        {
            CheckProgress.Say(progress, "O Spooler está em execução.");
            return new CheckResult<ServiceInfo>
            {
                Verdict = CheckVerdict.Healthy,
                Summary = "O serviço de impressão (Spooler) está em execução.",
                Facts = service, Evidence = evidence, Sources = sources, Limitations = limitations
            };
        }

        var disabled = string.Equals(service.StartMode, "Disabled", StringComparison.OrdinalIgnoreCase);
        var state = Describe(service.State);
        CheckProgress.Say(progress, $"O Spooler não está em execução: {state}.");
        return new CheckResult<ServiceInfo>
        {
            Verdict = CheckVerdict.Problem,
            Summary = $"O serviço de impressão (Spooler) não está em execução: {state}.",
            Facts = service,
            Findings =
            [
                new Finding
                {
                    Id = "f-1",
                    Title = "Serviço de impressão (Spooler) fora de execução",
                    Description = $"O Windows reporta o Spooler como '{state}'" +
                                  (disabled ? " e com início desabilitado." : "."),
                    Severity = Severity.High,
                    Confidence = Confidence.Confirmed,
                    EvidenceIds = ["ev-1"]
                }
            ],
            Evidence = evidence, Sources = sources, Limitations = limitations,
            Recommendations =
            [
                new Recommendation
                {
                    FindingId = "f-1",
                    Text = disabled
                        ? "O serviço 'Spooler de Impressão' está desabilitado. Um administrador precisa habilitá-lo e iniciá-lo em Serviços do Windows."
                        : "Inicie o serviço 'Spooler de Impressão' em Serviços do Windows (pode exigir um administrador)."
                }
            ]
        };
    }

    private static CheckResult<ServiceInfo> NotEvaluable(
        string summary, DataSourceResult[] sources, string[] limitations, ServiceInfo? facts = null) =>
        new()
        {
            Verdict = CheckVerdict.NotEvaluable,
            Summary = summary,
            Facts = facts, Sources = sources, Limitations = limitations,
            Recommendations = [new Recommendation { Text = "Tente executar novamente. Se persistir, verifique o serviço WMI e as permissões de leitura do usuário." }]
        };

    private static string Describe(ServiceState state) => state switch
    {
        ServiceState.Stopped => "parado",
        ServiceState.StartPending => "iniciando",
        ServiceState.StopPending => "parando",
        ServiceState.ContinuePending => "retomando",
        ServiceState.PausePending => "pausando",
        ServiceState.Paused => "pausado",
        _ => "desconhecido"
    };
}
