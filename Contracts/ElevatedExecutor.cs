namespace AutomationPlatform.Contracts;

/// <summary>
/// Quem executa uma ação que exige administrador. O app comum nunca recebe poder de administrador: ele pede a um
/// executor separado, que só faz ações de uma lista fixa. Devolve <see cref="ExecutionOutcome.ElevationDenied"/> quando
/// a aprovação do administrador (UAC) foi recusada, e <see cref="ExecutionOutcome.Refused"/> quando o executor se recusou.
/// </summary>
public interface IElevatedExecutor
{
    Task<ExecutionResult> RunAsync(string actionId, ActionTarget target, CancellationToken cancellationToken);
}
