using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;
using AutomationPlatform.Platform.Windows;

namespace AutomationPlatform.Tests;

public class DemoPrintingTests
{
    private static Task<DiagnosticReport> RunAsync(string scenario, string diagnosticId) =>
        new DiagnosticEngine(PrintingComposition.CreateDemoRegistry(scenario)!).RunAsync(diagnosticId);

    [Fact]
    public void Cenario_inexistente_nao_cria_registro() =>
        Assert.Null(PrintingComposition.CreateDemoRegistry("nao-existe"));

    [Fact]
    public async Task Todo_cenario_documentado_existe_e_roda_ate_o_fim()
    {
        foreach (var name in DemoPrintingProvider.Scenarios.Keys)
        {
            var report = await RunAsync(name, NotPrintingScenario.DiagnosticId);
            Assert.Equal(ExecutionStatus.Completed, report.ExecutionStatus);
        }
    }

    [Fact]
    public async Task Tudo_ok_nao_acusa_problema()
    {
        var report = await RunAsync("tudo-ok", FullPrintingDiagnostic.DiagnosticId);

        Assert.Equal(OverallStatus.NoProblemDetected, report.OverallStatus);
    }

    [Theory]
    [InlineData("spooler-parado")]
    [InlineData("pausada")]
    [InlineData("offline")]
    [InlineData("sem-porta")]
    [InlineData("erro-dispositivo")]
    [InlineData("papel-atolado")]
    [InlineData("fila-presa")]
    [InlineData("padrao-virtual")]
    [InlineData("varias")]
    public async Task Cenarios_de_problema_sao_apontados_como_problema(string scenario)
    {
        var report = await RunAsync(scenario, FullPrintingDiagnostic.DiagnosticId);

        Assert.NotEqual(OverallStatus.NoProblemDetected, report.OverallStatus);
        Assert.NotEmpty(report.Findings);
    }

    [Fact]
    public async Task Sem_acesso_e_inconclusivo_e_nunca_sem_problema()
    {
        var report = await RunAsync("sem-acesso", NotPrintingScenario.DiagnosticId);

        Assert.NotEqual(OverallStatus.NoProblemDetected, report.OverallStatus);
    }
}
