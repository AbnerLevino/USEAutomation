using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;

namespace AutomationPlatform.Tests;

/// <summary>Provedor falso: devolve o que o teste mandar, sem depender de impressora física.</summary>
internal sealed class FakePrintDeviceProvider : IPrintDeviceProvider
{
    public SourceResult<PrintDeviceInfo> Devices { get; set; } = SourceResult<PrintDeviceInfo>.Ok([]);
    public SourceResult<InstalledPrinterInfo> Printers { get; set; } = SourceResult<InstalledPrinterInfo>.Ok([]);
    public Exception? ThrowOnDevices { get; set; }

    public Task<SourceResult<PrintDeviceInfo>> GetPrintDevicesAsync(CancellationToken cancellationToken) =>
        ThrowOnDevices is null ? Task.FromResult(Devices) : Task.FromException<SourceResult<PrintDeviceInfo>>(ThrowOnDevices);

    public Task<SourceResult<InstalledPrinterInfo>> GetInstalledPrintersAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Printers);
}

public class PrinterRecognitionDiagnosticTests
{
    private static SourceResult<PrintDeviceInfo> DeviceList(params PrintDeviceInfo[] devices) => SourceResult<PrintDeviceInfo>.Ok(devices);
    private static SourceResult<InstalledPrinterInfo> QueueList(params string[] names) =>
        SourceResult<InstalledPrinterInfo>.Ok(names.Select(n => new InstalledPrinterInfo(n)).ToArray());

    private static Task<DiagnosticReport> RunAsync(FakePrintDeviceProvider provider)
    {
        var registry = new DiagnosticRegistry();
        registry.Register(new PrinterRecognitionDiagnostic(provider));
        return new DiagnosticEngine(registry).RunAsync(PrinterRecognitionDiagnostic.DiagnosticId);
    }

    [Fact]
    public async Task Device_without_error_and_with_queue_reports_no_problem_detected()
    {
        var report = await RunAsync(new FakePrintDeviceProvider
        {
            Devices = DeviceList(new PrintDeviceInfo("Impressora X", "USB\\VID_1", 0)),
            Printers = QueueList("Impressora X")
        });

        Assert.Equal(ExecutionStatus.Completed, report.ExecutionStatus);
        Assert.Equal(OverallStatus.NoProblemDetected, report.OverallStatus);
        Assert.Empty(report.Findings);
        Assert.Single(report.Evidence);
        // "Nada detectado" nunca é prova de ausência: precisa registrar a limitação.
        Assert.Contains(report.Limitations, l => l.Contains("não prova"));
    }

    [Fact]
    public async Task Device_with_error_code_is_confirmed_problem_with_evidence()
    {
        var report = await RunAsync(new FakePrintDeviceProvider
        {
            Devices = DeviceList(new PrintDeviceInfo("Impressora X", "USB\\VID_1", 28)),
            Printers = QueueList()
        });

        Assert.Equal(OverallStatus.ProblemConfirmed, report.OverallStatus);
        var finding = Assert.Single(report.Findings);
        Assert.Equal(Confidence.Confirmed, finding.Confidence);
        Assert.Contains("28", finding.Description);
        Assert.Contains(finding.EvidenceIds.Single(), report.Evidence.Select(e => e.Id));
        Assert.Single(report.Recommendations);
    }

    [Fact]
    public async Task Only_faulty_devices_become_findings()
    {
        var report = await RunAsync(new FakePrintDeviceProvider
        {
            Devices = DeviceList(
                new PrintDeviceInfo("Boa", "A", 0),
                new PrintDeviceInfo("Ruim", "B", 22)),
            Printers = QueueList("Boa")
        });

        Assert.Equal(2, report.Evidence.Count);
        var finding = Assert.Single(report.Findings);
        Assert.Contains("Ruim", finding.Title);
    }

    [Fact]
    public async Task Device_without_error_but_no_queue_is_probable_problem()
    {
        var report = await RunAsync(new FakePrintDeviceProvider
        {
            Devices = DeviceList(new PrintDeviceInfo("Impressora X", "USB\\VID_1", 0)),
            Printers = QueueList()
        });

        Assert.Equal(OverallStatus.ProblemProbable, report.OverallStatus);
        Assert.Equal(Confidence.Probable, Assert.Single(report.Findings).Confidence);
    }

