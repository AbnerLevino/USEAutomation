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
        public InMemoryReportSink Sink { get; } = new();
        public IActionCatalog Catalog { get; set; } = new ActionCatalog([]);

        public void PauseX() => World.Printers(GuidedWorld.Usb("Impressora X", isDefault: true, extended: 8));
        public void JamX() => World.Printers(GuidedWorld.Usb("Impressora X", isDefault: true, error: 8));
        public void FixX() => World.Printers(GuidedWorld.Usb("Impressora X", isDefault: true));

        public Task<DiagnosticReport> RunAsync(CancellationToken ct = default) =>
            new GuidedPresenter(ChatPacing.Instant, Sink)
                .RunAsync(user => new GuidedNotPrintingFlow(World.Scenario(), Catalog, new RemediationPipeline(new InMemoryAuditLog()), user), Screen, ct)
                .WaitAsync(Limit);
    }

    [Fact]
    public async Task Caminho_da_orientacao_mostra_quadro_pergunta_reverifica_fecha_e_conclui_nessa_ordem()
    {
        var kit = new Kit();
        kit.JamX();
        kit.Screen.OnPhysicalDone = kit.FixX;
        kit.Screen.WillDoPhysical(PhysicalActionReply.Done).WillAnswerResolved(ResolutionAnswer.Worked);

        var report = await kit.RunAsync();

        var log = kit.Screen.Log;
        Assert.Contains("board", log);
        Assert.True(log.IndexOf("board") < log.IndexOf("physical"));
        Assert.True(log.IndexOf("physical") < log.IndexOf("resolved"));
        Assert.Equal("conclusion:Resolvido", log.Last(l => l.StartsWith("conclusion:")));
        Assert.Equal(["feedback", "closed"], log.Skip(log.FindIndex(l => l.StartsWith("conclusion:")) + 1));
        Assert.Equal(FlowResult.Resolved, report.Closing!.Result);
        Assert.Equal(ChatTone.Good, kit.Screen.Conclusion!.Tone);
    }

    [Fact]
    public async Task A_pergunta_so_aparece_depois_de_tudo_que_veio_antes_dela()
    {
        var kit = new Kit();
        kit.JamX();
        kit.Screen.WillDoPhysical(PhysicalActionReply.Skipped);

        await kit.RunAsync();

        var log = kit.Screen.Log;
        var question = log.IndexOf("physical");
        var cause = log.FindIndex(l => l.StartsWith("speak:A impressora 'Impressora X' está avisando"));
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
        Assert.Equal("conclusion:Ainda não está resolvido", kit.Screen.Log.Last(l => l.StartsWith("conclusion:")));
        Assert.Equal(FlowResult.NotResolved, report.Closing!.Result);
    }

    // ---------- o fim do atendimento ----------

    [Fact]
    public async Task Depois_da_conclusao_pede_a_avaliacao_entrega_o_relatorio_e_encerra_nessa_ordem()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.Screen.Feedback = ServiceFeedback.Create(4, "  Foi rápido  ");

        var report = await kit.RunAsync();

        var log = kit.Screen.Log;
        var conclusion = log.FindIndex(l => l.StartsWith("conclusion:"));
        Assert.True(conclusion < log.IndexOf("feedback"));
        Assert.True(log.IndexOf("feedback") < log.IndexOf("closed"));
        Assert.Equal("closed", log[^1]);

        var delivered = Assert.Single(kit.Sink.Reports);
        Assert.Equal(4, delivered.Feedback!.Rating);
        Assert.Equal("Foi rápido", delivered.Feedback.Comment);
        Assert.Equal(delivered, report);
    }

    [Fact]
    public async Task Quem_nao_avalia_nem_comenta_tem_o_relatorio_entregue_sem_avaliacao()
    {
        var kit = new Kit();
        kit.PauseX();

        await kit.RunAsync();

        Assert.True(kit.Screen.FeedbackAsked);
        Assert.Null(Assert.Single(kit.Sink.Reports).Feedback);
    }

    [Fact]
    public async Task So_a_nota_ou_so_o_comentario_ja_contam_como_avaliacao()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.Screen.Feedback = ServiceFeedback.Create(null, "Obrigado");

        await kit.RunAsync();

        var feedback = Assert.Single(kit.Sink.Reports).Feedback!;
        Assert.Null(feedback.Rating);
        Assert.Equal("Obrigado", feedback.Comment);
    }

    [Fact]
    public async Task Atendimento_cancelado_nao_pede_avaliacao_mas_o_relatorio_e_entregue_e_o_atendimento_encerra()
    {
        var kit = new Kit();
        kit.World.Printers(GuidedWorld.Pdf(isDefault: true), GuidedWorld.Usb("Impressora A"), GuidedWorld.Usb("Impressora B"));
        kit.Screen.WillChoose((string?)null);

        await kit.RunAsync();

        Assert.False(kit.Screen.FeedbackAsked);
        Assert.Equal(FlowResult.Cancelled, Assert.Single(kit.Sink.Reports).Closing!.Result);
        Assert.Equal("closed", kit.Screen.Log[^1]);
    }

    [Fact]
    public async Task Se_a_tela_falha_antes_do_fim_nada_e_entregue_como_se_o_atendimento_tivesse_terminado()
    {
        var kit = new Kit();
        kit.JamX();
        kit.Screen.FailOnPhysical = () => new InvalidOperationException("a tela quebrou");

        await Assert.ThrowsAsync<InvalidOperationException>(() => kit.RunAsync());

        Assert.Empty(kit.Sink.Reports);
    }

    [Fact]
    public void Avaliacao_e_normalizada_nota_fora_de_1_a_5_vira_vazia_e_comentario_e_aparado()
    {
        Assert.Null(ServiceFeedback.Create(0, null).Rating);
        Assert.Null(ServiceFeedback.Create(6, null).Rating);
        Assert.Equal(5, ServiceFeedback.Create(5, null).Rating);
        Assert.Null(ServiceFeedback.Create(3, "   ").Comment);
        Assert.Equal("oi", ServiceFeedback.Create(3, "  oi \n").Comment);
        Assert.Equal(ServiceFeedback.MaxCommentLength, ServiceFeedback.Create(3, new string('a', 900)).Comment!.Length);
        Assert.True(ServiceFeedback.Create(null, " ").IsEmpty);
        Assert.False(ServiceFeedback.Create(1, null).IsEmpty);
    }

    [Fact]
    public async Task Avaliacao_vai_para_o_json_do_relatorio()
    {
        var kit = new Kit();
        kit.PauseX();
        kit.Screen.Feedback = ServiceFeedback.Create(5, "Ótimo");

        var json = ReportSerializer.Serialize(await kit.RunAsync());
        var back = ReportSerializer.Deserialize(json);

        Assert.Equal("1.3", back.SchemaVersion);
        Assert.Equal(5, back.Feedback!.Rating);
        Assert.Equal("Ótimo", back.Feedback.Comment);
    }

    [Fact]
    public async Task Causa_que_a_automacao_nao_corrige_vai_para_o_suporte_sem_nenhuma_pergunta_ao_usuario()
    {
        var kit = new Kit();
        kit.PauseX();   // catálogo vazio: sabe corrigir, mas ainda não está disponível

        var report = await kit.RunAsync();

        Assert.DoesNotContain(kit.Screen.Log, l => l is "confirm" or "physical" or "choose" or "resolved");
        Assert.Contains(kit.Screen.Log, l => l.StartsWith("speak:Encontrei o problema"));
        Assert.Equal("conclusion:Ainda não está resolvido", kit.Screen.Log.Last(l => l.StartsWith("conclusion:")));
        Assert.Equal(FlowResult.NotResolved, report.Closing!.Result);
    }

    [Fact]
    public async Task Escolha_de_impressora_passa_pela_tela()
    {
        var kit = new Kit();
        kit.World.Printers(GuidedWorld.Pdf(isDefault: true), GuidedWorld.Usb("Impressora A", extended: 8), GuidedWorld.Usb("Impressora B"));
        kit.Screen.WillChoose("Impressora A");

        await kit.RunAsync();

        Assert.Contains("choose", kit.Screen.Log);
        Assert.Contains(kit.Screen.Log, l => l.StartsWith("speak:A impressora 'Impressora A' está pausada, por isso nada sai dela."));
        Assert.Single(kit.Screen.Log, l => l.StartsWith("speak:A impressora 'Impressora A' está pausada"));
    }

    [Fact]
    public async Task Cancelar_a_escolha_conclui_como_cancelado()
    {
        var kit = new Kit();
        kit.World.Printers(GuidedWorld.Pdf(isDefault: true), GuidedWorld.Usb("Impressora A"), GuidedWorld.Usb("Impressora B"));
        kit.Screen.WillChoose((string?)null);

        var report = await kit.RunAsync();

        Assert.Equal(FlowResult.Cancelled, report.Closing!.Result);
        Assert.Equal("conclusion:Cancelado: nada foi alterado", kit.Screen.Log.Last(l => l.StartsWith("conclusion:")));
    }

    [Fact]
    public async Task Se_a_tela_falha_no_meio_o_fluxo_nao_fica_pendurado_e_o_erro_chega_ao_chamador()
    {
        var kit = new Kit();
        kit.JamX();
        kit.Screen.FailOnPhysical = () => new InvalidOperationException("a tela quebrou");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => kit.RunAsync());

        Assert.Equal("a tela quebrou", ex.Message);
    }

    [Fact]
    public async Task Fechar_a_janela_enquanto_espera_uma_resposta_cancela_tudo_sem_travar()
    {
        var kit = new Kit();
        kit.JamX();
        using var cts = new CancellationTokenSource();
        // A tela "demora" para responder: a janela é fechada enquanto a pergunta está aberta.
        kit.Screen.FailOnPhysical = () => { cts.Cancel(); return new OperationCanceledException(cts.Token); };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => kit.RunAsync(cts.Token));
    }
}
