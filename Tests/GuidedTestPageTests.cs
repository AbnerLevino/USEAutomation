using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;

namespace AutomationPlatform.Tests;

/// <summary>
/// As três coisas que o teste real apontou: perguntar qual impressora quando há mais de uma física,
/// o quadro mudar durante a correção e a conferência, e oferecer a página de teste.
/// </summary>
public class GuidedTestPageTests
{
    private sealed class Kit
    {
        public GuidedWorld World { get; } = new();
        public FakePrinterControl Control { get; }
        public FakeUserInteraction User { get; } = new();
        public EventLog Events { get; } = new();

        public Kit() { Control = new FakePrinterControl(World); }

        public Task<DiagnosticReport> RunAsync() => new GuidedNotPrintingFlow(
            World.Scenario(),
            new ActionCatalog([new ResumePrinterAction(World.Sys, Control), new PrintTestPageAction(World.Sys, Control)]),
            new RemediationPipeline(new InMemoryAuditLog()), User).RunAsync(Events);

        public void PauseX() => World.Printers(GuidedWorld.Usb("Impressora X", isDefault: true, extended: 8));
    }

    // ---------- 1. qual impressora? ----------

    [Fact]
    public async Task Com_duas_fisicas_pergunta_qual_mesmo_que_o_padrao_seja_fisico()
    {
        var kit = new Kit();
        kit.World.Printers(GuidedWorld.Usb("Impressora X", isDefault: true), GuidedWorld.Usb("Impressora Y"));
        kit.User.WillChoose("Impressora X").WillConfirm(false);

        await kit.RunAsync();

        Assert.Single(kit.User.Questions);
        Assert.Contains("mais de uma impressora", kit.User.Questions[0]);
    }

    [Fact]
    public async Task Escolher_uma_impressora_que_nao_e_a_padrao_avalia_a_escolhida()
    {
        var kit = new Kit();
        kit.World.Printers(
            GuidedWorld.Usb("Impressora X", isDefault: true),
            GuidedWorld.Usb("Impressora Y", extended: 8));
        kit.User.WillChoose("Impressora Y").WillConfirm(false);

        await kit.RunAsync();

        var cause = kit.Events.Of<CauseFound>().Single();
        Assert.Equal(CauseCodes.PrinterPaused, cause.Code);
        Assert.Equal("Impressora Y", cause.PrinterName);
    }

