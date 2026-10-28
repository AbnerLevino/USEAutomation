using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Tests;

/// <summary>
/// Testes de caracterização: usam valores que o Windows 11 (build 26200) devolveu DE VERDADE em
/// 2026-10-28, com filas falsas criadas só para o teste. Existem porque testes com dados inventados
/// repetem a nossa suposição; estes repetem a realidade. Se o Windows mudar, eles avisam.
/// Roteiro que gerou os valores: tools/Test-FilasFalsas.ps1.
/// </summary>
public class WindowsRealValuesTests
{
    private sealed class NullProgress : IProgress<DiagnosticProgress>
    {
        public void Report(DiagnosticProgress value) { }
    }

    private static Task<CheckResult<IReadOnlyList<PrinterAssessment>>> State(params PrinterStateInfo[] printers) =>
        new PrinterStateCheck(new FakePrintSystemProvider { PrinterStates = SourceResult<PrinterStateInfo>.Ok(printers) })
            .RunAsync(new NullProgress(), CancellationToken.None);

    [Fact]
    public async Task Fila_pausada_no_windows_real_tem_ExtendedPrinterStatus_8_e_erro_2()
    {
        // Observado após Win32_Printer.Pause() em 'TESTE Pausada' (porta LPT1:).
        var result = await State(new PrinterStateInfo("TESTE Pausada", "LPT1:", false, 8, 2, true));

        Assert.Equal(CheckVerdict.Problem, result.Verdict);
        Assert.True(result.Facts!.Single().Paused);
        Assert.False(result.Facts!.Single().Offline);
    }

    [Fact]
    public async Task Fila_marcada_offline_no_windows_real_tem_WorkOffline_true_e_status_2()
    {
        // Observado após WorkOffline = True em 'TESTE Rede Inalcancavel'; o status continuou 2 (desconhecido).
        var result = await State(new PrinterStateInfo("TESTE Rede Inalcancavel", "IP_10.255.255.1", true, 2, 0, false));

        Assert.Equal(CheckVerdict.Problem, result.Verdict);
        Assert.True(result.Facts!.Single().Offline);
    }

    [Fact]
    public async Task Fila_saudavel_no_windows_real_tem_status_2_e_erro_0_e_nao_e_problema()
    {
        // 'Microsoft Print to PDF' ociosa: ExtendedPrinterStatus 2 (desconhecido) e DetectedErrorState 0.
        var result = await State(new PrinterStateInfo("Microsoft Print to PDF", "PORTPROMPT:", false, 2, 0, true));

        Assert.Equal(CheckVerdict.Healthy, result.Verdict);
    }

    [Fact]
    public async Task Fila_de_rede_com_ip_inexistente_NAO_e_detectada_pelo_estado()
    {
        // Limitação PROVADA no Windows real: IP inexistente e sem WorkOffline aparece como status 2 / erro 0.
        // Se este teste passar a falhar, o Windows passou a informar esse caso e podemos aproveitá-lo.
        var result = await State(new PrinterStateInfo("TESTE Rede Inalcancavel", "IP_10.255.255.1", false, 2, 0, false));

        Assert.Equal(CheckVerdict.Healthy, result.Verdict);
        Assert.Contains(result.Limitations, l => l.Contains("rede inalcançável pode não aparecer como offline"));
    }

    [Fact]
    public async Task Trabalho_em_fila_pausada_no_windows_real_tem_StatusMask_0_e_nao_e_preso()
    {
        // Observado: Win32_PrintJob 'TESTE Pausada, 2' com StatusMask 0 mesmo com a fila pausada.
        var parsed = PrintJobNameParser.Parse("TESTE Pausada, 2");
        Assert.NotNull(parsed);
        var job = new PrintJobInfo(parsed!.Value.PrinterName, parsed.Value.JobId, (PrintJobStatus)0u);

        var result = await new QueueCheck(new FakePrintSystemProvider { Jobs = SourceResult<PrintJobInfo>.Ok([job]) })
            .RunAsync(new NullProgress(), CancellationToken.None);

        Assert.Equal(CheckVerdict.Healthy, result.Verdict);
        var queue = Assert.Single(result.Facts!);
        Assert.Equal("TESTE Pausada", queue.PrinterName);
        Assert.Equal(1, queue.TotalJobs);
        Assert.Equal(0, queue.StuckJobs);
    }

    [Fact]
    public async Task Primeira_fila_fisica_criada_vira_padrao_e_o_windows_gerencia_o_padrao()
    {
        // Observado: ao criar 'TESTE Pausada', o Windows a tornou padrão sozinho (LegacyDefaultPrinterMode 0).
        var printers = SourceResult<PrinterStateInfo>.Ok(
        [
            new PrinterStateInfo("TESTE Pausada", "LPT1:", false, 8, 2, true),
            new PrinterStateInfo("Microsoft Print to PDF", "PORTPROMPT:", false, 2, 0, false)
        ]);

        var result = await new DefaultPrinterCheck(new FakePrintSystemProvider
        {
            PrinterStates = printers,
            DefaultMode = SourceResult<DefaultPrinterModeInfo>.Ok([new DefaultPrinterModeInfo(true)])
        }).RunAsync(new NullProgress(), CancellationToken.None);

        // A verificação isolada diz "ok" mesmo com a padrão pausada: o cruzamento é trabalho do cenário (fase B).
        Assert.Equal(CheckVerdict.Healthy, result.Verdict);
        Assert.Equal("TESTE Pausada", result.Facts!.DefaultName);
        Assert.True(result.Facts.WindowsManagesDefault);
    }

    [Fact]
    public void Porta_tcpip_do_windows_real_mostra_endereco_e_porta_9100()
    {
        var net = new Dictionary<string, NetworkPortInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["IP_10.255.255.1"] = new("IP_10.255.255.1", "10.255.255.1", 9100)
        };

        var info = PortCheck.Classify(new PrinterStateInfo("TESTE Rede Inalcancavel", "IP_10.255.255.1", false, 2, 0, false), net);

        Assert.Equal(PortKind.NetworkTcp, info.Kind);
        Assert.Equal("10.255.255.1:9100", info.Address);
    }
}
