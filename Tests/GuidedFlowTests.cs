using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;

namespace AutomationPlatform.Tests;

public class GuidedFlowTests
{
    private sealed class Catalog(params IRemediationAction[] actions) : IActionCatalog
    {
        public IRemediationAction? Find(string actionId) => actions.FirstOrDefault(a => a.Id == actionId);
    }

    /// <summary>Tudo que um teste de fluxo precisa, já ligado.</summary>
    private sealed class Kit
    {
        public GuidedWorld World { get; } = new();
        public FakeUserInteraction User { get; } = new();
        public EventLog Events { get; } = new();
        public InMemoryAuditLog Audit { get; } = new();
        public IActionCatalog Catalog { get; set; } = new GuidedFlowTests.Catalog();

        public Task<DiagnosticReport> RunAsync(CancellationToken ct = default) => new GuidedNotPrintingFlow(
            World.Scenario(), Catalog, new RemediationPipeline(Audit), User).RunAsync(Events, ct);

        public static string ResumeId => PrinterActionIds.ResumePrinter;
        public void PauseX() => World.Printers(GuidedWorld.Usb("Impressora X", isDefault: true, extended: 8));
        public void JamX() => World.Printers(GuidedWorld.Usb("Impressora X", isDefault: true, error: 8));
        public void FixX() => World.Printers(GuidedWorld.Usb("Impressora X", isDefault: true));
    }

    // ---------- correção com ação conhecida ----------

    [Fact]
    public async Task Fila_pausada_com_acao_confirmada_e_usuario_diz_que_voltou_fecha_como_resolvido_pelo_usuario()
    {
        var kit = new Kit();
        kit.PauseX();
        var action = new FixAction(Kit.ResumeId, kit.FixX);
        kit.Catalog = new Catalog(action);
        kit.User.WillConfirm(true).WillAnswerResolved(ResolutionAnswer.Worked);

        var report = await kit.RunAsync();

        Assert.True(action.Executed);
        Assert.Equal(ExecutionStatus.Completed, report.ExecutionStatus);
        Assert.Equal(FlowResult.Resolved, report.Closing!.Result);
        Assert.Equal(ClosingBasis.UserAnswer, report.Closing.Basis);
        Assert.Equal(ActionStatus.Resolved, Assert.Single(report.Actions).Status);
        Assert.Single(kit.Audit.Records);
    }

    [Fact]
    public async Task Usuario_que_nao_pode_testar_deixa_a_leitura_objetiva_decidir_e_o_relatorio_diz_isso()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.Catalog = new Catalog(new FixAction(Kit.ResumeId, kit.FixX));
        kit.User.WillConfirm(true).WillAnswerResolved(ResolutionAnswer.CannotTest);

        var report = await kit.RunAsync();

