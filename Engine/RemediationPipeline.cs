using AutomationPlatform.Contracts;

namespace AutomationPlatform.Engine;

/// <summary>
/// Leva UMA ação do começo ao fim: pré-condição → confirmação → execução → verificação posterior.
/// Garantias: nada é executado sem pré-condição e sem confirmação; "Resolvido" só sai quando a
/// verificação independente confirma; toda tentativa, até a que não alterou nada, vai para a auditoria.
/// Exceções da ação viram resultado, nunca escapam; o cancelamento (CancellationToken) escapa.
/// </summary>
public sealed class RemediationPipeline(IAuditLog audit, TimeProvider? clock = null) : IActionRunner
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<ActionRecord> RunAsync(
        IRemediationAction action, ActionTarget target, IConfirmation confirmation, CancellationToken cancellationToken = default)
    {
        var started = _clock.GetUtcNow();
        var confirmed = false;
        string? preconditionDetail = null, executionDetail = null, verificationDetail = null;
        ActionStatus status;
        string summary;

        // 1. Pré-condição: se a causa já não existe, não há o que corrigir nem o que perguntar.
        var precondition = await Guard(() => action.CheckPreconditionAsync(target, cancellationToken),
            e => PreconditionResult.Not($"Não foi possível conferir a condição: {e.Message}"));
        if (!precondition.Met)
        {
            preconditionDetail = precondition.Reason;
            status = ActionStatus.PreconditionFailed;
            summary = $"Nada foi alterado: {precondition.Reason ?? "a condição para agir não existe."}";
            return Finish();
        }

        // 2. Confirmação: sem um "sim" explícito, nada muda.
        var prompt = new ConfirmationPrompt(action.Id, action.DisplayName, action.DescribeForConfirmation(target), action.Level);
        confirmed = await confirmation.ConfirmAsync(prompt, cancellationToken);
        if (!confirmed)
        {
            status = ActionStatus.NotConfirmed;
            summary = "Nada foi alterado: a correção não foi confirmada.";
            return Finish();
        }

        // 3. Execução.
        var execution = await Guard(() => action.ExecuteAsync(target, cancellationToken),
            e => new ExecutionResult(ExecutionOutcome.Failed, e.Message));
        executionDetail = execution.Detail;
        if (execution.Outcome != ExecutionOutcome.Done)
        {
            status = execution.Outcome == ExecutionOutcome.ElevationDenied ? ActionStatus.ElevationDenied : ActionStatus.ExecutionFailed;
            summary = execution.Outcome == ExecutionOutcome.ElevationDenied
                ? "Para continuar é preciso a aprovação de um administrador. Nada foi alterado."
                : $"A correção não foi concluída: {execution.Detail ?? "falha na execução"}.";
            return Finish();
        }

        // 4. Verificação posterior: leitura nova, independente da execução.
        var verification = await Guard(() => action.VerifyAsync(target, cancellationToken),
            e => new VerificationResult(VerificationOutcome.Inconclusive, $"Não foi possível verificar: {e.Message}"));
        verificationDetail = verification.Detail;
        (status, summary) = verification.Outcome switch
        {
            VerificationOutcome.Resolved => (ActionStatus.Resolved, $"Resolvido: {verification.Detail}"),
            VerificationOutcome.NotResolved => (ActionStatus.NotResolved, $"Não resolvido: {verification.Detail}"),
            _ => (ActionStatus.Inconclusive, $"A correção foi executada, mas não foi possível confirmar o resultado: {verification.Detail}")
        };
        return Finish();

        ActionRecord Finish()
        {
            var record = new ActionRecord
            {
                ActionId = action.Id, DisplayName = action.DisplayName, Level = action.Level,
                PrinterName = target.PrinterName, StartedAtUtc = started, CompletedAtUtc = _clock.GetUtcNow(),
                Status = status, Confirmed = confirmed, Summary = summary,
                PreconditionDetail = preconditionDetail, ExecutionDetail = executionDetail, VerificationDetail = verificationDetail
            };
            audit.Append(record);
            return record;
        }
    }

    private static async Task<T> Guard<T>(Func<Task<T>> step, Func<Exception, T> onError)
    {
        try { return await step(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { return onError(e); }
    }
}

/// <summary>Auditoria em memória: serve a testes e ao modo demonstração. O destino em disco é decisão da fase E.</summary>
public sealed class InMemoryAuditLog : IAuditLog
{
    private readonly List<ActionRecord> _records = [];

    public IReadOnlyList<ActionRecord> Records => _records;

    public void Append(ActionRecord record) => _records.Add(record);
}
