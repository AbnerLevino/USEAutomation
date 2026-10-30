using AutomationPlatform.Contracts;
using AutomationPlatform.Host.Desktop.Guided;

namespace AutomationPlatform.Tests;

public class GuidedBoardTests
{
    private static PrinterCard Card(string name, PrinterCardState state = PrinterCardState.Unknown, string text = "ainda verificando") =>
        new(name, IsVirtual: false, IsDefault: true, state, text);

    [Fact]
    public void Etapa_comeca_como_verificando_e_termina_com_o_veredito()
    {
        var board = new GuidedBoard();

        Assert.True(board.Apply(new StepStarted("spooler", "Serviço de impressão")));
        Assert.Equal(StepStatus.Running, board.Steps.Single().Status);

        Assert.True(board.Apply(new StepFinished("spooler", "Serviço de impressão", CheckVerdict.Problem, "parado")));
        var step = board.Steps.Single();
        Assert.Equal(StepStatus.Problem, step.Status);
        Assert.Equal("parado", step.Summary);
    }

    [Theory]
    [InlineData(CheckVerdict.Healthy, StepStatus.Healthy)]
    [InlineData(CheckVerdict.Problem, StepStatus.Problem)]
    [InlineData(CheckVerdict.NotEvaluable, StepStatus.NotEvaluable)]
    public void Veredito_vira_o_status_da_etapa(CheckVerdict verdict, StepStatus expected)
    {
        var board = new GuidedBoard();
        board.Apply(new StepStarted("a", "A"));
        board.Apply(new StepFinished("a", "A", verdict, "x"));

        Assert.Equal(expected, board.Steps.Single().Status);
    }

    [Fact]
    public void Etapa_repetida_recomeca_a_lista_numa_nova_rodada_de_verificacao()
    {
        var board = new GuidedBoard();
        board.Apply(new StepStarted("a", "A"));
        board.Apply(new StepFinished("a", "A", CheckVerdict.Problem, "x"));
        board.Apply(new StepStarted("b", "B"));

        board.Apply(new StepStarted("a", "A"));

        Assert.Equal(["a"], board.Steps.Select(s => s.Key));
        Assert.Equal(StepStatus.Running, board.Steps.Single().Status);
    }

    [Fact]
    public void Impressoras_achadas_substituem_as_anteriores()
    {
        var board = new GuidedBoard();
        board.Apply(new PrintersFound([Card("A"), Card("B")]));
        board.Apply(new PrintersFound([Card("C")]));

        Assert.Equal(["C"], board.Printers.Select(p => p.Name));
    }

    [Fact]
    public void Mudanca_de_estado_acha_a_impressora_sem_diferenciar_maiusculas()
    {
        var board = new GuidedBoard();
        board.Apply(new PrintersFound([Card("Impressora X")]));

        Assert.True(board.Apply(new PrinterStateChanged("IMPRESSORA x", PrinterCardState.Problem, "pausada")));

        var printer = board.Printers.Single();
        Assert.Equal(PrinterCardState.Problem, printer.State);
        Assert.Equal("pausada", printer.StateText);
        Assert.Equal("Impressora X", printer.Name);
    }

    [Fact]
    public void Mudanca_de_uma_impressora_desconhecida_nao_altera_o_quadro()
    {
        var board = new GuidedBoard();

        Assert.False(board.Apply(new PrinterStateChanged("Fantasma", PrinterCardState.Ok, "ok")));
        Assert.Empty(board.Printers);
    }

    [Fact]
    public void Eventos_que_nao_sao_do_quadro_nao_pedem_redesenho()
    {
        var board = new GuidedBoard();

        Assert.False(board.Apply(new Narration("oi")));
        Assert.False(board.Apply(new NoCauseFound("nada")));
    }
}
