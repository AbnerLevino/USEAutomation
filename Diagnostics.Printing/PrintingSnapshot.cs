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
        yield return (PrintingSteps.Spooler.Key, PrintingSteps.Spooler.Label, Spooler.Verdict, Spooler.Summary, Spooler.Evidence, Spooler.Sources, Spooler.Limitations);
        yield return (PrintingSteps.PrinterState.Key, PrintingSteps.PrinterState.Label, PrinterState.Verdict, PrinterState.Summary, PrinterState.Evidence, PrinterState.Sources, PrinterState.Limitations);
        yield return (PrintingSteps.Queue.Key, PrintingSteps.Queue.Label, Queue.Verdict, Queue.Summary, Queue.Evidence, Queue.Sources, Queue.Limitations);
        yield return (PrintingSteps.DeviceError.Key, PrintingSteps.DeviceError.Label, DeviceError.Verdict, DeviceError.Summary, DeviceError.Evidence, DeviceError.Sources, DeviceError.Limitations);
        yield return (PrintingSteps.DefaultPrinter.Key, PrintingSteps.DefaultPrinter.Label, DefaultPrinter.Verdict, DefaultPrinter.Summary, DefaultPrinter.Evidence, DefaultPrinter.Sources, DefaultPrinter.Limitations);
        yield return (PrintingSteps.Port.Key, PrintingSteps.Port.Label, Port.Verdict, Port.Summary, Port.Evidence, Port.Sources, Port.Limitations);
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

    /// <param name="events">Quando informado, recebe uma etapa por verificação e as impressoras achadas, para a tela desenhar.</param>
    public async Task<PrintingSnapshot> CollectAsync(
        IProgress<DiagnosticProgress> progress, CancellationToken cancellationToken, IProgress<GuidedEvent>? events = null)
    {
        var spoolerResult = await Step(PrintingSteps.Spooler, spooler, progress, events, cancellationToken);
        var stateResult = await Step(PrintingSteps.PrinterState, printerState, progress, events, cancellationToken);
        events?.Report(new PrintersFound(CardsFrom(stateResult)));
        var queueResult = await Step(PrintingSteps.Queue, queue, progress, events, cancellationToken);
        var deviceResult = await Step(PrintingSteps.DeviceError, deviceError, progress, events, cancellationToken);
        var defaultResult = await Step(PrintingSteps.DefaultPrinter, defaultPrinter, progress, events, cancellationToken);
        var portResult = await Step(PrintingSteps.Port, port, progress, events, cancellationToken);
        return new(spoolerResult, stateResult, queueResult, deviceResult, defaultResult, portResult);
    }

    private static async Task<CheckResult<T>> Step<T>(
        (string Key, string Label) step, ICheck<T> check,
        IProgress<DiagnosticProgress> progress, IProgress<GuidedEvent>? events, CancellationToken cancellationToken)
    {
        events?.Report(new StepStarted(step.Key, step.Label));
        var result = await check.RunAsync(progress, cancellationToken);
        events?.Report(new StepFinished(step.Key, step.Label, result.Verdict, result.Summary));
        return result;
    }

    /// <summary>Os cartões iniciais: o estado vem só do que o Windows reportou sobre cada fila.</summary>
    private static List<PrinterCard> CardsFrom(CheckResult<IReadOnlyList<PrinterAssessment>> state) =>
        (state.Facts ?? []).Select(a => new PrinterCard(
            a.Printer.Name, a.IsVirtual, a.Printer.IsDefault,
            a.HasIssue ? PrinterCardState.Problem : PrinterCardState.Unknown,
            a.Paused ? "pausada" : a.Offline ? "offline" : a.EquipmentError ?? "ainda verificando")).ToList();
}

/// <summary>Chave e nome de cada verificação. Usados no relatório (prefixo das evidências) e nos eventos da tela.</summary>
internal static class PrintingSteps
{
    public static readonly (string Key, string Label) Spooler = ("spooler", "Serviço de impressão");
    public static readonly (string Key, string Label) PrinterState = ("state", "Estado das impressoras");
    public static readonly (string Key, string Label) Queue = ("queue", "Fila de impressão");
    public static readonly (string Key, string Label) DeviceError = ("device", "Dispositivos de impressão");
    public static readonly (string Key, string Label) DefaultPrinter = ("default", "Impressora padrão");
    public static readonly (string Key, string Label) Port = ("port", "Portas");
}
