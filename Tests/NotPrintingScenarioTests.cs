using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;

namespace AutomationPlatform.Tests;

public class NotPrintingScenarioTests
{
    private sealed class NullProgress : IProgress<DiagnosticProgress>
    {
        public void Report(DiagnosticProgress value) { }
    }

    private sealed class ListProgress : IProgress<DiagnosticProgress>
    {
        public List<string> Messages { get; } = [];
        public void Report(DiagnosticProgress value) => Messages.Add(value.Message);
    }

    /// <summary>Um "mundo" de impressão pronto: por padrão, tudo saudável com uma impressora USB padrão.</summary>
    private sealed class World
    {
        public FakePrintSystemProvider Sys { get; } = new();
        public FakePrintDeviceProvider Dev { get; } = new();

        public World()
        {
            Sys.Spooler = SourceResult<ServiceInfo>.Ok([new ServiceInfo("Spooler", ServiceState.Running, "Auto")]);
            Printers(Usb("Impressora X", isDefault: true));
            Dev.Devices = SourceResult<PrintDeviceInfo>.Ok([new PrintDeviceInfo("Impressora X", @"USB\VID_1", 0)]);
        }

        public static PrinterStateInfo Usb(string name, bool isDefault = false, bool offline = false, int extended = 3, int error = 2, string? port = "USB001") =>
            new(name, port, offline, extended, error, isDefault);

        public static PrinterStateInfo Pdf(bool isDefault = false) =>
            new("Microsoft Print to PDF", "PORTPROMPT:", false, 2, 0, isDefault);

        public World Printers(params PrinterStateInfo[] p) { Sys.PrinterStates = SourceResult<PrinterStateInfo>.Ok(p); return this; }
        public World Jobs(params PrintJobInfo[] j) { Sys.Jobs = SourceResult<PrintJobInfo>.Ok(j); return this; }
        public World SpoolerIs(ServiceState state) { Sys.Spooler = SourceResult<ServiceInfo>.Ok([new ServiceInfo("Spooler", state, "Auto")]); return this; }

        public NotPrintingScenario Scenario() => NotPrintingScenario.Create(Sys, Dev);

        public async Task<ScenarioAnalysis> AnalyzeAsync() => (await Scenario().AnalyzeAsync(new NullProgress(), CancellationToken.None)).Analysis;

        public async Task<DiagnosticReport> RunAsync()
        {
            var registry = new DiagnosticRegistry();
            registry.Register(Scenario());
            return await new DiagnosticEngine(registry).RunAsync(NotPrintingScenario.DiagnosticId);
        }
    }

    private static PrintJobInfo Job(string printer, int id, PrintJobStatus? status = PrintJobStatus.None) => new(printer, id, status);

    // ---------- causas isoladas ----------

    [Fact]
    public async Task Spooler_parado_e_a_causa_principal_com_acao_de_reiniciar()
    {
        var analysis = await new World().SpoolerIs(ServiceState.Stopped).AnalyzeAsync();

        var primary = analysis.Primary!;
        Assert.Equal(CauseCodes.SpoolerNotRunning, primary.Code);
        Assert.Equal(PrinterActionIds.RestartSpooler, primary.CandidateActionId);
        Assert.Equal(Confidence.Confirmed, primary.Confidence);
    }

    [Fact]
    public async Task Fila_padrao_pausada_e_a_causa_principal_com_acao_de_retomar()
    {
        var analysis = await new World().Printers(World.Usb("Impressora X", isDefault: true, extended: 8)).AnalyzeAsync();

        var primary = analysis.Primary!;
        Assert.Equal(CauseCodes.PrinterPaused, primary.Code);
        Assert.Equal("Impressora X", primary.PrinterName);
        Assert.Equal(PrinterActionIds.ResumePrinter, primary.CandidateActionId);
    }

    [Fact]
    public async Task Fila_padrao_offline_e_causa_com_acao_de_colocar_online()
    {
        var analysis = await new World().Printers(World.Usb("Impressora X", isDefault: true, offline: true)).AnalyzeAsync();

        Assert.Equal(CauseCodes.PrinterOffline, analysis.Primary!.Code);
        Assert.Equal(PrinterActionIds.SetPrinterOnline, analysis.Primary.CandidateActionId);
    }

    [Fact]
    public async Task Fila_sem_porta_e_causa_so_com_orientacao()
    {
        var analysis = await new World().Printers(World.Usb("Impressora X", isDefault: true, port: null)).AnalyzeAsync();

        Assert.Equal(CauseCodes.PrinterNoPort, analysis.Primary!.Code);
        Assert.Null(analysis.Primary.CandidateActionId);
    }

    [Fact]
    public async Task Dispositivo_com_erro_de_driver_e_causa_com_o_significado_do_codigo()
    {
        var world = new World();
        world.Dev.Devices = SourceResult<PrintDeviceInfo>.Ok([new PrintDeviceInfo("Impressora X", @"USB\VID_1", 28)]);

        var analysis = await world.AnalyzeAsync();

        Assert.Equal(CauseCodes.DeviceError, analysis.Primary!.Code);
        Assert.Contains("drivers", analysis.Primary.Description);
        Assert.Null(analysis.Primary.CandidateActionId);
    }

