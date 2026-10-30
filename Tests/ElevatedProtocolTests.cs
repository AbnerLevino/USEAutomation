using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Platform.Windows.Elevation;

namespace AutomationPlatform.Tests;

public class ElevatedProtocolTests
{
    // ---------- a lista fixa ----------

    [Fact]
    public void A_lista_fixa_tem_so_o_que_foi_aprovado()
    {
        // Se alguém acrescentar uma ação, este teste lembra de que ela precisa de manipulador, validação e teste próprios.
        Assert.Equal([PrinterActionIds.RestartSpooler], ElevatedProtocol.AllowedActions);
    }

    // ---------- leitura do pedido ----------

    [Fact]
    public void Pedido_valido_e_lido()
    {
        var request = ElevatedProtocol.Parse(["--action", "printing.spooler.restart"]);

        Assert.Equal(new ElevatedRequest("printing.spooler.restart", null), request);
    }

    [Fact]
    public void Pedido_com_impressora_e_lido()
    {
        var request = ElevatedProtocol.Parse(["--action", "printing.printer.resume", "--printer", "Impressora X"]);

        Assert.Equal("Impressora X", request!.Printer);
    }

    [Fact]
    public void A_ordem_dos_argumentos_nao_importa()
    {
        var request = ElevatedProtocol.Parse(["--printer", "X", "--action", "printing.printer.resume"]);

        Assert.Equal("printing.printer.resume", request!.ActionId);
    }

    [Theory]
    [InlineData]                                                                       // nada
    [InlineData("--action")]                                                           // sem valor
    [InlineData("printing.spooler.restart")]                                           // sem a flag
    [InlineData("--action", "printing.spooler.restart", "--extra")]                    // sobrou argumento
    [InlineData("--action", "printing.spooler.restart", "--extra", "x")]               // flag desconhecida
    [InlineData("--action", "a.b", "--action", "c.d")]                                 // flag repetida
    [InlineData("--printer", "X")]                                                     // sem ação
    [InlineData("--action", "Printing.Spooler.Restart")]                               // maiúsculas
    [InlineData("--action", "printing.spooler.restart & calc")]                        // injeção
    [InlineData("--action", "printing.spooler.restart; del *")]
    [InlineData("--action", "printing/spooler")]
    [InlineData("--action", "..\\evil")]
    [InlineData("--action", ".printing")]
    [InlineData("--action", "printing.")]
    [InlineData("--action", "printing..restart")]
    [InlineData("--action", "")]
    public void Pedido_mal_formado_e_recusado(params string[] args)
    {
        Assert.Null(ElevatedProtocol.Parse(args));
    }

    [Fact]
    public void Id_de_acao_muito_longo_e_recusado()
    {
        Assert.Null(ElevatedProtocol.Parse(["--action", new string('a', 65)]));
        Assert.NotNull(ElevatedProtocol.Parse(["--action", new string('a', 64)]));
    }

    [Theory]
    [InlineData("Fila \"com aspas\"")]
    [InlineData("Fila\nnova linha")]
    [InlineData("Fila\0nulo")]
    [InlineData("termina com barra\\")]
    [InlineData("")]
    public void Nome_de_impressora_perigoso_e_recusado(string printer)
    {
        Assert.Null(ElevatedProtocol.Parse(["--action", "printing.printer.resume", "--printer", printer]));
        Assert.Null(ElevatedProtocol.BuildArguments("printing.printer.resume", printer));
    }

    [Fact]
    public void Nome_de_impressora_de_rede_com_barras_e_aceito()
    {
        Assert.NotNull(ElevatedProtocol.Parse(["--action", "printing.printer.resume", "--printer", @"\\servidor\fila"]));
    }

    [Fact]
    public void Nome_de_impressora_muito_longo_e_recusado()
    {
        Assert.Null(ElevatedProtocol.BuildArguments("printing.printer.resume", new string('x', 257)));
        Assert.NotNull(ElevatedProtocol.BuildArguments("printing.printer.resume", new string('x', 256)));
    }

    // ---------- escrita do pedido ----------

    [Fact]
    public void Linha_de_comando_e_montada_no_formato_esperado()
    {
        Assert.Equal("--action printing.spooler.restart", ElevatedProtocol.BuildArguments("printing.spooler.restart", null));
        Assert.Equal("--action printing.printer.resume --printer \"Impressora X\"",
            ElevatedProtocol.BuildArguments("printing.printer.resume", "Impressora X"));
    }

    [Fact]
    public void Acao_com_cara_de_injecao_nao_vira_linha_de_comando()
    {
        Assert.Null(ElevatedProtocol.BuildArguments("printing.spooler.restart & calc", null));
        Assert.Null(ElevatedProtocol.BuildArguments("PRINTING", null));
    }

    // ---------- códigos de saída ----------

    [Theory]
    [InlineData(ElevatedProtocol.Done, ExecutionOutcome.Done)]
    [InlineData(ElevatedProtocol.Refused, ExecutionOutcome.Refused)]
    [InlineData(ElevatedProtocol.PreconditionNotMet, ExecutionOutcome.Failed)]
    [InlineData(ElevatedProtocol.NotElevated, ExecutionOutcome.ElevationDenied)]
    [InlineData(ElevatedProtocol.Failed, ExecutionOutcome.Failed)]
    [InlineData(1, ExecutionOutcome.Failed)]
    [InlineData(-1, ExecutionOutcome.Failed)]
    [InlineData(255, ExecutionOutcome.Failed)]
    public void Codigo_de_saida_vira_o_resultado_certo_e_o_desconhecido_nunca_vira_sucesso(int code, ExecutionOutcome expected)
    {
        Assert.Equal(expected, ElevatedProtocol.ToResult(code).Outcome);
    }

    [Fact]
    public void Codigo_inesperado_mostra_o_numero_para_quem_for_investigar()
    {
        Assert.Contains("(1)", ElevatedProtocol.ToResult(1).Detail);
    }
}
