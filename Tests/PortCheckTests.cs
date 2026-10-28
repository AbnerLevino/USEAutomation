using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Tests;

public class PortCheckTests
{
    private sealed class NullProgress : IProgress<DiagnosticProgress>
    {
        public void Report(DiagnosticProgress value) { }
    }

    private static PrinterStateInfo Queue(string name, string? port) => new(name, port, false, 3, 2, false);

    private static Task<CheckResult<IReadOnlyList<PrinterPortInfo>>> RunAsync(
        SourceResult<PrinterStateInfo> printers, SourceResult<NetworkPortInfo>? ports = null) =>
        new PortCheck(new FakePrintSystemProvider
        {
            PrinterStates = printers,
            NetworkPorts = ports ?? SourceResult<NetworkPortInfo>.Ok([])
        }).RunAsync(new NullProgress(), CancellationToken.None);

    private static SourceResult<PrinterStateInfo> Queues(params PrinterStateInfo[] q) => SourceResult<PrinterStateInfo>.Ok(q);

    [Theory]
    [InlineData("PORTPROMPT:", PortKind.Virtual)]
    [InlineData("FILE:", PortKind.Virtual)]
    [InlineData("USB001", PortKind.Usb)]
    [InlineData("usb002", PortKind.Usb)]
    [InlineData("WSD-1234-abcd", PortKind.NetworkWsd)]
    [InlineData("LPT1:", PortKind.LegacyLocal)]
    [InlineData("COM3:", PortKind.LegacyLocal)]
    [InlineData(@"\\servidor\fila", PortKind.SharedConnection)]
    [InlineData("PORTA_ESTRANHA", PortKind.Other)]
    public async Task Classifica_o_tipo_da_porta_pelo_nome(string port, PortKind expected)
    {
        var result = await RunAsync(Queues(Queue("Fila", port)));

        Assert.Equal(expected, result.Facts!.Single().Kind);
    }

    [Fact]
    public async Task Porta_tcpip_mostra_o_endereco_configurado()
    {
        var result = await RunAsync(
            Queues(Queue("Fila", "IP_192.168.0.5")),
            SourceResult<NetworkPortInfo>.Ok([new NetworkPortInfo("IP_192.168.0.5", "192.168.0.5", 9100)]));

        var info = result.Facts!.Single();
        Assert.Equal(PortKind.NetworkTcp, info.Kind);
        Assert.Equal("192.168.0.5:9100", info.Address);
        Assert.Contains("192.168.0.5:9100", result.Evidence.Single().Description);
    }

    [Fact]
    public async Task Portas_configuradas_sao_saudaveis_e_nao_provam_acesso()
    {
        var result = await RunAsync(Queues(Queue("A", "USB001"), Queue("PDF", "PORTPROMPT:")));

        Assert.Equal(CheckVerdict.Healthy, result.Verdict);
        Assert.Contains(result.Limitations, l => l.Contains("não prova"));
        Assert.Contains(result.Limitations, l => l.Contains("não foi testado"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Fila_sem_porta_e_problema_confirmado(string? port)
    {
        var result = await RunAsync(Queues(Queue("Fila", port), Queue("Outra", "USB001")));

        Assert.Equal(CheckVerdict.Problem, result.Verdict);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(Confidence.Confirmed, finding.Confidence);
        Assert.Contains("Fila", finding.Title);
        Assert.Equal(PortKind.None, result.Facts!.First().Kind);
    }

    [Fact]
    public async Task Falha_nas_portas_tcpip_nao_derruba_a_avaliacao_mas_e_registrada()
    {
        var result = await RunAsync(
            Queues(Queue("A", "IP_1.1.1.1")),
            SourceResult<NetworkPortInfo>.Fail(SourceStatus.Unavailable, "x"));

        Assert.Equal(CheckVerdict.Healthy, result.Verdict);
        Assert.Equal(PortKind.Other, result.Facts!.Single().Kind);
        Assert.Contains(result.Limitations, l => l.Contains("TCP/IP não puderam"));
    }

    [Theory]
    [InlineData(SourceStatus.PermissionDenied)]
    [InlineData(SourceStatus.Unavailable)]
    [InlineData(SourceStatus.Failed)]
    public async Task Filas_nao_consultadas_nao_e_avaliavel(SourceStatus status)
    {
        var result = await RunAsync(SourceResult<PrinterStateInfo>.Fail(status, "x"));

        Assert.Equal(CheckVerdict.NotEvaluable, result.Verdict);
    }

    [Fact]
    public async Task Sem_filas_nao_e_avaliavel()
    {
        var result = await RunAsync(Queues());

        Assert.Equal(CheckVerdict.NotEvaluable, result.Verdict);
    }
}
