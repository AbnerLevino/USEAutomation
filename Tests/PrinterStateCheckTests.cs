using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Tests;

public class PrinterStateCheckTests
{
    private sealed class NullProgress : IProgress<DiagnosticProgress>
    {
        public void Report(DiagnosticProgress value) { }
    }

    private static PrinterStateInfo Printer(
        string name = "Impressora X", string? port = "USB001",
        bool? offline = false, int? extended = 3, int? error = 2, bool? isDefault = false) =>
        new(name, port, offline, extended, error, isDefault);

    private static Task<CheckResult<IReadOnlyList<PrinterAssessment>>> RunAsync(params PrinterStateInfo[] printers) =>
        RunAsync(SourceResult<PrinterStateInfo>.Ok(printers));

    private static Task<CheckResult<IReadOnlyList<PrinterAssessment>>> RunAsync(SourceResult<PrinterStateInfo> states) =>
        new PrinterStateCheck(new FakePrintSystemProvider { PrinterStates = states }).RunAsync(new NullProgress(), CancellationToken.None);

    [Fact]
    public async Task Fila_ociosa_sem_erro_e_saudavel_com_limitacao()
    {
        var result = await RunAsync(Printer());

        Assert.Equal(CheckVerdict.Healthy, result.Verdict);
        Assert.Empty(result.Findings);
        Assert.Contains(result.Limitations, l => l.Contains("não prova"));
    }

    [Fact]
    public async Task Estados_desconhecidos_nao_geram_problema()
    {
        // O Windows não informou nada: ausência de evidência não é evidência de problema.
        var result = await RunAsync(Printer(offline: null, extended: null, error: null));

        Assert.Equal(CheckVerdict.Healthy, result.Verdict);
    }

    [Theory]
    [InlineData(true, 3, 2)]    // WorkOffline marcado
    [InlineData(false, 7, 2)]   // ExtendedPrinterStatus = Offline
    [InlineData(false, 3, 9)]   // DetectedErrorState = Offline
    public async Task Offline_reportado_pelo_windows_e_problema_confirmado(bool offline, int extended, int error)
    {
        var result = await RunAsync(Printer(offline: offline, extended: extended, error: error));

        Assert.Equal(CheckVerdict.Problem, result.Verdict);
        Assert.True(result.Facts!.Single().Offline);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(Confidence.Confirmed, finding.Confidence);
        Assert.Contains("offline", finding.Title);
    }

    [Fact]
    public async Task Fila_pausada_e_problema_confirmado()
    {
        var result = await RunAsync(Printer(extended: 8));

        Assert.Equal(CheckVerdict.Problem, result.Verdict);
        Assert.True(result.Facts!.Single().Paused);
        Assert.Contains("pausada", result.Findings.Single().Title);
        Assert.Contains("retomar", result.Recommendations.Single().Text);
    }

    [Theory]
    [InlineData(3, "pouco papel", Severity.Medium)]
    [InlineData(5, "pouco toner", Severity.Medium)]
    [InlineData(4, "sem papel", Severity.High)]
    [InlineData(6, "sem toner", Severity.High)]
    [InlineData(7, "tampa", Severity.High)]
    [InlineData(8, "atolado", Severity.High)]
    [InlineData(10, "manutenção", Severity.High)]
    [InlineData(11, "bandeja", Severity.High)]
    public async Task Erro_do_equipamento_tem_descricao_e_gravidade(int errorState, string text, Severity severity)
    {
        var result = await RunAsync(Printer(error: errorState));

        Assert.Equal(CheckVerdict.Problem, result.Verdict);
        var finding = Assert.Single(result.Findings);
        Assert.Contains(text, finding.Description);
        Assert.Equal(severity, finding.Severity);
    }

    [Fact]
    public async Task Status_de_erro_sem_detalhe_ainda_e_reportado()
    {
        var result = await RunAsync(Printer(extended: 9, error: 0));

        Assert.Equal(CheckVerdict.Problem, result.Verdict);
        Assert.Contains("sem detalhe", result.Findings.Single().Description);
    }

    [Fact]
    public async Task Varias_condicoes_na_mesma_fila_geram_um_achado_para_cada()
    {
        var result = await RunAsync(Printer(offline: true, extended: 8, error: 4));

        Assert.Equal(3, result.Findings.Count);
        Assert.Equal(3, result.Findings.Select(f => f.Id).Distinct().Count());
        Assert.Single(result.Facts!);
    }

    [Fact]
    public async Task Fila_virtual_e_rotulada_e_avaliada()
    {
        var result = await RunAsync(
            Printer("Microsoft Print to PDF", port: "PORTPROMPT:", extended: 8),
            Printer("Impressora X"));

        var pdf = result.Facts!.Single(a => a.Printer.Name == "Microsoft Print to PDF");
        Assert.True(pdf.IsVirtual);
        Assert.True(pdf.Paused);
        Assert.False(result.Facts!.Single(a => a.Printer.Name == "Impressora X").IsVirtual);
        Assert.Contains(result.Evidence, e => e.Description.Contains("virtual"));
    }

    [Fact]
    public async Task Marca_a_impressora_padrao_na_evidencia()
    {
        var result = await RunAsync(Printer(isDefault: true));

        Assert.Contains("impressora padrão", result.Evidence.Single().Description);
    }

    [Theory]
    [InlineData(SourceStatus.PermissionDenied)]
    [InlineData(SourceStatus.Unavailable)]
    [InlineData(SourceStatus.Failed)]
    public async Task Fonte_nao_consultada_nao_e_avaliavel(SourceStatus status)
    {
        var result = await RunAsync(SourceResult<PrinterStateInfo>.Fail(status, "detalhe"));

        Assert.Equal(CheckVerdict.NotEvaluable, result.Verdict);
        Assert.Null(result.Facts);
    }

    [Fact]
    public async Task Sem_filas_instaladas_nao_e_avaliavel()
    {
        var result = await RunAsync();

        Assert.Equal(CheckVerdict.NotEvaluable, result.Verdict);
    }
}
