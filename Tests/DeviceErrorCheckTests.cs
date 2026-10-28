using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Tests;

public class DeviceErrorCheckTests
{
    private sealed class NullProgress : IProgress<DiagnosticProgress>
    {
        public void Report(DiagnosticProgress value) { }
    }

    private static Task<CheckResult<IReadOnlyList<PrintDeviceInfo>>> RunAsync(SourceResult<PrintDeviceInfo> devices) =>
        new DeviceErrorCheck(new FakePrintDeviceProvider { Devices = devices }).RunAsync(new NullProgress(), CancellationToken.None);

    private static SourceResult<PrintDeviceInfo> Devices(params PrintDeviceInfo[] d) => SourceResult<PrintDeviceInfo>.Ok(d);

    [Fact]
    public async Task Dispositivos_sem_erro_sao_saudaveis_com_limitacao()
    {
        var result = await RunAsync(Devices(new PrintDeviceInfo("A", @"USB\VID_1", 0)));

        Assert.Equal(CheckVerdict.Healthy, result.Verdict);
        Assert.Empty(result.Facts!);
        Assert.Contains(result.Limitations, l => l.Contains("não prova"));
    }

    [Fact]
    public async Task Codigo_de_erro_e_problema_confirmado_com_significado()
    {
        var result = await RunAsync(Devices(new PrintDeviceInfo("A", @"USB\VID_1", 28), new PrintDeviceInfo("B", @"SWD\X", 0)));

        Assert.Equal(CheckVerdict.Problem, result.Verdict);
        var finding = Assert.Single(result.Findings);
        Assert.Contains("28", finding.Description);
        Assert.Contains("drivers", finding.Description);
        Assert.Equal(Confidence.Confirmed, finding.Confidence);
        Assert.Equal("A", result.Facts!.Single().Name);
        Assert.Equal(2, result.Evidence.Count);
    }

    [Fact]
    public async Task Codigo_nao_informado_nao_conta_como_erro()
    {
        var result = await RunAsync(Devices(new PrintDeviceInfo("A", @"USB\VID_1", null)));

        Assert.Equal(CheckVerdict.Healthy, result.Verdict);
        Assert.Contains("não informado", result.Evidence.Single().Description);
    }

    [Fact]
    public async Task Nenhum_dispositivo_nao_e_avaliavel()
    {
        var result = await RunAsync(Devices());

        Assert.Equal(CheckVerdict.NotEvaluable, result.Verdict);
    }

    [Theory]
    [InlineData(SourceStatus.PermissionDenied)]
    [InlineData(SourceStatus.Unavailable)]
    [InlineData(SourceStatus.Failed)]
    public async Task Fonte_nao_consultada_nao_e_avaliavel(SourceStatus status)
    {
        var result = await RunAsync(SourceResult<PrintDeviceInfo>.Fail(status, "x"));

        Assert.Equal(CheckVerdict.NotEvaluable, result.Verdict);
        Assert.Equal(status, result.Sources.Single().Status);
    }
}
