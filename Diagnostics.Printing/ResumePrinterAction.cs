using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>Resultado de uma ordem dada ao Windows para mexer numa fila.</summary>
/// <param name="AccessDenied">true = o Windows negou por falta de permissão (para uma ação que exigiria administrador).</param>
public sealed record ControlResult(bool Succeeded, string? Detail = null, bool AccessDenied = false);

/// <summary>
/// A única porta pela qual as ações de impressão ALTERAM o Windows. A implementação real (WMI) fica em
/// Platform.Windows; os testes usam uma falsa. Cada método é uma ordem pequena e nomeada, nunca um comando livre.
/// </summary>
public interface IPrinterControl
{
    /// <summary>Retoma uma fila pausada. Não apaga nada.</summary>
    Task<ControlResult> ResumeAsync(string printerName, CancellationToken cancellationToken);
}

/// <summary>
/// Retoma uma fila de impressão pausada (N1: não exige administrador e não apaga nada). A pré-condição e
/// a verificação posterior LEEM o estado de novo pela mesma verificação de estado usada no diagnóstico,
/// de forma independente da execução: "resolvido" só sai se o Windows mostra a fila fora da pausa.
/// </summary>
public sealed class ResumePrinterAction(IPrintSystemProvider system, IPrinterControl control) : IRemediationAction
{
    public string Id => PrinterActionIds.ResumePrinter;
    public string DisplayName => "Retomar a impressora";
    public ActionLevel Level => ActionLevel.N1;

    public string DescribeForConfirmation(ActionTarget target) =>
        $"A impressora '{target.PrinterName}' está pausada, e por isso nada sai dela. Posso retomá-la? Nada será apagado.";

    public async Task<PreconditionResult> CheckPreconditionAsync(ActionTarget target, CancellationToken cancellationToken)
    {
        if (target.PrinterName is null) return PreconditionResult.Not("não sei de qual impressora se trata.");
        return await ReadAsync(target.PrinterName, cancellationToken) switch
        {
            { Found: false, Detail: var why } => PreconditionResult.Not(why),
            { Assessment.Paused: true } => PreconditionResult.Ok,
            _ => PreconditionResult.Not($"a impressora '{target.PrinterName}' já não está pausada.")
        };
    }

    public async Task<ExecutionResult> ExecuteAsync(ActionTarget target, CancellationToken cancellationToken)
    {
        var result = await control.ResumeAsync(target.PrinterName!, cancellationToken);
        return result.Succeeded
            ? new ExecutionResult(ExecutionOutcome.Done, result.Detail)
            : new ExecutionResult(ExecutionOutcome.Failed,
                result.AccessDenied ? "o Windows não permitiu retomar esta impressora sem um administrador." : result.Detail);
    }

    public async Task<VerificationResult> VerifyAsync(ActionTarget target, CancellationToken cancellationToken)
    {
        var read = await ReadAsync(target.PrinterName!, cancellationToken);
        if (!read.Found) return new VerificationResult(VerificationOutcome.Inconclusive, read.Detail);
        return read.Assessment!.Paused
            ? new VerificationResult(VerificationOutcome.NotResolved, $"a impressora '{target.PrinterName}' continua pausada.")
            : new VerificationResult(VerificationOutcome.Resolved, $"a impressora '{target.PrinterName}' não está mais pausada.");
    }

    private async Task<(bool Found, PrinterAssessment? Assessment, string Detail)> ReadAsync(string name, CancellationToken ct)
    {
        var state = await new PrinterStateCheck(system).RunAsync(new NoProgress(), ct);
        if (state.Verdict == CheckVerdict.NotEvaluable || state.Facts is null)
            return (false, null, $"não foi possível ler o estado das impressoras ({state.Summary}).");

        var assessment = state.Facts.FirstOrDefault(a => string.Equals(a.Printer.Name, name, StringComparison.OrdinalIgnoreCase));
        return assessment is null
            ? (false, null, $"não encontrei a impressora '{name}'.")
            : (true, assessment, "");
    }

    private sealed class NoProgress : IProgress<DiagnosticProgress>
    {
        public void Report(DiagnosticProgress value) { }
    }
}
