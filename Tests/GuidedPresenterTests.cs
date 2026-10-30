using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;
using AutomationPlatform.Host.Desktop.Chat;
using AutomationPlatform.Host.Desktop.Guided;

namespace AutomationPlatform.Tests;

public class GuidedPresenterTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private sealed class Kit
    {
        public GuidedWorld World { get; } = new();
        public FakeGuidedScreen Screen { get; } = new();
        public IActionCatalog Catalog { get; set; } = new ActionCatalog([]);

        public void PauseX() => World.Printers(GuidedWorld.Usb("Impressora X", isDefault: true, extended: 8));
        public void FixX() => World.Printers(GuidedWorld.Usb("Impressora X", isDefault: true));

        public Task<DiagnosticReport> RunAsync(CancellationToken ct = default) =>
            new GuidedPresenter(ChatPacing.Instant)
                .RunAsync(user => new GuidedNotPrintingFlow(World.Scenario(), Catalog, new RemediationPipeline(new InMemoryAuditLog()), user), Screen, ct)
                .WaitAsync(Limit);
    }

    [Fact]
    public async Task Caminho_da_orientacao_mostra_quadro_pergunta_reverifica_fecha_e_conclui_nessa_ordem()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.Screen.OnPhysicalDone = kit.FixX;
        kit.Screen.WillDoPhysical(PhysicalActionReply.Done).WillAnswerResolved(ResolutionAnswer.Worked);

        var report = await kit.RunAsync();

        var log = kit.Screen.Log;
        Assert.Contains("board", log);
        Assert.True(log.IndexOf("board") < log.IndexOf("physical"));
        Assert.True(log.IndexOf("physical") < log.IndexOf("resolved"));
        Assert.Equal("conclusion:Resolvido", log[^1]);
        Assert.Equal(FlowResult.Resolved, report.Closing!.Result);
        Assert.Equal(ChatTone.Good, kit.Screen.Conclusion!.Tone);
    }

    [Fact]
    public async Task A_pergunta_so_aparece_depois_de_tudo_que_veio_antes_dela()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.Screen.WillDoPhysical(PhysicalActionReply.Skipped);

        await kit.RunAsync();

        var log = kit.Screen.Log;
        var question = log.IndexOf("physical");
        var cause = log.FindIndex(l => l.StartsWith("speak:O Windows reporta a fila"));
        var lastStepBoard = log.FindLastIndex(question, l => l == "board");
        Assert.True(cause >= 0 && cause < question, "a causa deve ser falada antes da pergunta");
        Assert.True(lastStepBoard >= 0 && lastStepBoard < question, "o quadro deve estar completo antes da pergunta");
    }

    [Fact]
    public async Task Quadro_cresce_etapa_por_etapa_ate_as_seis_verificacoes()
    {
        var kit = new Kit();

        await kit.RunAsync();

        Assert.Equal(6, kit.Screen.BoardSizes.Max(b => b.Steps));
        Assert.Contains(kit.Screen.BoardSizes, b => b.Printers == 1);
    }

    [Fact]
    public async Task Com_correcao_cadastrada_a_tela_confirma_e_o_fluxo_executa()
    {
        var kit = new Kit();
        kit.PauseX();
        var action = new FixAction(PrinterActionIds.ResumePrinter, kit.FixX);
        kit.Catalog = new ActionCatalog([action]);
        kit.Screen.WillConfirm(true).WillAnswerResolved(ResolutionAnswer.Worked);

        var report = await kit.RunAsync();

        Assert.True(action.Executed);
        Assert.Contains("confirm", kit.Screen.Log);
        Assert.Equal(ActionStatus.Resolved, report.Actions.Single().Status);
        Assert.Equal(ClosingBasis.UserAnswer, report.Closing!.Basis);
    }

    [Fact]
    public async Task Recusar_a_confirmacao_nao_altera_nada_e_conclui_como_nao_resolvido()
    {
        var kit = new Kit();
        kit.PauseX();
        var action = new FixAction(PrinterActionIds.ResumePrinter, kit.FixX);
        kit.Catalog = new ActionCatalog([action]);
        kit.Screen.WillConfirm(false);

        var report = await kit.RunAsync();

        Assert.False(action.Executed);
        Assert.Equal("conclusion:Ainda não está resolvido", kit.Screen.Log[^1]);
        Assert.Equal(FlowResult.NotResolved, report.Closing!.Result);
    }

    [Fact]
    public async Task Escolha_de_impressora_passa_pela_tela()
    {
        var kit = new Kit();
        kit.World.Printers(GuidedWorld.Pdf(isDefault: true), GuidedWorld.Usb("Impressora A", extended: 8), GuidedWorld.Usb("Impressora B"));
        kit.Screen.WillChoose("Impressora A").WillDoPhysical(PhysicalActionReply.Skipped);

        await kit.RunAsync();

        Assert.Contains("choose", kit.Screen.Log);
        Assert.Contains(kit.Screen.Log, l => l.StartsWith("speak:Causa provável: A fila 'Impressora A' está pausada"));
        Assert.Single(kit.Screen.Log, l => l.Contains("'Impressora A' está pausada") && l.StartsWith("speak:Causa provável"));
    }

    [Fact]
    public async Task Cancelar_a_escolha_conclui_como_cancelado()
    {
        var kit = new Kit();
        kit.World.Printers(GuidedWorld.Pdf(isDefault: true), GuidedWorld.Usb("Impressora A"), GuidedWorld.Usb("Impressora B"));
        kit.Screen.WillChoose((string?)null);

        var report = await kit.RunAsync();

        Assert.Equal(FlowResult.Cancelled, report.Closing!.Result);
        Assert.Equal("conclusion:Cancelado: nada foi alterado", kit.Screen.Log[^1]);
    }

    [Fact]
    public async Task Se_a_tela_falha_no_meio_o_fluxo_nao_fica_pendurado_e_o_erro_chega_ao_chamador()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.Screen.FailOnPhysical = () => new InvalidOperationException("a tela quebrou");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => kit.RunAsync());

        Assert.Equal("a tela quebrou", ex.Message);
    }

    [Fact]
    public async Task Fechar_a_janela_enquanto_espera_uma_resposta_cancela_tudo_sem_travar()
    {
        var kit = new Kit();
        kit.PauseX();
        using var cts = new CancellationTokenSource();
        // A tela "demora" para responder: a janela é fechada enquanto a pergunta está aberta.
        kit.Screen.FailOnPhysical = () => { cts.Cancel(); return new OperationCanceledException(cts.Token); };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => kit.RunAsync(cts.Token));
    }
}