        Assert.Equal(FlowResult.Resolved, report.Closing!.Result);
        Assert.Equal(ClosingBasis.ObjectiveVerification, report.Closing.Basis);
    }

    [Fact]
    public async Task Usuario_que_diz_que_continua_sem_imprimir_vence_a_leitura_objetiva()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.Catalog = new Catalog(new FixAction(Kit.ResumeId, kit.FixX));
        kit.User.WillConfirm(true).WillAnswerResolved(ResolutionAnswer.StillNotWorking);

        var report = await kit.RunAsync();

        Assert.Equal(FlowResult.NotResolved, report.Closing!.Result);
        Assert.Equal(ClosingBasis.UserAnswer, report.Closing.Basis);
        Assert.Equal(ActionStatus.Resolved, report.Actions.Single().Status);
    }

    [Fact]
    public async Task Verificacao_objetiva_que_falha_nunca_vira_resolvido_quando_o_usuario_nao_pode_testar()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.Catalog = new Catalog(new FixAction(Kit.ResumeId, kit.FixX)
        {
            Verification = new(VerificationOutcome.NotResolved, "a fila continua pausada")
        });
        kit.User.WillConfirm(true).WillAnswerResolved(ResolutionAnswer.CannotTest);

        var report = await kit.RunAsync();

        Assert.Equal(FlowResult.NotResolved, report.Closing!.Result);
    }

    [Fact]
    public async Task Usuario_que_recusa_a_correcao_nada_e_alterado_e_o_fechamento_nao_e_perguntado()
    {
        var kit = new Kit();
        kit.PauseX();
        var action = new FixAction(Kit.ResumeId, kit.FixX);
        kit.Catalog = new Catalog(action);
        kit.User.WillConfirm(false);

        var report = await kit.RunAsync();

        Assert.False(action.Executed);
        Assert.Equal(ActionStatus.NotConfirmed, report.Actions.Single().Status);
        Assert.Equal(FlowResult.NotResolved, report.Closing!.Result);
        Assert.Equal(ClosingBasis.None, report.Closing.Basis);
    }

    [Fact]
    public async Task Causa_que_ja_nao_existe_na_hora_de_agir_e_inconclusiva_e_nada_e_perguntado()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.Catalog = new Catalog(new FixAction(Kit.ResumeId, kit.FixX) { Precondition = PreconditionResult.Not("a fila já foi retomada") });

        var report = await kit.RunAsync();

        Assert.Equal(FlowResult.Inconclusive, report.Closing!.Result);
        Assert.Empty(kit.User.Confirmed);
    }

    // ---------- o que a automação não corrige sozinha vai para o suporte, não para o usuário ----------

    [Fact]
    public async Task Correcao_conhecida_mas_indisponivel_nao_manda_o_usuario_fazer_nada_e_vai_para_o_suporte()
    {
        var kit = new Kit();
        kit.PauseX();   // causa com correção conhecida (retomar), mas o catálogo está vazio

        var report = await kit.RunAsync();

        Assert.Empty(kit.User.PhysicalRequests);
        Assert.Empty(kit.User.Confirmed);
        Assert.Empty(kit.User.Questions);
        Assert.Equal(FlowResult.NotResolved, report.Closing!.Result);
        Assert.Equal(ClosingBasis.None, report.Closing.Basis);
        Assert.Contains("Eu sei como corrigir", report.Closing.Message);
        Assert.Contains("relatório pronto para o suporte", report.Closing.Message);
    }

    [Fact]
    public async Task Fila_sem_porta_e_tecnica_vai_para_o_suporte_sem_tarefa_para_o_usuario()
    {
        var kit = new Kit();
        kit.World.Printers(GuidedWorld.Usb("Impressora X", isDefault: true, port: null));

        var report = await kit.RunAsync();

        Assert.Empty(kit.User.PhysicalRequests);
        Assert.Equal(CauseCodes.PrinterNoPort, kit.Events.Of<CauseFound>().Single().Code);
        Assert.Contains("precisa de um técnico", report.Closing!.Message);
    }

    [Fact]
    public async Task Erro_de_dispositivo_do_windows_e_tecnico_e_vai_para_o_suporte()
    {
        var kit = new Kit();
        kit.World.Dev.Devices = SourceResult<PrintDeviceInfo>.Ok([new PrintDeviceInfo("Impressora X", @"USB\VID_1", 28)]);

        var report = await kit.RunAsync();

        Assert.Empty(kit.User.PhysicalRequests);
        Assert.Contains("precisa de um técnico", report.Closing!.Message);
    }

    [Fact]
    public async Task A_entrega_ao_suporte_e_falada_na_conversa_e_o_relatorio_fica_com_a_causa()
    {
        var kit = new Kit();
        kit.PauseX();

        var report = await kit.RunAsync();

        Assert.Contains(kit.Events.Of<Narration>(), n => n.Message.Contains("Deixei o relatório pronto para o suporte"));
        Assert.Contains(report.Findings, f => f.Title.Contains("pausada"));
    }

    [Fact]
    public async Task Erro_de_equipamento_continua_pedindo_a_pessoa_diante_da_impressora()
    {
        var kit = new Kit();
        kit.JamX();
        kit.User.WillDoPhysical(PhysicalActionReply.Skipped);

        await kit.RunAsync();

        Assert.Single(kit.User.PhysicalRequests);
        Assert.Contains("papel atolado", kit.User.PhysicalRequests[0]);
    }

    // ---------- orientação (só quando é preciso uma pessoa no equipamento) ----------

    [Fact]
    public async Task Causa_sem_acao_so_orienta_nao_pede_confirmacao_e_reverifica_depois_do_usuario_agir()
    {
        var kit = new Kit();
        kit.JamX();
        kit.User.OnPhysicalAction = kit.FixX;
        kit.User.WillDoPhysical(PhysicalActionReply.Done).WillAnswerResolved(ResolutionAnswer.Worked);

        var report = await kit.RunAsync();

        Assert.Empty(kit.User.Confirmed);
        Assert.Empty(report.Actions);
        Assert.Single(kit.User.PhysicalRequests);
        Assert.Contains("papel atolado", kit.User.PhysicalRequests[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FlowResult.Resolved, report.Closing!.Result);
        Assert.Equal(ClosingBasis.UserAnswer, report.Closing.Basis);
        // Reverificou de verdade: as seis etapas rodaram duas vezes.
        Assert.Equal(12, kit.Events.Of<StepFinished>().Count());
    }

    [Fact]
    public async Task Depois_da_reverificacao_os_cartoes_ganham_o_estado_atual_e_nao_voltam_ao_inicial()
    {
        var kit = new Kit();
        kit.World.Printers(GuidedWorld.Usb("Impressora X", isDefault: true, error: 8), GuidedWorld.Usb("Impressora Y"));
        kit.User.OnPhysicalAction = () => kit.World.Printers(GuidedWorld.Usb("Impressora X", isDefault: true), GuidedWorld.Usb("Impressora Y"));
        kit.User.WillChoose("Impressora X").WillDoPhysical(PhysicalActionReply.Done).WillAnswerResolved(ResolutionAnswer.Worked);

        await kit.RunAsync();

        // Há duas rodadas de PrintersFound; depois de cada uma, o estado é republicado.
        Assert.Equal(2, kit.Events.Of<PrintersFound>().Count());
        var lastX = kit.Events.Of<PrinterStateChanged>().Last(c => c.Name == "Impressora X");
        Assert.Equal(PrinterCardState.Ok, lastX.State);
        var lastEvent = kit.Events.Events.FindLastIndex(e => e is PrintersFound);
        Assert.Contains(kit.Events.Events.Skip(lastEvent + 1), e => e is PrinterStateChanged { Name: "Impressora X" });
    }

    [Fact]
    public async Task Se_o_usuario_diz_que_fez_mas_o_problema_continua_o_programa_nao_acredita_e_pede_de_novo()
    {
        var kit = new Kit();
        kit.JamX();
        kit.User.WillDoPhysical(PhysicalActionReply.Done, PhysicalActionReply.Skipped);

        var report = await kit.RunAsync();

        Assert.Equal(2, kit.User.PhysicalRequests.Count);
        Assert.Equal(FlowResult.NotResolved, report.Closing!.Result);
        Assert.Contains(kit.Events.Of<Narration>(), n => n.Message.Contains("Ainda vejo o mesmo problema"));
    }

    [Fact]
    public async Task Usuario_que_nao_pode_testar_depois_de_agir_fecha_pela_leitura_objetiva()
    {
        var kit = new Kit();
        kit.JamX();
        kit.User.OnPhysicalAction = kit.FixX;
        kit.User.WillDoPhysical(PhysicalActionReply.Done).WillAnswerResolved(ResolutionAnswer.CannotTest);

        var report = await kit.RunAsync();

        Assert.Equal(FlowResult.Resolved, report.Closing!.Result);
        Assert.Equal(ClosingBasis.ObjectiveVerification, report.Closing.Basis);
    }

    [Fact]
    public async Task Problema_anterior_some_mas_outro_aparece_nao_e_dado_como_resolvido()
    {
        var kit = new Kit();
        kit.JamX();
        kit.User.OnPhysicalAction = () => kit.World.SpoolerIs(ServiceState.Stopped);
        kit.User.WillDoPhysical(PhysicalActionReply.Done);

        var report = await kit.RunAsync();

        Assert.Equal(FlowResult.NotResolved, report.Closing!.Result);
        Assert.Contains("encontrei outro", report.Closing.Message);
    }

    [Fact]
    public async Task Cancelar_o_pedido_fisico_encerra_sem_alterar_nada()
    {
        var kit = new Kit();
        kit.JamX();
        kit.User.WillDoPhysical(PhysicalActionReply.Cancel);

        var report = await kit.RunAsync();

        Assert.Equal(FlowResult.Cancelled, report.Closing!.Result);
        Assert.Empty(report.Actions);
    }

    // ---------- qual impressora? ----------

    // Padrão virtual + duas físicas (A pausada, B saudável): o motor não sabe qual é a do usuário.
    private static void TwoPhysical(Kit kit) => kit.World.Printers(
        GuidedWorld.Pdf(isDefault: true), GuidedWorld.Usb("Impressora A", extended: 8), GuidedWorld.Usb("Impressora B"));

    [Fact]
    public async Task Com_duas_impressoras_fisicas_pergunta_qual_e_ignora_o_problema_da_outra()
    {
        var kit = new Kit();
        TwoPhysical(kit);
        kit.User.WillChoose("Impressora B");

        await kit.RunAsync();

        Assert.Single(kit.User.Questions);
        // A pausada não entra na análise de B: a causa que sobra é a do padrão virtual.
        Assert.Equal(CauseCodes.DefaultIsVirtual, kit.Events.Of<CauseFound>().Single().Code);
        Assert.DoesNotContain(kit.Events.Of<PrinterStateChanged>(), c => c.Name == "Impressora A" && c.State == PrinterCardState.Problem);
    }

    [Fact]
    public async Task Escolher_a_impressora_com_problema_leva_a_causa_dela()
    {
        var kit = new Kit();
        TwoPhysical(kit);
        kit.User.WillChoose("Impressora A");

        var report = await kit.RunAsync();

        Assert.Contains("Impressora A", kit.Events.Of<CauseFound>().Single().Title);
        Assert.Equal(FlowResult.NotResolved, report.Closing!.Result);
    }

    [Fact]
    public async Task Cancelar_a_escolha_encerra_sem_nada_alterado()
    {
        var kit = new Kit();
        TwoPhysical(kit);
        kit.User.WillChoose((string?)null);

        var report = await kit.RunAsync();

        Assert.Equal(FlowResult.Cancelled, report.Closing!.Result);
        Assert.Empty(report.Actions);
        Assert.Empty(kit.Events.Of<CauseFound>());
    }

    [Fact]
    public async Task Com_uma_impressora_fisica_nao_pergunta_qual()
    {
        var kit = new Kit();
        kit.World.Printers(GuidedWorld.Usb("Impressora X", isDefault: true), GuidedWorld.Pdf());

        await kit.RunAsync();

        Assert.Empty(kit.User.Questions);
    }

    // ---------- sem causa e cartões ----------

    [Fact]
    public async Task Sem_causa_o_fluxo_e_inconclusivo_e_avisa_que_isso_nao_prova_que_funciona()
    {
        var kit = new Kit();

        var report = await kit.RunAsync();

        var none = Assert.Single(kit.Events.Of<NoCauseFound>());
        Assert.Contains("não prova", none.Message);
        Assert.Equal(FlowResult.Inconclusive, report.Closing!.Result);
        Assert.Equal(ClosingBasis.None, report.Closing.Basis);
    }

    [Fact]
    public async Task Cartoes_mudam_de_estado_depois_do_cruzamento_e_a_nao_avaliada_nao_vira_ok()
    {
        var kit = new Kit();
        kit.World.Printers(GuidedWorld.Usb("Impressora X", isDefault: true, extended: 8), GuidedWorld.Pdf());

        await kit.RunAsync();

        var changes = kit.Events.Of<PrinterStateChanged>().ToDictionary(c => c.Name);
        Assert.Equal(PrinterCardState.Problem, changes["Impressora X"].State);
        Assert.Equal("pausada", changes["Impressora X"].StateText);
        Assert.Equal(PrinterCardState.Unknown, changes["Microsoft Print to PDF"].State);
    }

    [Fact]
    public async Task Spooler_parado_marca_a_impressora_avaliada_como_problema()
    {
        var kit = new Kit();
        kit.World.SpoolerIs(ServiceState.Stopped);

        await kit.RunAsync();

        var change = Assert.Single(kit.Events.Of<PrinterStateChanged>());
        Assert.Equal(PrinterCardState.Problem, change.State);
        Assert.Contains("serviço de impressão", change.StateText);
    }

    [Fact]
    public async Task Eventos_chegam_na_ordem_certa_do_comeco_ao_fim()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.Catalog = new Catalog(new FixAction(Kit.ResumeId, kit.FixX));
        kit.User.WillConfirm(true).WillAnswerResolved(ResolutionAnswer.Worked);

        await kit.RunAsync();

        var order = kit.Events.Events.Select(e => e.GetType().Name).Where(n => n is not nameof(Narration) and not nameof(StepStarted) and not nameof(StepFinished)).ToList();
        Assert.Equal(
            [nameof(PrintersFound), nameof(PrinterStateChanged), nameof(CauseFound), nameof(PrinterStateChanged), nameof(ActionFinished), nameof(PrintersFound), nameof(PrinterStateChanged), nameof(FlowClosed)],
            order);
    }

    // ---------- relatório e falhas ----------

    [Fact]
    public async Task Relatorio_carrega_acoes_e_fechamento_e_vai_para_o_json()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.Catalog = new Catalog(new FixAction(Kit.ResumeId, kit.FixX));
        kit.User.WillConfirm(true).WillAnswerResolved(ResolutionAnswer.Worked);

        var report = await kit.RunAsync();
        var json = ReportSerializer.Serialize(report);

        Assert.Equal("1.2", report.SchemaVersion);
        Assert.Contains("\"closing\"", json);
        Assert.Contains("\"basis\": \"UserAnswer\"", json);
        Assert.Contains("Conclusão:", report.Summary);
    }

    [Fact]
    public async Task Cancelamento_externo_vira_relatorio_cancelado()
    {
        var kit = new Kit();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var report = await kit.RunAsync(cts.Token);

        Assert.Equal(ExecutionStatus.Cancelled, report.ExecutionStatus);
        Assert.Null(report.Closing);
    }

    [Fact]
    public async Task Fonte_que_trava_vira_relatorio_de_tempo_esgotado()
    {
        var flow = new GuidedNotPrintingFlow(
            NotPrintingScenario.Create(new HangingSystem(), new FakePrintDeviceProvider()),
            new Catalog(), new RemediationPipeline(new InMemoryAuditLog()), new FakeUserInteraction(),
            collectTimeout: TimeSpan.FromMilliseconds(50));

        var report = await flow.RunAsync(new EventLog());

        Assert.Equal(ExecutionStatus.TimedOut, report.ExecutionStatus);
    }

    private sealed class HangingSystem : IPrintSystemProvider
    {
        private static async Task<SourceResult<T>> Hang<T>(CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return SourceResult<T>.Ok([]);
        }

        public Task<SourceResult<ServiceInfo>> GetSpoolerServiceAsync(CancellationToken ct) => Hang<ServiceInfo>(ct);
        public Task<SourceResult<PrintJobInfo>> GetPrintJobsAsync(CancellationToken ct) => Hang<PrintJobInfo>(ct);
        public Task<SourceResult<PrinterStateInfo>> GetPrinterStatesAsync(CancellationToken ct) => Hang<PrinterStateInfo>(ct);
        public Task<SourceResult<DefaultPrinterModeInfo>> GetDefaultPrinterModeAsync(CancellationToken ct) => Hang<DefaultPrinterModeInfo>(ct);
        public Task<SourceResult<NetworkPortInfo>> GetNetworkPortsAsync(CancellationToken ct) => Hang<NetworkPortInfo>(ct);
    }
}
