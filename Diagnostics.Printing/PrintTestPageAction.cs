using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>
/// Imprime a página de teste do Windows (N1: não exige administrador; gasta uma folha). Serve para o usuário
/// confirmar com os próprios olhos que a impressão voltou, sem precisar achar o Painel de Controle.
/// A verificação posterior NUNCA diz "resolvido": o sistema só sabe que a página foi enviada, não que saiu
/// papel. Por isso o resultado é inconclusivo e quem fecha é a resposta do usuário.
/// </summary>
public sealed class PrintTestPageAction(IPrintSystemProvider system, IPrinterControl control) : IRemediationAction
{
    public string Id => PrinterActionIds.PrintTestPage;
    public string DisplayName => "Imprimir página de teste";
    public ActionLevel Level => ActionLevel.N1;

    public string DescribeForConfirmation(ActionTarget target) =>
        $"Quer que eu imprima uma página de teste na impressora '{target.PrinterName}' para conferir se está tudo certo? Vai gastar uma folha.";

    public async Task<PreconditionResult> CheckPreconditionAsync(ActionTarget target, CancellationToken cancellationToken)
    {
        if (target.PrinterName is null) return PreconditionResult.Not("não sei em qual impressora imprimir.");

        var state = await new PrinterStateCheck(system).RunAsync(new NoProgress(), cancellationToken);
        if (state.Verdict == CheckVerdict.NotEvaluable || state.Facts is null)
            return PreconditionResult.Not("não foi possível ler o estado das impressoras.");

        var printer = state.Facts.FirstOrDefault(a => string.Equals(a.Printer.Name, target.PrinterName, StringComparison.OrdinalIgnoreCase));
        return printer switch
        {
            null => PreconditionResult.Not($"não encontrei a impressora '{target.PrinterName}'."),
            { IsVirtual: true } => PreconditionResult.Not($"'{target.PrinterName}' é uma impressora virtual (gera arquivo), não imprime papel."),
            { Paused: true } => PreconditionResult.Not($"a impressora '{target.PrinterName}' está pausada."),
            { Offline: true } => PreconditionResult.Not($"a impressora '{target.PrinterName}' está offline."),
            _ => PreconditionResult.Ok
        };
    }

    public async Task<ExecutionResult> ExecuteAsync(ActionTarget target, CancellationToken cancellationToken)
    {
        var result = await control.PrintTestPageAsync(target.PrinterName!, cancellationToken);
        return result.Succeeded
            ? new ExecutionResult(ExecutionOutcome.Done, result.Detail)
            : new ExecutionResult(ExecutionOutcome.Failed, result.Detail);
    }

    public Task<VerificationResult> VerifyAsync(ActionTarget target, CancellationToken cancellationToken) =>
        Task.FromResult(new VerificationResult(VerificationOutcome.Inconclusive,
            $"a página de teste foi enviada para '{target.PrinterName}'; só você pode confirmar se ela saiu."));

    private sealed class NoProgress : IProgress<DiagnosticProgress>
    {
        public void Report(DiagnosticProgress value) { }
    }
}
