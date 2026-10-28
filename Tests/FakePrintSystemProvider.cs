using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Tests;

/// <summary>Provedor falso do sistema de impressão: devolve o que o teste mandar.</summary>
internal sealed class FakePrintSystemProvider : IPrintSystemProvider
{
    public SourceResult<ServiceInfo> Spooler { get; set; } = SourceResult<ServiceInfo>.Ok([]);
    public SourceResult<PrintJobInfo> Jobs { get; set; } = SourceResult<PrintJobInfo>.Ok([]);
    public SourceResult<PrinterStateInfo> PrinterStates { get; set; } = SourceResult<PrinterStateInfo>.Ok([]);

    public SourceResult<DefaultPrinterModeInfo> DefaultMode { get; set; } = SourceResult<DefaultPrinterModeInfo>.Ok([new DefaultPrinterModeInfo(false)]);
    public SourceResult<NetworkPortInfo> NetworkPorts { get; set; } = SourceResult<NetworkPortInfo>.Ok([]);

    public Task<SourceResult<DefaultPrinterModeInfo>> GetDefaultPrinterModeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(DefaultMode);

    public Task<SourceResult<NetworkPortInfo>> GetNetworkPortsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(NetworkPorts);

    public Task<SourceResult<ServiceInfo>> GetSpoolerServiceAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Spooler);

    public Task<SourceResult<PrintJobInfo>> GetPrintJobsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Jobs);

    public Task<SourceResult<PrinterStateInfo>> GetPrinterStatesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PrinterStates);
}
