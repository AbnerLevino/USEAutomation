using AutomationPlatform.Contracts;
using AutomationPlatform.Host.Desktop.Guided;

namespace AutomationPlatform.Tests;

public class GuidedSpeechTests
{
    [Fact]
    public void Narracao_e_causa_viram_fala_e_eventos_do_quadro_nao()
    {
        Assert.Equal("Olá", GuidedSpeech.For(new Narration("Olá")));
        Assert.Equal("Nada sai.", GuidedSpeech.For(new CauseFound("C", "A fila está pausada", "Nada sai.", "Retome.", "X", null)));
        Assert.Null(GuidedSpeech.For(new StepFinished("a", "A", CheckVerdict.Healthy, "ok")));
        Assert.Null(GuidedSpeech.For(new PrintersFound([])));
        Assert.Null(GuidedSpeech.For(new TechnicalNote("Vou perguntar ao Windows o estado de cada fila.")));
        Assert.Null(GuidedSpeech.For(new FlowClosed(new FlowClosing(FlowResult.Resolved, ClosingBasis.None, "ok"))));
    }

    [Fact]
    public void Confirmacao_n1_nao_traz_aviso_nenhum()
    {
        var text = GuidedSpeech.ConfirmText(new ConfirmationPrompt("a", "Retomar", "Vou retomar a fila.", ActionLevel.N1));

        Assert.Equal("Vou retomar a fila.", text);
    }

    [Fact]
    public void Confirmacao_n2_avisa_que_pode_aparecer_o_pedido_de_administrador()
    {
        var text = GuidedSpeech.ConfirmText(new ConfirmationPrompt("a", "Reiniciar", "Vou reiniciar.", ActionLevel.N2));

        Assert.Contains("administrador", text);
        Assert.Contains("nada será alterado", text);
        Assert.DoesNotContain("apagar", text);
    }

    [Fact]
    public void Confirmacao_n3_avisa_da_perda_de_dados_e_do_administrador()
    {
        var text = GuidedSpeech.ConfirmText(new ConfirmationPrompt("a", "Limpar", "Vou limpar a fila.", ActionLevel.N3));

        Assert.Contains("apagar trabalhos", text);
        Assert.Contains("administrador", text);
    }

    [Theory]
    [InlineData("spooler", "serviço de impressão do computador")]
    [InlineData("state", "como estão as impressoras")]
    [InlineData("queue", "documentos travados")]
    [InlineData("device", "reconhece a impressora")]
    [InlineData("default", "impressora o computador usa")]
    [InlineData("port", "ligação das impressoras")]
    public void Cada_etapa_vira_uma_frase_simples_sem_jargao(string key, string expected)
    {
        var text = GuidedSpeech.For(new StepStarted(key, "rótulo técnico"))!;

        Assert.Contains(expected, text);
        Assert.DoesNotContain("Spooler", text);
        Assert.DoesNotContain("fila", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WMI", text);
    }

    [Fact]
    public void Etapa_desconhecida_usa_o_rotulo_sem_quebrar()
    {
        Assert.Equal("Verificando: Algo novo.", GuidedSpeech.For(new StepStarted("nova", "Algo novo")));
    }

    [Fact]
    public void Causas_sao_ditas_sem_jargao_e_com_o_nome_da_impressora()
    {
        var paused = GuidedSpeech.For(new CauseFound("PRINTER_PAUSED", "A fila 'X' está pausada", "técnico", "g", "Impressora X", null))!;
        var spooler = GuidedSpeech.For(new CauseFound("SPOOLER_NOT_RUNNING", "t", "técnico", "g", null, null))!;

        Assert.Equal("A impressora 'Impressora X' está pausada, por isso nada sai dela.", paused);
        Assert.DoesNotContain("Spooler", spooler);
        Assert.DoesNotContain("fila", paused, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resultado_de_uma_acao_e_dito_em_palavras_simples()
    {
        ActionRecord Rec(ActionStatus status, string? verification = null, string? precondition = null, string? execution = null) => new()
        {
            ActionId = "a", DisplayName = "d", Level = ActionLevel.N1, StartedAtUtc = DateTimeOffset.UtcNow, CompletedAtUtc = DateTimeOffset.UtcNow,
            Status = status, Confirmed = true, Summary = "resumo técnico", VerificationDetail = verification,
            PreconditionDetail = precondition, ExecutionDetail = execution
        };

        Assert.Equal("Pronto! A impressora 'X' não está mais pausada.", GuidedSpeech.For(new ActionFinished(Rec(ActionStatus.Resolved, "a impressora 'X' não está mais pausada."))));
        Assert.Equal("Não deu certo: a impressora continua pausada.", GuidedSpeech.For(new ActionFinished(Rec(ActionStatus.NotResolved, "a impressora continua pausada."))));
        Assert.Equal("Não foi preciso mudar nada: a impressora já não está pausada.", GuidedSpeech.For(new ActionFinished(Rec(ActionStatus.PreconditionFailed, precondition: "a impressora já não está pausada."))));
        Assert.StartsWith("Não consegui fazer isso:", GuidedSpeech.For(new ActionFinished(Rec(ActionStatus.ExecutionFailed, execution: "o Windows recusou.")))!);
        Assert.Contains("administrador", GuidedSpeech.For(new ActionFinished(Rec(ActionStatus.ElevationDenied)))!);
        Assert.Null(GuidedSpeech.For(new ActionFinished(Rec(ActionStatus.NotConfirmed))));
    }
}
