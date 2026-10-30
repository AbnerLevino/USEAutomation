using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;
using AutomationPlatform.Platform.Windows;

namespace AutomationPlatform.Tests;

public class ResumePrinterActionTests
{
    private static readonly ActionTarget X = new("Impressora X");

    private static (GuidedWorld World, FakePrinterControl Control, ResumePrinterAction Action) Setup(bool paused = true, bool takesEffect = true)
    {
        var world = new GuidedWorld();
        if (paused) world.Printers(GuidedWorld.Usb("Impressora X", isDefault: true, extended: 8));
        var control = new FakePrinterControl(world, takesEffect);
        return (world, control, new ResumePrinterAction(world.Sys, control));
    }

    // ---------- contrato ----------

    [Fact]
    public void E_uma_acao_n1_com_o_id_da_lista_e_pergunta_em_linguagem_simples()
    {
        var (_, _, action) = Setup();

        Assert.Equal(PrinterActionIds.ResumePrinter, action.Id);
        Assert.Equal(ActionLevel.N1, action.Level);
        var text = action.DescribeForConfirmation(X);
        Assert.Contains("Impressora X", text);
        Assert.Contains("Nada será apagado", text);
    }

    // ---------- pré-condição ----------

    [Fact]
    public async Task Pre_condicao_vale_quando_a_fila_esta_realmente_pausada()
    {
        var (_, _, action) = Setup();

        Assert.True((await action.CheckPreconditionAsync(X, default)).Met);
    }

    [Fact]
    public async Task Pre_condicao_falha_se_a_fila_ja_nao_esta_pausada()
    {
        var (_, _, action) = Setup(paused: false);

        var result = await action.CheckPreconditionAsync(X, default);

        Assert.False(result.Met);
        Assert.Contains("já não está pausada", result.Reason);
    }

    [Fact]
    public async Task Pre_condicao_falha_se_a_impressora_nao_existe_mais()
    {
        var (_, _, action) = Setup();

        var result = await action.CheckPreconditionAsync(new ActionTarget("Fantasma"), default);

        Assert.False(result.Met);
        Assert.Contains("não encontrei", result.Reason);
    }

    [Fact]
    public async Task Pre_condicao_falha_se_nao_da_para_ler_o_estado_e_nao_presume()
    {
        var (world, _, action) = Setup();
        world.Sys.PrinterStates = SourceResult<PrinterStateInfo>.Fail(SourceStatus.PermissionDenied, "negado");

        Assert.False((await action.CheckPreconditionAsync(X, default)).Met);
    }

    [Fact]
    public async Task Sem_nome_de_impressora_nao_age()
    {
        var (_, _, action) = Setup();

        Assert.False((await action.CheckPreconditionAsync(new ActionTarget(), default)).Met);
    }

    // ---------- execução ----------

    [Fact]
    public async Task Execucao_manda_retomar_exatamente_a_fila_alvo()
    {
        var (_, control, action) = Setup();

        var result = await action.ExecuteAsync(X, default);

        Assert.Equal(ExecutionOutcome.Done, result.Outcome);
        Assert.Equal(["Impressora X"], control.Resumed);
    }

    [Fact]
    public async Task Execucao_que_o_windows_recusa_vira_falha_com_explicacao()
    {
        var (_, control, action) = Setup();
        control.Result = new ControlResult(false, "código 5", AccessDenied: true);

        var result = await action.ExecuteAsync(X, default);

        Assert.Equal(ExecutionOutcome.Failed, result.Outcome);
        Assert.Contains("administrador", result.Detail);
    }

    // ---------- verificação posterior ----------

    [Fact]
    public async Task Verificacao_le_o_windows_de_novo_e_so_diz_resolvido_se_saiu_da_pausa()
    {
        var (_, _, action) = Setup();
        await action.ExecuteAsync(X, default);

        Assert.Equal(VerificationOutcome.Resolved, (await action.VerifyAsync(X, default)).Outcome);
    }

    [Fact]
    public async Task Ordem_que_o_windows_aceitou_mas_nao_pegou_nao_e_dada_como_resolvida()
    {
        var (_, _, action) = Setup(takesEffect: false);
        var exec = await action.ExecuteAsync(X, default);

        Assert.Equal(ExecutionOutcome.Done, exec.Outcome);
        Assert.Equal(VerificationOutcome.NotResolved, (await action.VerifyAsync(X, default)).Outcome);
    }

