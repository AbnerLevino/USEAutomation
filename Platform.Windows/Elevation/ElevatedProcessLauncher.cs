using System.ComponentModel;
using System.Diagnostics;
using AutomationPlatform.Contracts;

namespace AutomationPlatform.Platform.Windows.Elevation;

/// <summary>Sinal de que o usuário recusou (ou cancelou) o aviso do Windows pedindo a aprovação de um administrador.</summary>
public sealed class ElevationCancelledException : Exception
{
    public ElevationCancelledException() : base("A aprovação do administrador foi recusada.") { }
}

/// <summary>Inicia um programa pedindo elevação e espera ele terminar. Uma porta pequena, para testar sem o aviso do Windows.</summary>
public interface IElevatedProcessRunner
{
    /// <exception cref="ElevationCancelledException">O aviso do Windows foi recusado.</exception>
    /// <exception cref="TimeoutException">O programa não terminou a tempo.</exception>
    Task<int> RunAsync(string executablePath, string arguments, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// Inicia pelo mecanismo padrão do Windows (verbo "runas"): é o Windows quem mostra o aviso e recebe a senha do
/// administrador. Este programa nunca vê nem guarda essa senha.
/// </summary>
public sealed class WindowsElevatedProcessRunner : IElevatedProcessRunner
{
    private const int ErrorCancelled = 1223;   // ERROR_CANCELLED: o usuário recusou o aviso

    public async Task<int> RunAsync(string executablePath, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(executablePath, arguments)
        {
            UseShellExecute = true,   // necessário para o verbo "runas"
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? ""
        };

        Process? process;
        try { process = Process.Start(info); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled) { throw new ElevationCancelledException(); }

        if (process is null) throw new InvalidOperationException("O Windows não iniciou o executor.");
        using (process)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try { await process.WaitForExitAsync(timeoutSource.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new TimeoutException(); }
            return process.ExitCode;
        }
    }
}

/// <summary>Opções do lançador. <paramref name="AllowUnprotectedFolder"/> é só para desenvolvimento e vem com aviso.</paramref></summary>
public sealed record ElevatedLaunchOptions(bool AllowUnprotectedFolder = false, TimeSpan? Timeout = null)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Pede a ação a um executor elevado SEPARADO, depois de o usuário já ter confirmado na tela. Antes de lançar:
/// só aceita ação da lista fixa, confere que o executor existe e que está num lugar que usuário comum não consegue
/// alterar (senão a aprovação entregaria poder de administrador a quem trocasse o arquivo). Cada pedido gera um
/// aviso novo do Windows; recusar o aviso vira <see cref="ExecutionOutcome.ElevationDenied"/> e nada é alterado.
/// </summary>
public sealed class ElevatedProcessLauncher(
    string executablePath, IElevatedProcessRunner runner, IAclReader acl, ElevatedLaunchOptions? options = null) : IElevatedExecutor
{
    public const string ExecutableName = "AutomationPlatform.Host.Elevated.exe";

    private readonly ElevatedLaunchOptions _options = options ?? new ElevatedLaunchOptions();

    /// <summary>O executor mora ao lado do programa principal.</summary>
    public static string DefaultExecutablePath() => Path.Combine(AppContext.BaseDirectory, ExecutableName);

    public async Task<ExecutionResult> RunAsync(string actionId, ActionTarget target, CancellationToken cancellationToken)
    {
        // 1. Só a lista fixa, com parâmetros que cabem na linha de comando sem ambiguidade.
        if (!ElevatedProtocol.AllowedActions.Contains(actionId, StringComparer.Ordinal))
            return new ExecutionResult(ExecutionOutcome.Refused, "essa ação não está na lista de ações permitidas.");
        if (ElevatedProtocol.BuildArguments(actionId, target.PrinterName) is not { } arguments)
            return new ExecutionResult(ExecutionOutcome.Refused, "o pedido tem valores que não são aceitos.");

        // 2. O executor existe e está num lugar protegido (ou o modo de desenvolvimento foi pedido de propósito).
        if (!File.Exists(executablePath))
            return new ExecutionResult(ExecutionOutcome.Failed, "não encontrei o executor ao lado do programa.");

        string? warning = null;
        var (trusted, reason) = ExecutableTrust.Check(executablePath, acl);
        if (!trusted)
        {
            if (!_options.AllowUnprotectedFolder)
                return new ExecutionResult(ExecutionOutcome.Refused,
                    $"o executor está num lugar que um usuário comum consegue alterar, então não vou pedir poder de administrador para ele. {reason}");
            warning = "AVISO: executor em pasta sem proteção (modo de desenvolvimento).";
        }

        // 3. Lança. O aviso do Windows aparece aqui; recusar não altera nada.
        try
        {
            var exitCode = await runner.RunAsync(executablePath, arguments, _options.Timeout ?? ElevatedLaunchOptions.DefaultTimeout, cancellationToken);
            var result = ElevatedProtocol.ToResult(exitCode);
            return warning is null ? result : result with { Detail = result.Detail is null ? warning : $"{result.Detail} {warning}" };
        }
        catch (ElevationCancelledException)
        {
            return new ExecutionResult(ExecutionOutcome.ElevationDenied, "o administrador não aprovou o aviso do Windows.");
        }
        catch (TimeoutException)
        {
            return new ExecutionResult(ExecutionOutcome.Failed, "o executor demorou demais e foi abandonado; confira o resultado.");
        }
        catch (Win32Exception ex)
        {
            return new ExecutionResult(ExecutionOutcome.Failed, $"o Windows não conseguiu iniciar o executor: {ex.Message}");
        }
    }
}
