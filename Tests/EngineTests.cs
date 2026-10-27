using AutomationPlatform.Contracts;
using AutomationPlatform.Engine;

namespace AutomationPlatform.Tests;

public class EngineTests
{
    // Diagnóstico falso: o comportamento é injetado pelo teste.
    private sealed class FakeDiagnostic(Func<IProgress<DiagnosticProgress>, CancellationToken, Task<DiagnosticOutcome>> run)
        : IDiagnostic
    {
        public string Id => "fake.test";
        public string DisplayName => "Diagnóstico falso";
        public Task<DiagnosticOutcome> RunAsync(IProgress<DiagnosticProgress> progress, CancellationToken ct) =>
            run(progress, ct);
    }

    // Progress<T> entrega em outra thread; este é síncrono e previsível nos testes.
    private sealed class ListProgress : IProgress<DiagnosticProgress>
    {
        public List<string> Messages { get; } = [];
        public void Report(DiagnosticProgress value) => Messages.Add(value.Message);
    }

    private static DiagnosticEngine EngineWith(IDiagnostic diagnostic)
    {
        var registry = new DiagnosticRegistry();
        registry.Register(diagnostic);
        return new DiagnosticEngine(registry);
    }

    private static DiagnosticOutcome Outcome(OverallStatus status) =>
        new() { OverallStatus = status, Summary = "ok" };

    [Fact]
    public async Task Concluido_copia_o_resultado_do_diagnostico()
    {
        var engine = EngineWith(new FakeDiagnostic((_, _) =>
            Task.FromResult(Outcome(OverallStatus.NoProblemDetected))));

        var report = await engine.RunAsync("fake.test");

        Assert.Equal(ExecutionStatus.Completed, report.ExecutionStatus);
        Assert.Equal(OverallStatus.NoProblemDetected, report.OverallStatus);
        Assert.Equal("fake.test", report.DiagnosticId);
        Assert.Equal(ReportSchema.CurrentVersion, report.SchemaVersion);
        Assert.True(report.CompletedAtUtc >= report.StartedAtUtc);
    }

    [Fact]
    public async Task Id_inexistente_gera_falha_sem_lancar_excecao()
    {
        var engine = new DiagnosticEngine(new DiagnosticRegistry());

        var report = await engine.RunAsync("nao.existe");

        Assert.Equal(ExecutionStatus.Failed, report.ExecutionStatus);
        Assert.Equal(OverallStatus.NotEvaluated, report.OverallStatus);
        Assert.Contains("nao.existe", report.ErrorMessage);
    }

    [Fact]
    public async Task Excecao_vira_falha_e_nao_e_escondida()
    {
        var engine = EngineWith(new FakeDiagnostic((_, _) =>
            throw new InvalidOperationException("boom")));

        var report = await engine.RunAsync("fake.test");

        Assert.Equal(ExecutionStatus.Failed, report.ExecutionStatus);
        Assert.Equal(OverallStatus.NotEvaluated, report.OverallStatus);
        Assert.Contains("InvalidOperationException", report.ErrorMessage);
        Assert.Contains("boom", report.ErrorMessage);
    }

    [Fact]
    public async Task Acesso_negado_vira_PermissionDenied()
    {
        var engine = EngineWith(new FakeDiagnostic((_, _) =>
            throw new UnauthorizedAccessException("negado")));

        var report = await engine.RunAsync("fake.test");

        Assert.Equal(ExecutionStatus.PermissionDenied, report.ExecutionStatus);
        Assert.Equal(OverallStatus.NotEvaluated, report.OverallStatus);
    }

    [Fact]
    public async Task Tempo_limite_interrompe_mesmo_se_o_diagnostico_ignorar_o_token()
    {
        var engine = EngineWith(new FakeDiagnostic(async (_, _) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30)); // ignora o token de propósito
            return Outcome(OverallStatus.Inconclusive);
        }));

        var report = await engine.RunAsync("fake.test", timeout: TimeSpan.FromMilliseconds(50));

        Assert.Equal(ExecutionStatus.TimedOut, report.ExecutionStatus);
    }

    [Fact]
    public async Task Cancelamento_do_solicitante_vira_Cancelled()
    {
        using var cts = new CancellationTokenSource();
        var engine = EngineWith(new FakeDiagnostic(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Outcome(OverallStatus.Inconclusive);
        }));

        var running = engine.RunAsync("fake.test", cancellationToken: cts.Token);
        cts.Cancel();
        var report = await running;

        Assert.Equal(ExecutionStatus.Cancelled, report.ExecutionStatus);
    }

    [Fact]
    public async Task Mensagens_de_progresso_chegam_a_quem_pediu()
    {
        var engine = EngineWith(new FakeDiagnostic((progress, _) =>
        {
            progress.Report(new DiagnosticProgress("etapa do diagnóstico", DateTimeOffset.UtcNow));
            return Task.FromResult(Outcome(OverallStatus.Inconclusive));
        }));
        var progress = new ListProgress();

        await engine.RunAsync("fake.test", progress);

        Assert.Contains("etapa do diagnóstico", progress.Messages);
        Assert.True(progress.Messages.Count >= 3); // início, etapa, fim
    }

    [Fact]
    public void Registro_rejeita_id_duplicado()
    {
        var registry = new DiagnosticRegistry();
        registry.Register(new FakeDiagnostic((_, _) => Task.FromResult(Outcome(OverallStatus.Inconclusive))));

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(new FakeDiagnostic((_, _) => Task.FromResult(Outcome(OverallStatus.Inconclusive)))));
    }
}
