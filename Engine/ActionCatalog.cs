using AutomationPlatform.Contracts;

namespace AutomationPlatform.Engine;

/// <summary>A lista fixa de ações conhecidas, por id. Quem não está na lista não executa. Lista vazia = só diagnóstico e orientação.</summary>
public sealed class ActionCatalog(IEnumerable<IRemediationAction> actions) : IActionCatalog
{
    private readonly Dictionary<string, IRemediationAction> _byId =
        actions.ToDictionary(a => a.Id, StringComparer.Ordinal);

    public IRemediationAction? Find(string actionId) => _byId.GetValueOrDefault(actionId);
}

/// <summary>
/// Guarda os relatórios dos atendimentos encerrados SÓ em memória (somem quando o programa fecha). É o destino
/// provisório, até existir um lugar definido para o suporte receber os relatórios.
/// </summary>
public sealed class InMemoryReportSink : IReportSink
{
    private readonly List<DiagnosticReport> _reports = [];

    public IReadOnlyList<DiagnosticReport> Reports => _reports;

    public Task SubmitAsync(DiagnosticReport report, CancellationToken cancellationToken)
    {
        lock (_reports) _reports.Add(report);
        return Task.CompletedTask;
    }
}
