using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>
/// Reinicia o serviço de impressão do Windows (N2: exige administrador). Quem de fato mexe no serviço é o executor
/// elevado; esta ação só confere a condição, pede ao executor e depois LÊ o serviço de novo: "resolvido" só sai se o
/// Windows mostra o serviço funcionando. Não apaga nenhum documento da fila.
/// </summary>
public sealed class RestartSpoolerAction(IPrintSystemProvider system, IElevatedExecutor executor) : IRemediationAction
{
    public string Id => PrinterActionIds.RestartSpooler;
    public string DisplayName => "Reiniciar o serviço de impressão";
    public ActionLevel Level => ActionLevel.N2;

    public string DescribeForConfirmation(ActionTarget target) =>
        "O serviço que cuida das impressões neste computador está parado, e por isso nada é impresso. Posso reiniciá-lo? Não vou apagar nenhum documento.";

    public async Task<PreconditionResult> CheckPreconditionAsync(ActionTarget target, CancellationToken cancellationToken)
    {
        var spooler = await ReadAsync(cancellationToken);
        return spooler switch
        {
            { Verdict: CheckVerdict.NotEvaluable } => PreconditionResult.Not("não foi possível ler o estado do serviço de impressão."),
            { Facts.State: ServiceState.Running } => PreconditionResult.Not("o serviço de impressão já está funcionando."),
            _ => PreconditionResult.Ok
        };
    }

    public Task<ExecutionResult> ExecuteAsync(ActionTarget target, CancellationToken cancellationToken) =>
        executor.RunAsync(Id, target, cancellationToken);

    public async Task<VerificationResult> VerifyAsync(ActionTarget target, CancellationToken cancellationToken)
    {
        var spooler = await ReadAsync(cancellationToken);
        if (spooler.Verdict == CheckVerdict.NotEvaluable || spooler.Facts is null)
            return new VerificationResult(VerificationOutcome.Inconclusive, "não foi possível ler o estado do serviço de impressão.");

        return spooler.Facts.State == ServiceState.Running
            ? new VerificationResult(VerificationOutcome.Resolved, "o serviço de impressão voltou a funcionar.")
            : new VerificationResult(VerificationOutcome.NotResolved, "o serviço de impressão continua parado.");
    }

    private Task<CheckResult<ServiceInfo>> ReadAsync(CancellationToken ct) =>
        new SpoolerCheck(system).RunAsync(new NoProgress(), ct);

    private sealed class NoProgress : IProgress<DiagnosticProgress>
    {
        public void Report(DiagnosticProgress value) { }
    }
}