    [Theory]
    [InlineData(4, "sem papel")]
    [InlineData(6, "sem toner")]
    [InlineData(7, "tampa")]
    [InlineData(8, "atolado")]
    public async Task Erro_bloqueante_do_equipamento_e_causa(int errorState, string text)
    {
        var analysis = await new World().Printers(World.Usb("Impressora X", isDefault: true, error: errorState)).AnalyzeAsync();

        Assert.Equal(CauseCodes.EquipmentError, analysis.Primary!.Code);
        Assert.Contains(text, analysis.Primary.Description);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public async Task Pouco_papel_ou_toner_e_aviso_e_nao_causa(int errorState)
    {
        var analysis = await new World().Printers(World.Usb("Impressora X", isDefault: true, error: errorState)).AnalyzeAsync();

        Assert.Null(analysis.Primary);
        var warning = Assert.Single(analysis.Causes);
        Assert.Equal(CauseCodes.EquipmentWarning, warning.Code);
        Assert.False(warning.IsBlocking);
    }

    [Fact]
    public async Task Trabalho_preso_e_causa_quando_o_spooler_roda()
    {
        var analysis = await new World().Jobs(Job("Impressora X", 1, PrintJobStatus.Error)).AnalyzeAsync();

        Assert.Equal(CauseCodes.StuckJobs, analysis.Primary!.Code);
        Assert.Equal(PrinterActionIds.CancelStuckJobs, analysis.Primary.CandidateActionId);
    }

    [Fact]
    public async Task Padrao_virtual_com_fisica_instalada_e_causa_provavel_com_sugestao()
    {
        var analysis = await new World().Printers(World.Pdf(isDefault: true), World.Usb("Impressora X")).AnalyzeAsync();

        var primary = analysis.Primary!;
        Assert.Equal(CauseCodes.DefaultIsVirtual, primary.Code);
        Assert.Equal(Confidence.Probable, primary.Confidence);
        Assert.Equal("Impressora X", primary.PrinterName);
        Assert.Equal(PrinterActionIds.SetDefaultPrinter, primary.CandidateActionId);
    }

    [Fact]
    public async Task Sem_nenhuma_padrao_e_causa_e_avalia_as_filas_fisicas()
    {
        var analysis = await new World().Printers(World.Usb("Impressora X", extended: 8)).AnalyzeAsync();

        Assert.Contains(analysis.Causes, c => c.Code == CauseCodes.NoDefaultPrinter);
        Assert.Contains(analysis.Causes, c => c.Code == CauseCodes.PrinterPaused);
        Assert.Equal(CauseCodes.PrinterPaused, analysis.Primary!.Code); // Rank 2 vence o Rank 8
    }

    // ---------- causa x consequência e prioridade ----------

    [Fact]
    public async Task Caso_real_padrao_pausada_com_um_trabalho_o_trabalho_e_consequencia_nao_causa()
    {
        // Observado no Windows real em 2026-10-28: 'TESTE Pausada' (padrão, status 8) com 1 trabalho de StatusMask 0.
        var analysis = await new World()
            .Printers(World.Usb("TESTE Pausada", isDefault: true, port: "LPT1:", extended: 8), World.Pdf())
            .Jobs(Job("TESTE Pausada", 4, PrintJobStatus.None))
            .AnalyzeAsync();

        Assert.Equal(CauseCodes.PrinterPaused, analysis.Primary!.Code);
        Assert.DoesNotContain(analysis.Causes, c => c.Code == CauseCodes.StuckJobs);
        var consequence = Assert.Single(analysis.Consequences);
        Assert.Contains("1 trabalho(s)", consequence);
        Assert.Contains("pausada", consequence);
    }

    [Fact]
    public async Task Spooler_parado_com_fila_acumulada_a_fila_e_consequencia()
    {
        var analysis = await new World()
            .SpoolerIs(ServiceState.Stopped)
            .Jobs(Job("Impressora X", 1, PrintJobStatus.Error), Job("Impressora X", 2))
            .AnalyzeAsync();

        Assert.Equal(CauseCodes.SpoolerNotRunning, analysis.Primary!.Code);
        Assert.DoesNotContain(analysis.Causes, c => c.Code == CauseCodes.StuckJobs);
        Assert.Contains("2 trabalho(s)", Assert.Single(analysis.Consequences));
        Assert.Contains("serviço de impressão parado", analysis.Consequences[0]);
    }

    [Fact]
    public async Task Duas_causas_a_de_menor_rank_e_a_principal_e_a_outra_continua_listada()
    {
        var analysis = await new World()
            .Printers(World.Usb("Impressora X", isDefault: true, extended: 8))
            .Jobs(Job("Impressora X", 1, PrintJobStatus.Error))
            .AnalyzeAsync();

        Assert.Equal([CauseCodes.PrinterPaused, CauseCodes.StuckJobs], analysis.Causes.Select(c => c.Code).ToArray());
        Assert.Equal(CauseCodes.PrinterPaused, analysis.Primary!.Code);
    }

    [Fact]
    public async Task Spooler_parado_vence_fila_pausada()
    {
        var analysis = await new World().SpoolerIs(ServiceState.Stopped)
            .Printers(World.Usb("Impressora X", isDefault: true, extended: 8)).AnalyzeAsync();

        Assert.Equal([CauseCodes.SpoolerNotRunning, CauseCodes.PrinterPaused], analysis.Causes.Select(c => c.Code).ToArray());
    }

    // ---------- sem causa, sem evidência, alvos ----------

    [Fact]
    public async Task Mundo_saudavel_nao_acha_causa_e_e_inconclusivo()
    {
        var report = await new World().RunAsync();

        Assert.Equal(ExecutionStatus.Completed, report.ExecutionStatus);
        Assert.Equal(OverallStatus.Inconclusive, report.OverallStatus);
        Assert.Empty(report.Findings);
        Assert.Contains("não prova", report.Summary);
        Assert.Contains(report.Recommendations, r => r.Text.Contains("página de teste"));
    }

    [Fact]
    public async Task Verificacao_nao_avaliavel_vira_nota_e_nunca_tudo_bem()
    {
        var world = new World();
        world.Sys.Spooler = SourceResult<ServiceInfo>.Fail(SourceStatus.PermissionDenied, "negado");

        var report = await world.RunAsync();
        var analysis = await world.AnalyzeAsync();

        Assert.Equal(OverallStatus.Inconclusive, report.OverallStatus);
        Assert.Contains(analysis.Notes, n => n.Contains("Serviço de impressão") && n.Contains("não pôde ser avaliada"));
        Assert.Contains("não pôde(ram) ser avaliada(s)", report.Summary);
    }

    [Fact]
    public async Task So_virtual_instalada_nao_ha_alvo_e_orienta_o_reconhecimento()
    {
        var world = new World().Printers(World.Pdf(isDefault: true));
        world.Dev.Devices = SourceResult<PrintDeviceInfo>.Ok([]);

        var analysis = await world.AnalyzeAsync();

        Assert.Null(analysis.Primary);
        Assert.Contains(analysis.Notes, n => n.Contains("Não há impressora física instalada") && n.Contains("printing.device-recognition"));
    }

    [Fact]
    public async Task Padrao_fisica_avalia_so_ela_e_avisa_das_outras_filas_fisicas()
    {
        var analysis = await new World()
            .Printers(World.Usb("Impressora X", isDefault: true), World.Usb("Impressora Y", extended: 8))
            .AnalyzeAsync();

        Assert.Equal(["Impressora X"], analysis.TargetPrinters.ToArray());
        Assert.Null(analysis.Primary); // a pausa de Y não é o alvo
        Assert.Contains(analysis.Notes, n => n.Contains("Impressora Y"));
    }

    // ---------- relatório ----------

    [Fact]
    public async Task Relatorio_tem_evidencias_com_ids_unicos_e_achados_apontando_para_elas()
    {
        var report = await new World()
            .Printers(World.Usb("Impressora X", isDefault: true, extended: 8))
            .Jobs(Job("Impressora X", 1))
            .RunAsync();

        Assert.Equal(OverallStatus.ProblemConfirmed, report.OverallStatus);
        var ids = report.Evidence.Select(e => e.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(report.Findings.SelectMany(f => f.EvidenceIds), id => Assert.Contains(id, ids));
        Assert.StartsWith("Causa provável:", report.Findings[0].Title);
        Assert.Contains(report.Findings, f => f.Title == "Consequência");
        Assert.Equal(report.Sources.Count, report.Sources.Select(s => s.Name).Distinct().Count());
    }

    [Fact]
    public async Task Causa_provavel_gera_relatorio_provavel_nao_confirmado()
    {
        var report = await new World().Printers(World.Pdf(isDefault: true), World.Usb("Impressora X")).RunAsync();

        Assert.Equal(OverallStatus.ProblemProbable, report.OverallStatus);
    }

    [Fact]
    public async Task Narra_o_cruzamento_em_portugues()
    {
        var progress = new ListProgress();

        await new World().SpoolerIs(ServiceState.Stopped).Scenario().AnalyzeAsync(progress, CancellationToken.None);

        Assert.Contains(progress.Messages, m => m.Contains("cruzar"));
        Assert.Contains(progress.Messages, m => m.StartsWith("Causa provável:"));
    }

    [Fact]
    public void E_um_cenario_com_sintoma_e_roda_como_diagnostico_comum()
    {
        IScenario scenario = new World().Scenario();

        Assert.Equal("printing.not-printing", scenario.Id);
        Assert.Equal("Impressora não está imprimindo", scenario.Symptom);
        Assert.IsAssignableFrom<IDiagnostic>(scenario);
    }
}
