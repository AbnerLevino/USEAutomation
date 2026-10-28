using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Tests;

public class QueueCheckTests
{
    private sealed class NullProgress : IProgress<DiagnosticProgress>
    {
        public void Report(DiagnosticProgress value) { }
    }

    private static PrintJobInfo Job(string printer, int id, PrintJobStatus? status) => new(printer, id, status);

    private static Task<CheckResult<IReadOnlyList<PrinterQueueSummary>>> RunAsync(SourceResult<PrintJobInfo> jobs) =>
        new QueueCheck(new FakePrintSystemProvider { Jobs = jobs }).RunAsync(new NullProgress(), CancellationToken.None);

    private static SourceResult<PrintJobInfo> Jobs(params PrintJobInfo[] jobs) => SourceResult<PrintJobInfo>.Ok(jobs);

    [Fact]
    public async Task Sem_trabalhos_e_saudavel_e_registra_limitacao()
    {
        var result = await RunAsync(Jobs());

        Assert.Equal(CheckVerdict.Healthy, result.Verdict);
        Assert.Empty(result.Facts!);
        Assert.Contains(result.Limitations, l => l.Contains("não prova"));
    }

    [Theory]
    [InlineData(PrintJobStatus.Error)]
    [InlineData(PrintJobStatus.Deleting)]
    [InlineData(PrintJobStatus.BlockedDeviceQueue)]
    [InlineData(PrintJobStatus.Error | PrintJobStatus.Spooling)]
    public async Task Trabalho_em_erro_exclusao_ou_bloqueado_e_preso(PrintJobStatus status)
    {
        var result = await RunAsync(Jobs(Job("Fila A", 1, status), Job("Fila A", 2, PrintJobStatus.Printing)));

        Assert.Equal(CheckVerdict.Problem, result.Verdict);
        var queue = Assert.Single(result.Facts!);
        Assert.Equal(2, queue.TotalJobs);
        Assert.Equal(1, queue.StuckJobs);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(Confidence.Confirmed, finding.Confidence);
        Assert.Contains(finding.EvidenceIds.Single(), result.Evidence.Select(e => e.Id));
    }

    [Theory]
    [InlineData(PrintJobStatus.Paused)]
    [InlineData(PrintJobStatus.Offline)]
    [InlineData(PrintJobStatus.PaperOut)]
    [InlineData(PrintJobStatus.UserIntervention)]
    public async Task Trabalho_esperando_por_condicao_da_impressora_nao_e_preso(PrintJobStatus status)
    {
        var result = await RunAsync(Jobs(Job("Fila A", 1, status)));

        Assert.Equal(CheckVerdict.Healthy, result.Verdict);
        var queue = Assert.Single(result.Facts!);
        Assert.Equal(0, queue.StuckJobs);
        Assert.Equal(1, queue.HeldJobs);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task Estado_desconhecido_do_trabalho_nao_conta_como_preso()
    {
        var result = await RunAsync(Jobs(Job("Fila A", 1, null)));

        Assert.Equal(CheckVerdict.Healthy, result.Verdict);
        Assert.Equal(0, result.Facts!.Single().StuckJobs);
    }

    [Fact]
    public async Task Agrupa_por_fila_e_so_aponta_a_fila_com_problema()
    {
        var result = await RunAsync(Jobs(
            Job("Fila A", 1, PrintJobStatus.Printing),
            Job("Fila B", 2, PrintJobStatus.Error),
            Job("fila b", 3, PrintJobStatus.Error)));

        Assert.Equal(2, result.Facts!.Count);
        var finding = Assert.Single(result.Findings);
        Assert.Contains("Fila B", finding.Title);
        Assert.Contains("2 de 2", finding.Description);
    }

    [Theory]
    [InlineData(SourceStatus.PermissionDenied)]
    [InlineData(SourceStatus.Unavailable)]
    [InlineData(SourceStatus.Failed)]
    public async Task Fonte_nao_consultada_nao_e_avaliavel(SourceStatus status)
    {
        var result = await RunAsync(SourceResult<PrintJobInfo>.Fail(status, "detalhe"));

        Assert.Equal(CheckVerdict.NotEvaluable, result.Verdict);
        Assert.Null(result.Facts);
        Assert.Equal(status, result.Sources.Single().Status);
    }

    [Fact]
    public async Task Evidencia_nunca_expoe_nomes_de_documentos()
    {
        // O contrato nem tem campo para o documento: só fila, número e estado.
        Assert.DoesNotContain(typeof(PrintJobInfo).GetProperties(), p => p.Name.Contains("Document", StringComparison.OrdinalIgnoreCase));
        var result = await RunAsync(Jobs(Job("Fila A", 1, PrintJobStatus.Error)));
        Assert.All(result.Evidence, e => Assert.DoesNotContain("documento", e.Description.Replace("documentos", ""), StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Microsoft Print to PDF, 3", "Microsoft Print to PDF", 3)]
    [InlineData("Fila, com virgula, 12", "Fila, com virgula", 12)]
    public void Separa_fila_e_numero_do_nome_do_wmi(string name, string printer, int id)
    {
        var parsed = PrintJobNameParser.Parse(name);

        Assert.Equal((printer, id), parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sem numero")]
    [InlineData(", 5")]
    [InlineData("Fila, abc")]
    public void Nome_fora_do_formato_e_descartado(string? name) => Assert.Null(PrintJobNameParser.Parse(name));
}
