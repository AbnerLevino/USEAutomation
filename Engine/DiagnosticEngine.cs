using AutomationPlatform.Contracts;

namespace AutomationPlatform.Engine;

/// <summary>
/// Executa um diagnóstico pelo id e SEMPRE devolve um <see cref="DiagnosticReport"/>:
/// falhas viram relatório com o estado correspondente, nunca são engolidas.
/// </summary>
public sealed class DiagnosticEngine
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly DiagnosticRegistry _registry;
    private readonly TimeProvider _time;

    public DiagnosticEngine(DiagnosticRegistry registry, TimeProvider? timeProvider = null)
    {
        _registry = registry;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<DiagnosticReport> RunAsync(
        string diagnosticId,
        IProgress<DiagnosticProgress>? progress = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var started = _time.GetUtcNow();
        var report = progress ?? new NullProgress();

        if (!_registry.TryGet(diagnosticId, out var diagnostic) || diagnostic is null)
        {
            return Failure(diagnosticId, started, ExecutionStatus.Failed,
                $"Diagnóstico '{diagnosticId}' não encontrado.",
                $"Nenhum diagnóstico está registrado com o id '{diagnosticId}'.");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout ?? DefaultTimeout);

        try
        {
            Notify(report, $"Iniciando: {diagnostic.DisplayName}.");

            // WaitAsync devolve o controle ao estourar o tempo mesmo que o
            // diagnóstico ignore o token de cancelamento.
            var outcome = await diagnostic.RunAsync(report, timeoutSource.Token)
                .WaitAsync(timeoutSource.Token);

            Notify(report, "Análise concluída.");
            return new DiagnosticReport
            {
                DiagnosticId = diagnostic.Id,
                ExecutionStatus = ExecutionStatus.Completed,
                OverallStatus = outcome.OverallStatus,
                StartedAtUtc = started,
                CompletedAtUtc = _time.GetUtcNow(),
                Summary = outcome.Summary,
                Findings = outcome.Findings,
                Evidence = outcome.Evidence,
                Sources = outcome.Sources,
                Limitations = outcome.Limitations,
                Recommendations = outcome.Recommendations
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(diagnostic.Id, started, ExecutionStatus.Cancelled,
                "O diagnóstico foi cancelado antes de terminar.", "Cancelado pelo solicitante.");
        }
        catch (OperationCanceledException)
        {
            return Failure(diagnostic.Id, started, ExecutionStatus.TimedOut,
                "O diagnóstico excedeu o tempo limite e foi interrompido.",
                $"Tempo limite de {(timeout ?? DefaultTimeout).TotalSeconds:0.#} s excedido.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Failure(diagnostic.Id, started, ExecutionStatus.PermissionDenied,
                "Sem permissão para coletar os dados necessários.", Describe(ex));
        }
        catch (Exception ex)
        {
            return Failure(diagnostic.Id, started, ExecutionStatus.Failed,
                "Ocorreu uma falha técnica durante o diagnóstico.", Describe(ex));
        }
    }

    private DiagnosticReport Failure(string id, DateTimeOffset started, ExecutionStatus status, string summary, string error) =>
        new()
        {
            DiagnosticId = id,
            ExecutionStatus = status,
            OverallStatus = OverallStatus.NotEvaluated,
            StartedAtUtc = started,
            CompletedAtUtc = _time.GetUtcNow(),
            Summary = summary,
            ErrorMessage = error,
            Limitations = ["Nenhuma conclusão foi tirada: a execução não terminou normalmente."]
        };

    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

    private void Notify(IProgress<DiagnosticProgress> progress, string message) =>
        progress.Report(new DiagnosticProgress(message, _time.GetUtcNow()));

    private sealed class NullProgress : IProgress<DiagnosticProgress>
    {
        public void Report(DiagnosticProgress value) { }
    }
}
