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
        Assert.Null(GuidedSpeech.For(new StepStarted("a", "A")));
        Assert.Null(GuidedSpeech.For(new PrintersFound([])));
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
}
