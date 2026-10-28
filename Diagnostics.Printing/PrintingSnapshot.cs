using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>O resultado das seis verificações, coletado de uma vez para ser cruzado.</summary>
public sealed record PrintingSnapshot(
    CheckResult<ServiceInfo> Spooler,
    CheckResult<IReadOnlyList<PrinterAssessment>> PrinterState,
    CheckResult<IReadOnlyList<PrinterQueueSummary>> Queue,
    CheckResult<IReadOnlyList<PrintDeviceInfo>> DeviceError,
    CheckResult<DefaultPrinterFacts> DefaultPrinter,
    CheckResult<IReadOnlyList<PrinterPortInfo>> Port)
{
    /// <summary>Todas as verificações, com o prefixo usado nos ids das evidências.</summary>
    internal IEnumerable<(string Key, string Label, CheckVerdict Verdict, string Summary,
        IReadOnlyList<Evidence> Evidence, IReadOnlyList<DataSourceResult> Sources, IReadOnlyList<string> Limitations)> Parts()
    {
        yield return ("spooler", "Serviço de impressão", Spooler.Verdict, Spooler.Summary, Spooler.Evidence, Spooler.Sources, Spooler.Limitations);
        yield return ("state", "Estado das impressoras", PrinterState.Verdict, PrinterState.Summary, PrinterState.Evidence, PrinterState.Sources, PrinterState.Limitations);
        yield return ("queue", "Fila de impressão", Queue.Verdict, Queue.Summary, Queue.Evidence, Queue.Sources, Queue.Limitations);
        yield return ("device", "Dispositivos de impressão", DeviceError.Verdict, DeviceError.Summary, DeviceError.Evidence, DeviceError.Sources, DeviceError.Limitations);
        yield return ("default", "Impressora padrão", DefaultPrinter.Verdict, DefaultPrinter.Summary, DefaultPrinter.Evidence, DefaultPrinter.Sources, DefaultPrinter.Limitations);
        yield return ("port", "Portas", Port.Verdict, Port.Summary, Port.Evidence, Port.Sources, Port.Limitations);
    }
}

/// <summary>Roda as seis verificações, uma de cada vez (a narração sai na ordem), e devolve o conjunto.</summary>
public sealed class PrintingSnapshotCollector(
    ICheck<ServiceInfo> spooler,
    ICheck<IReadOnlyList<PrinterAssessment>> printerState,
    ICheck<IReadOnlyList<PrinterQueueSummary>> queue,
    ICheck<IReadOnlyList<PrintDeviceInfo>> deviceError,
    ICheck<DefaultPrinterFacts> defaultPrinter,
    ICheck<IReadOnlyList<PrinterPortInfo>> port)
{
    public static PrintingSnapshotCollector Create(IPrintSystemProvider system, IPrintDeviceProvider devices) =>
        new(new SpoolerCheck(system), new PrinterStateCheck(system), new QueueCheck(system),
            new DeviceErrorCheck(devices), new DefaultPrinterCheck(system), new PortCheck(system));

    public async Task<PrintingSnapshot> CollectAsync(IProgress<DiagnosticProgress> progress, CancellationToken cancellationToken) =>
        new(
            await spooler.RunAsync(progress, cancellationToken),
            await printerState.RunAsync(progress, cancellationToken),
            await queue.RunAsync(progress, cancellationToken),
            await deviceError.RunAsync(progress, cancellationToken),
            await defaultPrinter.RunAsync(progress, cancellationToken),
            await port.RunAsync(progress, cancellationToken));
}