    [Fact]
    public async Task Nao_sei_qual_avalia_todas_as_fisicas_e_acha_o_problema_de_qualquer_uma()
    {
        var kit = new Kit();
        kit.World.Printers(
            GuidedWorld.Usb("Impressora X", isDefault: true),
            GuidedWorld.Usb("Impressora Y", extended: 8),
            GuidedWorld.Pdf());
        kit.User.WillChoose("*").WillConfirm(false);

        await kit.RunAsync();

        Assert.Equal("Impressora Y", kit.Events.Of<CauseFound>().Single().PrinterName);
        var states = kit.Events.Of<PrinterStateChanged>().GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.Last());
        Assert.Equal(PrinterCardState.Ok, states["Impressora X"].State);
        Assert.Equal(PrinterCardState.Unknown, states["Microsoft Print to PDF"].State);
    }

    // ---------- 2. o quadro muda enquanto corrige e confere ----------

    [Fact]
    public async Task Depois_do_sim_o_cartao_mostra_corrigindo_e_depois_o_resultado_da_conferencia()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.User.WillConfirm(true, false).WillAnswerResolved(ResolutionAnswer.Worked);

        await kit.RunAsync();

        var texts = kit.Events.Of<PrinterStateChanged>().Where(c => c.Name == "Impressora X").Select(c => c.StateText).ToList();
        Assert.Equal(["pausada", "corrigindo…", "nada encontrado"], texts);
        Assert.Equal(PrinterCardState.Ok, kit.Events.Of<PrinterStateChanged>().Last(c => c.Name == "Impressora X").State);
    }

    [Fact]
    public async Task A_conferencia_depois_da_correcao_roda_as_seis_verificacoes_de_novo_e_o_usuario_ve()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.User.WillConfirm(true, false).WillAnswerResolved(ResolutionAnswer.Worked);

        await kit.RunAsync();

        Assert.Equal(12, kit.Events.Of<StepFinished>().Count());
        Assert.Contains(kit.Events.Of<Narration>(), n => n.Message.Contains("conferir de novo"));
    }

    [Fact]
    public async Task O_resultado_da_correcao_so_e_dito_depois_da_conferencia_para_nao_ser_empurrado_para_fora_da_tela()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.User.WillConfirm(true, false).WillAnswerResolved(ResolutionAnswer.Worked);

        await kit.RunAsync();

        var events = kit.Events.Events;
        var result = events.FindIndex(e => e is ActionFinished);
        var lastCheckStep = events.FindLastIndex(e => e is StepFinished);
        Assert.True(lastCheckStep < result, "o resultado deve vir depois das verificações da conferência");
        Assert.Single(events.OfType<ActionFinished>());   // dito uma vez só, não duas
    }

    [Fact]
    public async Task Se_o_usuario_recusa_a_correcao_o_cartao_volta_a_mostrar_o_problema()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.User.WillConfirm(false);

        await kit.RunAsync();

        var last = kit.Events.Of<PrinterStateChanged>().Last(c => c.Name == "Impressora X");
        Assert.Equal(PrinterCardState.Problem, last.State);
        Assert.Equal("pausada", last.StateText);
    }

    // ---------- 3. página de teste ----------

    [Fact]
    public async Task Depois_de_consertar_a_automacao_oferece_a_pagina_de_teste_e_o_usuario_so_confirma_que_saiu()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.User.WillConfirm(true, true).WillAnswerResolved(ResolutionAnswer.Worked);

        var report = await kit.RunAsync();

        Assert.Equal(["Impressora X"], kit.Control.TestPages);
        Assert.Contains("A página de teste saiu", kit.User.Questions.Last());
        Assert.Equal(FlowResult.Resolved, report.Closing!.Result);
        Assert.Equal(ClosingBasis.UserAnswer, report.Closing.Basis);
        Assert.Contains("página de teste saiu", report.Closing.Message);
        Assert.Equal([PrinterActionIds.ResumePrinter, PrinterActionIds.PrintTestPage], report.Actions.Select(a => a.ActionId));
    }

    [Fact]
    public async Task Pagina_de_teste_que_nao_saiu_fecha_como_nao_resolvido_mesmo_com_a_fila_retomada()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.User.WillConfirm(true, true).WillAnswerResolved(ResolutionAnswer.StillNotWorking);

        var report = await kit.RunAsync();

        Assert.Equal(FlowResult.NotResolved, report.Closing!.Result);
        Assert.Contains("página de teste não saiu", report.Closing.Message);
    }

    [Fact]
    public async Task Recusar_a_pagina_de_teste_nao_imprime_nada_e_cai_na_pergunta_comum()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.User.WillConfirm(true, false).WillAnswerResolved(ResolutionAnswer.Worked);

        var report = await kit.RunAsync();

        Assert.Empty(kit.Control.TestPages);
        Assert.Contains("A impressão voltou a funcionar", kit.User.Questions.Last());
        Assert.Equal(FlowResult.Resolved, report.Closing!.Result);
        Assert.DoesNotContain(report.Actions, a => a.ActionId == PrinterActionIds.PrintTestPage && a.Status != ActionStatus.NotConfirmed);
    }

    [Fact]
    public async Task Pagina_de_teste_que_o_windows_recusa_cai_na_pergunta_comum_sem_inventar_que_saiu()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.Control.TestPageResult = new ControlResult(false, "o Windows recusou.");
        kit.User.WillConfirm(true, true).WillAnswerResolved(ResolutionAnswer.Worked);

        await kit.RunAsync();

        Assert.Contains("A impressão voltou a funcionar", kit.User.Questions.Last());
    }

    [Fact]
    public async Task Sem_causa_com_uma_impressora_so_a_pagina_de_teste_deixa_o_usuario_conferir()
    {
        var kit = new Kit();
        kit.User.WillConfirm(true).WillAnswerResolved(ResolutionAnswer.Worked);

        var report = await kit.RunAsync();

        Assert.Single(kit.Events.Of<NoCauseFound>());
        Assert.Equal(["Impressora X"], kit.Control.TestPages);
        Assert.Equal(FlowResult.Resolved, report.Closing!.Result);
        Assert.Equal(ClosingBasis.UserAnswer, report.Closing.Basis);
    }

    [Fact]
    public async Task Sem_causa_e_sem_querer_a_pagina_o_resultado_continua_inconclusivo()
    {
        var kit = new Kit();
        kit.User.WillConfirm(false);

        var report = await kit.RunAsync();

        Assert.Equal(FlowResult.Inconclusive, report.Closing!.Result);
        Assert.Empty(kit.Control.TestPages);
    }

    [Fact]
    public async Task Sem_causa_e_nao_sei_qual_nao_oferece_pagina_porque_nao_ha_uma_impressora_so()
    {
        var kit = new Kit();
        kit.World.Printers(GuidedWorld.Usb("Impressora X", isDefault: true), GuidedWorld.Usb("Impressora Y"));
        kit.User.WillChoose("*");

        var report = await kit.RunAsync();

        Assert.Empty(kit.User.Confirmed);
        Assert.Equal(FlowResult.Inconclusive, report.Closing!.Result);
    }

    [Fact]
    public async Task Impressora_virtual_nao_recebe_pagina_de_teste()
    {
        var kit = new Kit();
        kit.World.Printers(GuidedWorld.Pdf(isDefault: true));
        kit.User.WillConfirm(true);

        await kit.RunAsync();

        Assert.Empty(kit.Control.TestPages);
    }

    // ---------- a ação em si ----------

    private static PrintTestPageAction Action(GuidedWorld world, FakePrinterControl control) => new(world.Sys, control);

    [Fact]
    public async Task Acao_so_age_em_impressora_fisica_pronta()
    {
        var world = new GuidedWorld();
        var action = Action(world, new FakePrinterControl(world));

        Assert.True((await action.CheckPreconditionAsync(new ActionTarget("Impressora X"), default)).Met);

        world.Printers(GuidedWorld.Usb("Impressora X", extended: 8));
        Assert.Contains("pausada", (await action.CheckPreconditionAsync(new ActionTarget("Impressora X"), default)).Reason);

        world.Printers(GuidedWorld.Usb("Impressora X", offline: true, extended: 7));
        Assert.Contains("offline", (await action.CheckPreconditionAsync(new ActionTarget("Impressora X"), default)).Reason);

        world.Printers(GuidedWorld.Pdf());
        Assert.Contains("virtual", (await action.CheckPreconditionAsync(new ActionTarget("Microsoft Print to PDF"), default)).Reason);

        Assert.False((await action.CheckPreconditionAsync(new ActionTarget("Fantasma"), default)).Met);
        Assert.False((await action.CheckPreconditionAsync(new ActionTarget(), default)).Met);
    }

    [Fact]
    public async Task Verificacao_nunca_afirma_que_a_pagina_saiu()
    {
        var world = new GuidedWorld();
        var control = new FakePrinterControl(world);
        var action = Action(world, control);

        var exec = await action.ExecuteAsync(new ActionTarget("Impressora X"), default);
        var verify = await action.VerifyAsync(new ActionTarget("Impressora X"), default);

        Assert.Equal(ExecutionOutcome.Done, exec.Outcome);
        Assert.Equal(["Impressora X"], control.TestPages);
        Assert.Equal(VerificationOutcome.Inconclusive, verify.Outcome);
        Assert.Contains("só você pode confirmar", verify.Detail);
    }

    [Fact]
    public void Pergunta_diz_que_gasta_uma_folha_e_e_nivel_n1()
    {
        var action = Action(new GuidedWorld(), new FakePrinterControl(new GuidedWorld()));

        Assert.Equal(ActionLevel.N1, action.Level);
        var text = action.DescribeForConfirmation(new ActionTarget("Impressora X"));
        Assert.Contains("Impressora X", text);
        Assert.Contains("uma folha", text);
    }
}
