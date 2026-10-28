using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Tests;

public class DefaultPrinterCheckTests
{
    private sealed class NullProgress : IProgress<DiagnosticProgress>
    {
        public void Report(DiagnosticProgress value) { }
    }

    private static PrinterStateInfo Queue(string name, string port, bool? isDefault) =>
        new(name, port, false, 3, 2, isDefault);

    private static Task<CheckResult<DefaultPrinterFacts>> RunAsync(
        SourceResult<PrinterStateInfo> printers, SourceResult<DefaultPrinterModeInfo>? mode = null) =>
        new DefaultPrinterCheck(new FakePrintSystemProvider
        {
            PrinterStates = printers,
            DefaultMode = mode ?? SourceResult<DefaultPrinterModeInfo>.Ok([new DefaultPrinterModeInfo(false)])
        }).RunAsync(new NullProgress(), CancellationToken.None);

    private static SourceResult<PrinterStateInfo> Queues(params PrinterStateInfo[] q) => SourceResult<PrinterStateInfo>.Ok(q);

    [Fact]
    public async Task Padrao_fisica_e_saudavel()
    {
        var result = await RunAsync(Queues(Queue("Impressora X", "USB001", true), Queue("PDF", "PORTPROMPT:", false)));

        Assert.Equal(CheckVerdict.Healthy, result.Verdict);
        Assert.Equal("Impressora X", result.Facts!.DefaultName);
        Assert.False(result.Facts.DefaultIsVirtual);
        Assert.Equal(1, result.Facts.PhysicalQueueCount);
        Assert.Contains(result.Limitations, l => l.Contains("não prova"));
    }

    [Fact]
    public async Task Nenhuma_marcada_como_padrao_e_problema_confirmado()
    {
        var result = await RunAsync(Queues(Queue("A", "USB001", false), Queue("B", "IP_1", false)));

        Assert.Equal(CheckVerdict.Problem, result.Verdict);
        Assert.Null(result.Facts!.DefaultName);
        Assert.Equal(Confidence.Confirmed, result.Findings.Single().Confidence);
    }

    [Fact]
    public async Task Padrao_virtual_com_fisica_instalada_e_problema_provavel()
    {
        var result = await RunAsync(Queues(Queue("PDF", "PORTPROMPT:", true), Queue("Impressora X", "USB001", false)));

        Assert.Equal(CheckVerdict.Problem, result.Verdict);
        Assert.True(result.Facts!.DefaultIsVirtual);
        Assert.Equal(Confidence.Probable, result.Findings.Single().Confidence);
        Assert.Contains("Impressora X", result.Recommendations.Single().Text + "Impressora X");
    }

    [Fact]
    public async Task Padrao_virtual_sem_nenhuma_fisica_nao_e_problema()
    {
        // Só existe PDF: não há para onde "corrigir" o padrão.
        var result = await RunAsync(Queues(Queue("PDF", "PORTPROMPT:", true)));

        Assert.Equal(CheckVerdict.Healthy, result.Verdict);
    }

    [Fact]
    public async Task Windows_gerenciando_o_padrao_vira_limitacao_e_evidencia()
    {
        var result = await RunAsync(
            Queues(Queue("A", "USB001", true)),
            SourceResult<DefaultPrinterModeInfo>.Ok([new DefaultPrinterModeInfo(true)]));

        Assert.True(result.Facts!.WindowsManagesDefault);
        Assert.Contains(result.Limitations, l => l.Contains("gerencia"));
        Assert.Contains(result.Evidence, e => e.Description.Contains("gerencia"));
    }

    [Fact]
    public async Task Modo_indisponivel_nao_impede_a_avaliacao_mas_e_registrado()
    {
        var result = await RunAsync(
            Queues(Queue("A", "USB001", true)),
            SourceResult<DefaultPrinterModeInfo>.Fail(SourceStatus.PermissionDenied, "x"));

        Assert.Equal(CheckVerdict.Healthy, result.Verdict);
        Assert.Null(result.Facts!.WindowsManagesDefault);
        Assert.Contains(result.Limitations, l => l.Contains("Não foi possível saber"));
    }

    [Fact]
    public async Task Windows_nao_informou_quem_e_padrao_nao_e_avaliavel()
    {
        var result = await RunAsync(Queues(Queue("A", "USB001", null)));

        Assert.Equal(CheckVerdict.NotEvaluable, result.Verdict);
    }

    [Theory]
    [InlineData(SourceStatus.PermissionDenied)]
    [InlineData(SourceStatus.Unavailable)]
    [InlineData(SourceStatus.Failed)]
    public async Task Filas_nao_consultadas_nao_e_avaliavel(SourceStatus status)
    {
        var result = await RunAsync(SourceResult<PrinterStateInfo>.Fail(status, "x"));

        Assert.Equal(CheckVerdict.NotEvaluable, result.Verdict);
        Assert.Null(result.Facts);
    }

    [Fact]
    public async Task Sem_filas_nao_e_avaliavel()
    {
        var result = await RunAsync(Queues());

        Assert.Equal(CheckVerdict.NotEvaluable, result.Verdict);
    }
}