    [Fact]
    public async Task Verificacao_que_nao_consegue_ler_e_inconclusiva()
    {
        var (world, _, action) = Setup();
        await action.ExecuteAsync(X, default);
        world.Sys.PrinterStates = SourceResult<PrinterStateInfo>.Fail(SourceStatus.Unavailable, "fora do ar");

        Assert.Equal(VerificationOutcome.Inconclusive, (await action.VerifyAsync(X, default)).Outcome);
    }

    // ---------- pelo fluxo guiado inteiro ----------

    private sealed class Run(GuidedWorld world, bool takesEffect)
    {
        public GuidedWorld World { get; } = world;
        public FakePrinterControl Control { get; } = new(world, takesEffect);
        public FakeUserInteraction User { get; } = new();

        public Task<DiagnosticReport> GoAsync()
        {
            var action = new ResumePrinterAction(World.Sys, Control);
            return new GuidedNotPrintingFlow(World.Scenario(), new ActionCatalog([action]),
                new RemediationPipeline(new InMemoryAuditLog()), User).RunAsync(new EventLog());
        }
    }

    private static Run PausedRun(bool takesEffect = true)
    {
        var world = new GuidedWorld();
        world.Printers(GuidedWorld.Usb("Impressora X", isDefault: true, extended: 8));
        return new Run(world, takesEffect);
    }

    [Fact]
    public async Task Fluxo_completo_a_automacao_pergunta_retoma_verifica_e_o_usuario_so_confirma()
    {
        var run = PausedRun();
        run.User.WillConfirm(true).WillAnswerResolved(ResolutionAnswer.Worked);

        var report = await run.GoAsync();

        Assert.Equal(["Impressora X"], run.Control.Resumed);
        Assert.Empty(run.User.PhysicalRequests);
        Assert.Equal(ActionStatus.Resolved, report.Actions.Single().Status);
        Assert.Equal(FlowResult.Resolved, report.Closing!.Result);
        Assert.Equal(ClosingBasis.UserAnswer, report.Closing.Basis);
    }

    [Fact]
    public async Task Fluxo_completo_se_o_usuario_nao_confirma_a_fila_continua_pausada()
    {
        var run = PausedRun();
        run.User.WillConfirm(false);

        var report = await run.GoAsync();

        Assert.Empty(run.Control.Resumed);
        Assert.Equal(FlowResult.NotResolved, report.Closing!.Result);
    }

    [Fact]
    public async Task Fluxo_completo_se_a_ordem_nao_pega_o_relatorio_diz_que_nao_resolveu_mesmo_com_usuario_sem_testar()
    {
        var run = PausedRun(takesEffect: false);
        run.User.WillConfirm(true).WillAnswerResolved(ResolutionAnswer.CannotTest);

        var report = await run.GoAsync();

        Assert.Equal(ActionStatus.NotResolved, report.Actions.Single().Status);
        Assert.Equal(FlowResult.NotResolved, report.Closing!.Result);
    }

    // ---------- demonstração e segurança do WQL ----------

    [Fact]
    public async Task No_modo_demo_retomar_so_muda_os_dados_falsos()
    {
        var demo = DemoPrintingProvider.Create("pausada")!;
        var action = new ResumePrinterAction(demo, demo);
        var target = new ActionTarget("HP LaserJet (demo)");

        Assert.True((await action.CheckPreconditionAsync(target, default)).Met);
        await action.ExecuteAsync(target, default);

        Assert.Equal(VerificationOutcome.Resolved, (await action.VerifyAsync(target, default)).Outcome);
        Assert.False((await action.CheckPreconditionAsync(target, default)).Met);
    }

    [Fact]
    public async Task No_modo_demo_fila_inexistente_falha_sem_inventar()
    {
        var demo = DemoPrintingProvider.Create("pausada")!;

        var result = await demo.ResumeAsync("Fantasma", default);

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("Impressora X", "Impressora X")]
    [InlineData(@"\\servidor\fila", @"\\\\servidor\\fila")]
    [InlineData("Fila' OR Name LIKE '%", @"Fila\' OR Name LIKE \'%")]
    public void Nome_da_fila_e_escapado_antes_de_entrar_na_consulta_WQL(string name, string expected)
    {
        Assert.Equal(expected, WmiPrinterControl.Escape(name));
    }
}
