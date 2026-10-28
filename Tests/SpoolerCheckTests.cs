using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;

namespace AutomationPlatform.Tests;

public class SpoolerCheckTests
{
    private sealed class ListProgress : IProgress<DiagnosticProgress>
    {
        public List<string> Messages { get; } = [];
        public void Report(DiagnosticProgress value) => Messages.Add(value.Message);
    }

    private static SourceResult<ServiceInfo> Service(ServiceState state, string? mode = "Auto") =>
        SourceResult<ServiceInfo>.Ok([new ServiceInfo("Spooler", state, mode)]);

    private static Task<CheckResult<ServiceInfo>> RunAsync(SourceResult<ServiceInfo> spooler, IProgress<DiagnosticProgress>? progress = null) =>
        new SpoolerCheck(new FakePrintSystemProvider { Spooler = spooler })
            .RunAsync(progress ?? new ListProgress(), CancellationToken.None);

    [Fact]
    public async Task Servico_em_execucao_e_saudavel_mas_registra_limitacoes()
    {
        var result = await RunAsync(Service(ServiceState.Running));

        Assert.Equal(CheckVerdict.Healthy, result.Verdict);
        Assert.Equal(ServiceState.Running, result.Facts!.State);
        Assert.Empty(result.Findings);
        Assert.Single(result.Evidence);
        // Saudável nunca é prova de que a impressão funciona.
        Assert.Contains(result.Limitations, l => l.Contains("não prova"));
    }

    [Theory]
    [InlineData(ServiceState.Stopped)]
    [InlineData(ServiceState.StartPending)]
    [InlineData(ServiceState.StopPending)]
    [InlineData(ServiceState.Paused)]
    public async Task Servico_fora_de_execucao_e_problema_confirmado_com_evidencia(ServiceState state)
    {
        var result = await RunAsync(Service(state));

        Assert.Equal(CheckVerdict.Problem, result.Verdict);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(Confidence.Confirmed, finding.Confidence);
        Assert.Contains(finding.EvidenceIds.Single(), result.Evidence.Select(e => e.Id));
        Assert.Single(result.Recommendations);
    }

    [Fact]
    public async Task Servico_desabilitado_orienta_chamar_um_administrador()
    {
        var result = await RunAsync(Service(ServiceState.Stopped, mode: "Disabled"));

        Assert.Contains("desabilitado", result.Findings.Single().Description);
        Assert.Contains("administrador", result.Recommendations.Single().Text);
    }

    [Theory]
    [InlineData(SourceStatus.PermissionDenied)]
    [InlineData(SourceStatus.Unavailable)]
    [InlineData(SourceStatus.Failed)]
    public async Task Fonte_nao_consultada_nao_e_avaliavel(SourceStatus status)
    {
        var result = await RunAsync(SourceResult<ServiceInfo>.Fail(status, "detalhe"));

        Assert.Equal(CheckVerdict.NotEvaluable, result.Verdict);
        Assert.Empty(result.Findings);
        Assert.Equal(status, result.Sources.Single().Status);
    }

    [Fact]
    public async Task Servico_nao_encontrado_nao_e_avaliavel()
    {
        var result = await RunAsync(SourceResult<ServiceInfo>.Ok([]));

        Assert.Equal(CheckVerdict.NotEvaluable, result.Verdict);
        Assert.Null(result.Facts);
    }

    [Fact]
    public async Task Estado_desconhecido_nao_e_avaliavel()
    {
        var result = await RunAsync(Service(ServiceState.Unknown));

        Assert.Equal(CheckVerdict.NotEvaluable, result.Verdict);
    }

    [Fact]
    public async Task Narra_o_que_esta_fazendo()
    {
        var progress = new ListProgress();

        await RunAsync(Service(ServiceState.Running), progress);

        Assert.Contains(progress.Messages, m => m.Contains("Spooler"));
    }

    [Theory]
    [InlineData("Running", ServiceState.Running)]
    [InlineData("stopped", ServiceState.Stopped)]
    [InlineData(" Stop Pending ", ServiceState.StopPending)]
    [InlineData("Unknown", ServiceState.Unknown)]
    [InlineData("algo novo", ServiceState.Unknown)]
    [InlineData(null, ServiceState.Unknown)]
    public void Interpreta_o_texto_de_estado_do_wmi(string? text, ServiceState expected) =>
        Assert.Equal(expected, ServiceStateParser.Parse(text));

    [Theory]
    [InlineData(CheckVerdict.Healthy, OverallStatus.NoProblemDetected)]
    [InlineData(CheckVerdict.Problem, OverallStatus.ProblemConfirmed)]
    [InlineData(CheckVerdict.NotEvaluable, OverallStatus.Inconclusive)]
    public async Task Verificacao_roda_como_diagnostico_pelo_engine(CheckVerdict verdict, OverallStatus expected)
    {
        var state = verdict switch
        {
            CheckVerdict.Healthy => Service(ServiceState.Running),
            CheckVerdict.Problem => Service(ServiceState.Stopped),
            _ => SourceResult<ServiceInfo>.Fail(SourceStatus.Unavailable, "x")
        };
        var registry = new DiagnosticRegistry();
        registry.Register(new CheckDiagnostic<ServiceInfo>(new SpoolerCheck(new FakePrintSystemProvider { Spooler = state })));

        var report = await new DiagnosticEngine(registry).RunAsync(SpoolerCheck.CheckId);

        Assert.Equal(ExecutionStatus.Completed, report.ExecutionStatus);
        Assert.Equal(expected, report.OverallStatus);
    }
}
