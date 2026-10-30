using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Tests;

public class GuidedEventsTests
{
    private static async Task<EventLog> CollectAsync(GuidedWorld world)
    {
        var log = new EventLog();
        await world.Scenario().AnalyzeAsync(new NullDiagnosticProgress(), CancellationToken.None, log);
        return log;
    }

    [Fact]
    public async Task Cada_uma_das_seis_verificacoes_emite_inicio_e_fim_na_ordem()
    {
        var log = await CollectAsync(new GuidedWorld());

        var steps = log.Events.Where(e => e is StepStarted or StepFinished).ToList();
        Assert.Equal(12, steps.Count);
        var keys = new[] { "spooler", "state", "queue", "device", "default", "port" };
        for (var i = 0; i < keys.Length; i++)
        {
            Assert.Equal(keys[i], Assert.IsType<StepStarted>(steps[i * 2]).Key);
            Assert.Equal(keys[i], Assert.IsType<StepFinished>(steps[i * 2 + 1]).Key);
        }
    }

    [Fact]
    public async Task As_impressoras_aparecem_logo_depois_da_verificacao_de_estado()
    {
        var log = await CollectAsync(new GuidedWorld());

        var stateFinished = log.Events.FindIndex(e => e is StepFinished { Key: "state" });
        var found = log.Events.FindIndex(e => e is PrintersFound);
        Assert.Equal(stateFinished + 1, found);
    }

    [Fact]
    public async Task Cartoes_trazem_nome_tipo_padrao_e_estado_em_linguagem_de_usuario()
    {
        var world = new GuidedWorld().Printers(
            GuidedWorld.Usb("Impressora X", isDefault: true, extended: 8), GuidedWorld.Pdf());

        var log = await CollectAsync(world);

        var cards = log.Of<PrintersFound>().Single().Printers;
        var x = cards.Single(c => c.Name == "Impressora X");
        Assert.False(x.IsVirtual);
        Assert.True(x.IsDefault);
        Assert.Equal(PrinterCardState.Problem, x.State);
        Assert.Equal("pausada", x.StateText);
        Assert.True(cards.Single(c => c.Name == "Microsoft Print to PDF").IsVirtual);
    }

    [Fact]
    public async Task Sem_problema_no_estado_o_cartao_ainda_nao_afirma_que_esta_ok()
    {
        var log = await CollectAsync(new GuidedWorld());

        var card = log.Of<PrintersFound>().Single().Printers.Single();
        Assert.Equal(PrinterCardState.Unknown, card.State);
    }

    [Fact]
    public async Task Sem_consumidor_de_eventos_a_analise_funciona_como_antes()
    {
        var (_, analysis) = await new GuidedWorld().Scenario()
            .AnalyzeAsync(new NullDiagnosticProgress(), CancellationToken.None);

        Assert.Null(analysis.Primary);
    }
}
