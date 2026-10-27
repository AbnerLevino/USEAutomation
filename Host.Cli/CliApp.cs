using AutomationPlatform.Contracts;
using AutomationPlatform.Engine;

namespace AutomationPlatform.Host.Cli;

/// <summary>
/// Lógica do terminal, separada do Main para poder ser testada.
/// stdout recebe só o resultado (relatório); stderr recebe a narração e as mensagens de uso.
/// Com --json, o stdout contém apenas o JSON e a narração é suprimida.
/// </summary>
public static class CliApp
{
    /// <summary>Execução terminou sem problema detectado.</summary>
    public const int ExitNoProblem = 0;
    /// <summary>Execução terminou e encontrou problema (confirmado ou provável).</summary>
    public const int ExitProblemFound = 1;
    /// <summary>Execução terminou, mas o resultado é inconclusivo.</summary>
    public const int ExitInconclusive = 2;
    /// <summary>A execução falhou (erro, permissão, tempo limite, cancelamento ou id inexistente).</summary>
    public const int ExitExecutionFailed = 3;
    /// <summary>Comando ou argumentos inválidos.</summary>
    public const int ExitUsageError = 64;

    public static async Task<int> RunAsync(
        string[] args, DiagnosticRegistry registry, TextWriter stdout, TextWriter stderr,
        CancellationToken cancellationToken = default)
    {
        var json = args.Contains("--json", StringComparer.OrdinalIgnoreCase);
        var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

        if (positional is ["list"])
        {
            foreach (var d in registry.All.OrderBy(d => d.Id))
                stdout.WriteLine($"{d.Id}  -  {d.DisplayName}");
            return ExitNoProblem;
        }

        if (positional is not ["run", var id])
        {
            stderr.WriteLine(Usage);
            return ExitUsageError;
        }

        var progress = new SyncProgress(json ? null : stderr);
        var engine = new DiagnosticEngine(registry);
        var report = await engine.RunAsync(id, progress, cancellationToken: cancellationToken);

        if (json)
            stdout.WriteLine(ReportSerializer.Serialize(report));
        else
            ReportPrinter.Print(report, stdout);

        return ExitCodeFor(report);
    }

    public static int ExitCodeFor(DiagnosticReport report)
    {
        if (report.ExecutionStatus != ExecutionStatus.Completed)
            return ExitExecutionFailed;

        return report.OverallStatus switch
        {
            OverallStatus.NoProblemDetected => ExitNoProblem,
            OverallStatus.ProblemConfirmed or OverallStatus.ProblemProbable => ExitProblemFound,
            OverallStatus.Inconclusive => ExitInconclusive,
            _ => ExitExecutionFailed
        };
    }

    private const string Usage =
        """
        Uso:
          AutomationPlatform.Host.Cli list
          AutomationPlatform.Host.Cli run <id> [--json]

        Códigos de saída:
          0  rodou e não detectou problema
          1  rodou e encontrou problema (confirmado ou provável)
          2  rodou, mas o resultado é inconclusivo
          3  a execução falhou (erro, permissão, tempo limite, cancelamento, id inexistente)
          64 comando inválido
        """;

    /// <summary>
    /// Escreve a narração na hora, na ordem em que chega. (Progress&lt;T&gt; entrega em outra
    /// thread e poderia embaralhar ou perder mensagens ao fim do programa.)
    /// </summary>
    private sealed class SyncProgress(TextWriter? narration) : IProgress<DiagnosticProgress>
    {
        public void Report(DiagnosticProgress value) => narration?.WriteLine(value.Message);
    }
}
