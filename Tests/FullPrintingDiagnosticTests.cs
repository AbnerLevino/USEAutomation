using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;

namespace AutomationPlatform.Tests;

public class FullPrintingDiagnosticTests
{
    private static FakePrintSystemProvider HealthySystem() => new()
    {
        Spooler = SourceResult<ServiceInfo>.Ok([new ServiceInfo("Spooler", ServiceState.Running, "Auto")]),
        PrinterStates = SourceResult<PrinterStateInfo>.Ok([new PrinterStateInfo("Impressora X", "USB001", false, 3, 2, true)])
    };

    private static FakePrintDeviceProvider HealthyDevices() => new()
    {
        Devices = SourceResult<PrintDeviceInfo>.Ok([new PrintDeviceInfo("Impressora X", @"USB\VID_1", 0)])
    };

    private static Task<DiagnosticReport> RunAsync(FakePrintSystemProvider sys, FakePrintDeviceProvider dev)
    {
        var registry = new DiagnosticRegistry();
        registry.Register(FullPrintingDiagnostic.Create(sys, dev));
        return new DiagnosticEngine(registry).RunAsync(FullPrintingDiagnostic.DiagnosticId);
    }

    [Fact]
    public async Task Tudo_saudavel_e_NoProblemDetected_com_a_ressalva()
    {
        var report = await RunAsync(HealthySystem(), HealthyDevices());

        Assert.Equal(ExecutionStatus.Completed, report.ExecutionStatus);
        Assert.Equal(OverallStatus.NoProblemDetected, report.OverallStatus);
        Assert.Contains("6 verificação(ões) sem problema", report.Summary);
        Assert.Contains("não prova", report.Summary);
        Assert.Empty(report.Findings);
        // Painel saudável não manda "falar com o suporte".
        Assert.Empty(report.Recommendations);
    }

    [Fact]
    public async Task Avalia_todas_as_filas_inclusive_a_virtual_pausada()
    {
        var sys = HealthySystem();
        sys.PrinterStates = SourceResult<PrinterStateInfo>.Ok(
        [
            new PrinterStateInfo("Impressora X", "USB001", false, 3, 2, true),
            new PrinterStateInfo("Microsoft Print to PDF", "PORTPROMPT:", false, 8, 2, false)
        ]);

        var report = await RunAsync(sys, HealthyDevices());

        Assert.Equal(OverallStatus.ProblemConfirmed, report.OverallStatus);
        Assert.Contains(report.Findings, f => f.Title.Contains("Microsoft Print to PDF") && f.Title.Contains("pausada"));
    }

    [Fact]
    public async Task Problema_em_uma_fila_que_nao_e_a_padrao_aparece_no_completo()
    {
        var sys = HealthySystem();
        sys.PrinterStates = SourceResult<PrinterStateInfo>.Ok(
        [
            new PrinterStateInfo("Impressora X", "USB001", false, 3, 2, true),
            new PrinterStateInfo("Impressora Y", "USB002", true, 3, 2, false)
        ]);

        var report = await RunAsync(sys, HealthyDevices());

        Assert.Contains(report.Findings, f => f.Title.Contains("Impressora Y") && f.Title.Contains("offline"));
    }

    [Fact]
    public async Task Verificacao_nao_avaliavel_sem_problema_e_inconclusivo()
    {
        var sys = HealthySystem();
        sys.Jobs = SourceResult<PrintJobInfo>.Fail(SourceStatus.PermissionDenied, "negado");

        var report = await RunAsync(sys, HealthyDevices());

        Assert.Equal(OverallStatus.Inconclusive, report.OverallStatus);
        Assert.Contains("1 não avaliável", report.Summary);
    }

    [Fact]
    public async Task So_aviso_de_toner_continua_sem_problema_mas_mostra_o_aviso()
    {
        var sys = HealthySystem();
        sys.PrinterStates = SourceResult<PrinterStateInfo>.Ok([new PrinterStateInfo("Impressora X", "USB001", false, 3, 5, true)]);

        var report = await RunAsync(sys, HealthyDevices());

        Assert.Equal(OverallStatus.NoProblemDetected, report.OverallStatus);
        Assert.Contains(report.Findings, f => f.Title.StartsWith("Atenção:") && f.Title.Contains("pouco toner"));
        Assert.Contains("há avisos", report.Summary);
    }

    [Fact]
    public async Task Causa_bloqueante_aparece_como_provavel_causa_tambem_no_completo()
    {
        var sys = HealthySystem();
        sys.Spooler = SourceResult<ServiceInfo>.Ok([new ServiceInfo("Spooler", ServiceState.Stopped, "Auto")]);

        var report = await RunAsync(sys, HealthyDevices());

        Assert.Equal(OverallStatus.ProblemConfirmed, report.OverallStatus);
        Assert.StartsWith("Causa provável:", report.Findings[0].Title);
    }
}
