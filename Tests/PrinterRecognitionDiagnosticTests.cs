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
        Assert.Equal(2, report.Evidence.Count); // 1 fila + 1 dispositivo
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

        Assert.Equal(2, report.Evidence.Count(e => e.Source.Contains("Plug and Play")));
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
            Devices = DeviceList(new PrintDeviceInfo("Impressora X", "USB\\VID_1", 0)),
            Printers = SourceResult<InstalledPrinterInfo>.Fail(SourceStatus.Failed, "erro")
        });

        Assert.Equal(OverallStatus.NoProblemDetected, report.OverallStatus);
        Assert.Contains(report.Limitations, l => l.Contains("filas de impressão não puderam"));
    }

    // ---- Virtuais x físicas ----

    private static readonly PrintDeviceInfo SystemPdfDevice = new("Microsoft Print to PDF", "SWD\\PRINTENUM\\{1}", 0);
    private static readonly InstalledPrinterInfo PdfQueue = new("Microsoft Print to PDF", "PORTPROMPT:");

    [Fact]
    public async Task Only_virtual_items_are_listed_but_do_not_prove_a_physical_printer()
    {
        // É exatamente o cenário real desta máquina de desenvolvimento.
        var report = await RunAsync(new FakePrintDeviceProvider
        {
            Devices = DeviceList(SystemPdfDevice),
            Printers = SourceResult<InstalledPrinterInfo>.Ok([PdfQueue])
        });

        Assert.Equal(OverallStatus.Inconclusive, report.OverallStatus);
        Assert.Contains("Nenhuma impressora física", report.Summary);
        Assert.Contains(report.Evidence, e => e.Description.Contains("Microsoft Print to PDF") && e.Description.Contains("virtual"));
        Assert.Contains(report.Limitations, l => l.Contains("virtuais"));
    }

    [Fact]
    public async Task Usb_device_with_only_virtual_queues_is_probable_problem()
    {
        var report = await RunAsync(new FakePrintDeviceProvider
        {
            Devices = DeviceList(new PrintDeviceInfo("Impressora X", "USB\\VID_1", 0), SystemPdfDevice),
            Printers = SourceResult<InstalledPrinterInfo>.Ok([PdfQueue])
        });

        Assert.Equal(OverallStatus.ProblemProbable, report.OverallStatus);
        Assert.Equal(Confidence.Probable, Assert.Single(report.Findings).Confidence);
    }

    [Fact]
    public async Task Usb_device_with_physical_queue_reports_no_problem_and_keeps_virtual_queue_listed()
    {
        var report = await RunAsync(new FakePrintDeviceProvider
        {
            Devices = DeviceList(new PrintDeviceInfo("Impressora X", "USB\\VID_1", 0)),
            Printers = SourceResult<InstalledPrinterInfo>.Ok([PdfQueue, new InstalledPrinterInfo("Impressora X", "USB001")])
        });

        Assert.Equal(OverallStatus.NoProblemDetected, report.OverallStatus);
        Assert.Contains("1 virtual(is), 1 física(s)", report.Summary);
        Assert.Equal(2, report.Evidence.Count(e => e.Source.Contains("Win32_Printer")));
    }

    [Fact]
    public async Task Network_queue_alone_is_inconclusive_because_connectivity_is_not_checked()
    {
        var report = await RunAsync(new FakePrintDeviceProvider
        {
            Devices = DeviceList(),
            Printers = SourceResult<InstalledPrinterInfo>.Ok([new InstalledPrinterInfo("Escritório", "IP_192.168.0.5")])
        });

        Assert.Equal(OverallStatus.Inconclusive, report.OverallStatus);
        Assert.Contains("porta física ou de rede", report.Summary);
    }

    [Theory]
    [InlineData("PORTPROMPT:", true)]
    [InlineData("portprompt:", true)]
    [InlineData("FILE:", true)]
    [InlineData("nul:", true)]
    [InlineData("SHRFAX:", true)]
    [InlineData("USB001", false)]
    [InlineData("IP_192.168.0.5", false)]
    [InlineData(null, false)] // porta desconhecida: não presumimos virtual
    public void Queue_is_classified_as_virtual_by_port_not_by_name(string? port, bool expectedVirtual)
    {
        Assert.Equal(expectedVirtual, PrinterClassifier.IsVirtualQueue(new InstalledPrinterInfo("qualquer nome", port)));
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
