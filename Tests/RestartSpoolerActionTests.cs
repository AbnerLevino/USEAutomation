using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;
using AutomationPlatform.Platform.Windows;

namespace AutomationPlatform.Tests;

public class RestartSpoolerActionTests
{
    /// <summary>Executor elevado falso: devolve as respostas combinadas, na ordem, e "conserta" o computador falso quando dá certo.</summary>
    private sealed class FakeElevated(GuidedWorld world, params ExecutionResult[] answers) : IElevatedExecutor
    {
        private readonly Queue<ExecutionResult> _answers = new(answers);
        public List<(string Id, ActionTarget Target)> Calls { get; } = [];

        public Task<ExecutionResult> RunAsync(string actionId, ActionTarget target, CancellationToken cancellationToken)
        {
            Calls.Add((actionId, target));
            var answer = _answers.Count > 0 ? _answers.Dequeue() : new ExecutionResult(ExecutionOutcome.Done);
            if (answer.Outcome == ExecutionOutcome.Done) world.SpoolerIs(ServiceState.Running);
            return Task.FromResult(answer);
        }
    }

    private static readonly ExecutionResult Done = new(ExecutionOutcome.Done);
    private static readonly ExecutionResult Denied = new(ExecutionOutcome.ElevationDenied, "o administrador não aprovou o aviso do Windows.");

    private static GuidedWorld StoppedWorld() => new GuidedWorld().SpoolerIs(ServiceState.Stopped);

    // ---------- a ação ----------

    [Fact]
    public void E_n2_pede_confirmacao_em_linguagem_simples_e_promete_nao_apagar_nada()
    {
        var action = new RestartSpoolerAction(StoppedWorld().Sys, new FakeElevated(StoppedWorld()));

        Assert.Equal(ActionLevel.N2, action.Level);
        Assert.Equal(PrinterActionIds.RestartSpooler, action.Id);
        var text = action.DescribeForConfirmation(new ActionTarget());
        Assert.Contains("Posso reiniciá-lo", text);
        Assert.Contains("Não vou apagar nenhum documento", text);
        Assert.DoesNotContain("Spooler", text);
    }

    [Fact]
    public async Task Pre_condicao_vale_com_o_servico_parado()
    {
        var world = StoppedWorld();
        var action = new RestartSpoolerAction(world.Sys, new FakeElevated(world));

        Assert.True((await action.CheckPreconditionAsync(new ActionTarget(), default)).Met);
    }

    [Fact]
    public async Task Pre_condicao_falha_se_o_servico_ja_esta_funcionando()
    {
        var world = new GuidedWorld();
        var action = new RestartSpoolerAction(world.Sys, new FakeElevated(world));

        var result = await action.CheckPreconditionAsync(new ActionTarget(), default);

        Assert.False(result.Met);
        Assert.Contains("já está funcionando", result.Reason);
    }

    [Fact]
    public async Task Pre_condicao_falha_se_nao_da_para_ler_o_servico_e_nao_presume()
    {
        var world = StoppedWorld();
        world.Sys.Spooler = SourceResult<ServiceInfo>.Fail(SourceStatus.PermissionDenied, "negado");
        var action = new RestartSpoolerAction(world.Sys, new FakeElevated(world));

        Assert.False((await action.CheckPreconditionAsync(new ActionTarget(), default)).Met);
    }

    [Fact]
    public async Task Execucao_pede_ao_executor_elevado_exatamente_esta_acao()
    {
        var world = StoppedWorld();
        var elevated = new FakeElevated(world);
        var action = new RestartSpoolerAction(world.Sys, elevated);

        var result = await action.ExecuteAsync(new ActionTarget(), default);

        Assert.Equal(ExecutionOutcome.Done, result.Outcome);
        Assert.Equal(PrinterActionIds.RestartSpooler, Assert.Single(elevated.Calls).Id);
    }

    [Fact]
    public async Task Verificacao_le_o_servico_de_novo_e_so_diz_resolvido_se_ele_esta_funcionando()
    {
        var world = StoppedWorld();
        var action = new RestartSpoolerAction(world.Sys, new FakeElevated(world));
        await action.ExecuteAsync(new ActionTarget(), default);

        Assert.Equal(VerificationOutcome.Resolved, (await action.VerifyAsync(new ActionTarget(), default)).Outcome);
    }

    [Fact]
    public async Task Executor_que_disse_sucesso_mas_o_servico_continua_parado_nao_e_dado_como_resolvido()
    {
        var world = StoppedWorld();
        var action = new RestartSpoolerAction(world.Sys, new FakeElevated(world));

        // Nada executou de fato (o computador falso continua com o serviço parado).
        Assert.Equal(VerificationOutcome.NotResolved, (await action.VerifyAsync(new ActionTarget(), default)).Outcome);
    }

    [Fact]
    public async Task Verificacao_que_nao_consegue_ler_e_inconclusiva()
    {
        var world = StoppedWorld();
        var action = new RestartSpoolerAction(world.Sys, new FakeElevated(world));
        world.Sys.Spooler = SourceResult<ServiceInfo>.Fail(SourceStatus.Unavailable, "fora do ar");

        Assert.Equal(VerificationOutcome.Inconclusive, (await action.VerifyAsync(new ActionTarget(), default)).Outcome);
    }

    // ---------- o fluxo guiado inteiro ----------

    private sealed class Run
    {
        public GuidedWorld World { get; } = StoppedWorld();
        public FakeUserInteraction User { get; } = new();
        public FakeElevated Elevated { get; }
        public InMemoryAuditLog Audit { get; } = new();