    [Fact]
    public async Task No_device_found_is_inconclusive_not_a_hardware_failure()
    {
        var report = await RunAsync(new FakePrintDeviceProvider { Devices = DeviceList(), Printers = QueueList("De rede") });

        Assert.Equal(ExecutionStatus.Completed, report.ExecutionStatus);
        Assert.Equal(OverallStatus.Inconclusive, report.OverallStatus);
        Assert.Empty(report.Findings);
        Assert.Contains(report.Limitations, l => l.Contains("rede"));
    }

    [Fact]
    public async Task Unavailable_device_source_is_inconclusive_and_recorded()
    {
        var report = await RunAsync(new FakePrintDeviceProvider
        {
            Devices = SourceResult<PrintDeviceInfo>.Fail(SourceStatus.Unavailable, "WMI parado")
        });

        Assert.Equal(OverallStatus.Inconclusive, report.OverallStatus);
        var source = report.Sources.First();
        Assert.Equal(SourceStatus.Unavailable, source.Status);
        Assert.Equal("WMI parado", source.Detail);
    }

    [Fact]
    public async Task Permission_denied_source_is_inconclusive_never_no_problem()
    {
        var report = await RunAsync(new FakePrintDeviceProvider
        {
            Devices = SourceResult<PrintDeviceInfo>.Fail(SourceStatus.PermissionDenied, "negado")
        });

        Assert.NotEqual(OverallStatus.NoProblemDetected, report.OverallStatus);
        Assert.Equal(OverallStatus.Inconclusive, report.OverallStatus);
        Assert.Equal(SourceStatus.PermissionDenied, report.Sources.First().Status);
    }

    [Fact]
    public async Task Unauthorized_exception_becomes_permission_denied_execution()
    {
        var report = await RunAsync(new FakePrintDeviceProvider { ThrowOnDevices = new UnauthorizedAccessException("sem acesso") });

        Assert.Equal(ExecutionStatus.PermissionDenied, report.ExecutionStatus);
        Assert.Equal(OverallStatus.NotEvaluated, report.OverallStatus);
    }

    [Fact]
    public async Task Exception_during_collection_becomes_failed_report_not_a_crash()
    {
        var report = await RunAsync(new FakePrintDeviceProvider { ThrowOnDevices = new InvalidOperationException("quebrou") });

        Assert.Equal(ExecutionStatus.Failed, report.ExecutionStatus);
        Assert.Equal(OverallStatus.NotEvaluated, report.OverallStatus);
        Assert.Contains("quebrou", report.ErrorMessage);
    }

    [Fact]
    public async Task Printer_queue_failure_is_recorded_as_limitation_but_devices_still_analysed()
    {
        var report = await RunAsync(new FakePrintDeviceProvider
        {
            Devices = DeviceList(new PrintDeviceInfo("Impressora X", "A", 0)),
            Printers = SourceResult<InstalledPrinterInfo>.Fail(SourceStatus.Failed, "erro")
        });

        Assert.Equal(OverallStatus.NoProblemDetected, report.OverallStatus);
        Assert.Contains(report.Limitations, l => l.Contains("filas de impressão não puderam"));
    }

    [Fact]
    public async Task Report_from_diagnostic_survives_json_round_trip()
    {
        var report = await RunAsync(new FakePrintDeviceProvider
        {
            Devices = DeviceList(new PrintDeviceInfo("Impressora X", "A", 28)),
            Printers = QueueList()
        });

        var clone = ReportSerializer.Deserialize(ReportSerializer.Serialize(report));

        Assert.Equal(report.OverallStatus, clone.OverallStatus);
        Assert.Equal(report.Findings.Count, clone.Findings.Count);
        Assert.Equal(report.Findings[0].Confidence, clone.Findings[0].Confidence);
        Assert.Equal(ReportSchema.CurrentVersion, clone.SchemaVersion);
    }
}
