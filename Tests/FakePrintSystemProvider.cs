using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Tests;

/// <summary>Provedor falso do sistema de impressão: devolve o que o teste mandar.</summary>
internal sealed class FakePrintSystemProvider : IPrintSystemProvider
{
    public SourceResult<ServiceInfo> Spooler { get; set; } = SourceResult<ServiceInfo>.Ok([]);

    public Task<SourceResult<ServiceInfo>> GetSpoolerServiceAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Spooler);
}