        public Run(params ExecutionResult[] answers) { Elevated = new FakeElevated(World, answers); }

        public Task<DiagnosticReport> GoAsync() => new GuidedNotPrintingFlow(
            World.Scenario(), new ActionCatalog([new RestartSpoolerAction(World.Sys, Elevated)]),
            new RemediationPipeline(Audit), User).RunAsync(new EventLog());
    }

    [Fact]
    public async Task Fluxo_completo_o_usuario_confirma_o_administrador_aprova_e_o_servico_volta()
    {
        var run = new Run(Done);
        run.User.WillConfirm(true).WillAnswerResolved(ResolutionAnswer.Worked);

        var report = await run.GoAsync();

        var prompt = Assert.Single(run.User.Confirmed);
        Assert.True(prompt.MayNeedAdministrator);   // a tela avisa que pode aparecer o pedido de administrador
        Assert.Equal(ActionLevel.N2, prompt.Level);
        Assert.Equal(ActionStatus.Resolved, Assert.Single(report.Actions).Status);
        Assert.Equal(FlowResult.Resolved, report.Closing!.Result);
        Assert.Empty(run.User.PhysicalRequests);
    }

    [Fact]
    public async Task Administrador_recusa_e_o_usuario_nao_quer_tentar_de_novo_nada_e_alterado()
    {
        var run = new Run(Denied);
        run.User.WillConfirm(true, false);

        var report = await run.GoAsync();

        var record = Assert.Single(report.Actions);
        Assert.Equal(ActionStatus.ElevationDenied, record.Status);
        Assert.Contains("Nada foi alterado", record.Summary);
        Assert.Equal(FlowResult.NotResolved, report.Closing!.Result);
        Assert.Contains("administrador", report.Closing.Message);
        Assert.Empty(run.User.Questions);   // sem fechamento de "voltou a imprimir": nada foi feito
        Assert.Equal(ServiceState.Stopped, run.World.Sys.Spooler.Items.Single().State);
    }

    [Fact]
    public async Task Administrador_recusa_o_usuario_tenta_de_novo_e_desta_vez_aprova()
    {
        var run = new Run(Denied, Done);
        run.User.WillConfirm(true, true).WillAnswerResolved(ResolutionAnswer.Worked);

        var report = await run.GoAsync();

        Assert.Equal([ActionStatus.ElevationDenied, ActionStatus.Resolved], report.Actions.Select(a => a.Status));
        Assert.Equal(2, run.Elevated.Calls.Count);
        Assert.Equal(FlowResult.Resolved, report.Closing!.Result);
        // A pergunta "posso reiniciar?" foi feita UMA vez; a segunda tentativa só perguntou se queria tentar de novo.
        Assert.Equal(2, run.User.Confirmed.Count);
        Assert.Equal("retry-elevation", run.User.Confirmed[1].ActionId);
    }

    [Fact]
    public async Task Recusas_seguidas_podem_ser_repetidas_ate_o_usuario_desistir()
    {
        var run = new Run(Denied, Denied, Denied);
        run.User.WillConfirm(true, true, true, false);

        var report = await run.GoAsync();

        Assert.Equal(3, run.Elevated.Calls.Count);
        Assert.All(report.Actions, a => Assert.Equal(ActionStatus.ElevationDenied, a.Status));
        Assert.Equal(FlowResult.NotResolved, report.Closing!.Result);
    }

    [Fact]
    public async Task Toda_tentativa_vai_para_a_auditoria_inclusive_a_recusada()
    {
        var run = new Run(Denied, Done);
        run.User.WillConfirm(true, true).WillAnswerResolved(ResolutionAnswer.Worked);

        await run.GoAsync();

        Assert.Equal([ActionStatus.ElevationDenied, ActionStatus.Resolved], run.Audit.Records.Select(r => r.Status));
    }

    [Fact]
    public async Task Executor_que_se_recusa_nao_oferece_tentar_de_novo_porque_nao_foi_o_aviso()
    {
        var run = new Run(new ExecutionResult(ExecutionOutcome.Refused, "o executor recusou o pedido."));
        run.User.WillConfirm(true);

        var report = await run.GoAsync();

        Assert.Equal(ActionStatus.ExecutionFailed, Assert.Single(report.Actions).Status);
        Assert.Single(run.User.Confirmed);
    }

    [Fact]
    public async Task Usuario_que_nao_confirma_nunca_chega_a_pedir_administrador()
    {
        var run = new Run(Done);
        run.User.WillConfirm(false);

        await run.GoAsync();

        Assert.Empty(run.Elevated.Calls);
    }

    // ---------- demonstração ----------

    [Fact]
    public async Task No_modo_demo_a_aprovacao_e_simulada_e_so_muda_os_dados_falsos()
    {
        var demo = DemoPrintingProvider.Create("spooler-parado")!;
        var action = new RestartSpoolerAction(demo, demo);

        Assert.True((await action.CheckPreconditionAsync(new ActionTarget(), default)).Met);
        Assert.Equal(ExecutionOutcome.Done, (await action.ExecuteAsync(new ActionTarget(), default)).Outcome);
        Assert.Equal(VerificationOutcome.Resolved, (await action.VerifyAsync(new ActionTarget(), default)).Outcome);
    }

    [Fact]
    public async Task No_modo_demo_so_a_acao_da_lista_e_aceita()
    {
        var demo = DemoPrintingProvider.Create("spooler-parado")!;

        var result = await demo.RunAsync("system.format", new ActionTarget(), default);

        Assert.Equal(ExecutionOutcome.Refused, result.Outcome);
    }
}
