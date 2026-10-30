using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Tests;

/// <summary>Recolhe os eventos do fluxo guiado na ordem em que chegam (sem o atraso do Progress&lt;T&gt;).</summary>
internal sealed class EventLog : IProgress<GuidedEvent>
{
    public List<GuidedEvent> Events { get; } = [];
    public void Report(GuidedEvent value) => Events.Add(value);
    public IEnumerable<T> Of<T>() where T : GuidedEvent => Events.OfType<T>();
}

internal sealed class NullDiagnosticProgress : IProgress<DiagnosticProgress>
{
    public void Report(DiagnosticProgress value) { }
}

/// <summary>
/// Um computador falso para o fluxo guiado: por padrão tudo saudável, com uma impressora USB padrão.
/// Os testes mudam o que precisam e podem mudar de novo no meio do fluxo (para simular o conserto).
/// </summary>
internal sealed class GuidedWorld
{
    public FakePrintSystemProvider Sys { get; } = new();
    public FakePrintDeviceProvider Dev { get; } = new();

    public GuidedWorld()
    {
        SpoolerIs(ServiceState.Running);
        Printers(Usb("Impressora X", isDefault: true));
        Dev.Devices = SourceResult<PrintDeviceInfo>.Ok([new PrintDeviceInfo("Impressora X", @"USB\VID_1", 0)]);
    }

    public static PrinterStateInfo Usb(string name, bool isDefault = false, bool offline = false, int extended = 3, int error = 2, string? port = "USB001") =>
        new(name, port, offline, extended, error, isDefault);

    public static PrinterStateInfo Pdf(bool isDefault = false) =>
        new("Microsoft Print to PDF", "PORTPROMPT:", false, 2, 0, isDefault);

    public GuidedWorld Printers(params PrinterStateInfo[] p) { Sys.PrinterStates = SourceResult<PrinterStateInfo>.Ok(p); return this; }
    public GuidedWorld SpoolerIs(ServiceState state) { Sys.Spooler = SourceResult<ServiceInfo>.Ok([new ServiceInfo("Spooler", state, "Auto")]); return this; }

    public NotPrintingScenario Scenario() => NotPrintingScenario.Create(Sys, Dev);
}

/// <summary>Ação falsa que "conserta" o computador falso quando executa.</summary>
internal sealed class FixAction(string id, Action fix) : IRemediationAction
{
    public bool Executed { get; private set; }
    public PreconditionResult Precondition { get; set; } = PreconditionResult.Ok;
    public VerificationResult Verification { get; set; } = new(VerificationOutcome.Resolved, "a leitura nova confirma o conserto");

    public string Id => id;
    public string DisplayName => "Retomar a impressora";
    public ActionLevel Level => ActionLevel.N1;
    public string DescribeForConfirmation(ActionTarget target) => $"Vou retomar '{target.PrinterName}'.";
    public Task<PreconditionResult> CheckPreconditionAsync(ActionTarget target, CancellationToken ct) => Task.FromResult(Precondition);
    public Task<ExecutionResult> ExecuteAsync(ActionTarget target, CancellationToken ct)
    {
        Executed = true;
        fix();
        return Task.FromResult(new ExecutionResult(ExecutionOutcome.Done));
    }
    public Task<VerificationResult> VerifyAsync(ActionTarget target, CancellationToken ct) => Task.FromResult(Verification);
}
