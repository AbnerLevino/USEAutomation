using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>
/// Fonte de dados das verificações de impressão (serviço, fila, estado das impressoras).
/// Complementa <see cref="IPrintDeviceProvider"/>. A implementação real (WMI) fica em
/// Platform.Windows; os testes usam uma versão falsa. Falhas esperadas viram
/// <see cref="SourceResult{T}"/>, não exceção.
/// </summary>
public interface IPrintSystemProvider
{
    /// <summary>O serviço "Spooler" (Spooler de Impressão). Lista vazia = o Windows não o encontrou.</summary>
    Task<SourceResult<ServiceInfo>> GetSpoolerServiceAsync(CancellationToken cancellationToken);

    /// <summary>Trabalhos nas filas (somente impressora, id e estado; nunca o nome do documento).</summary>
    Task<SourceResult<PrintJobInfo>> GetPrintJobsAsync(CancellationToken cancellationToken);

    /// <summary>Estado de cada fila de impressão instalada.</summary>
    Task<SourceResult<PrinterStateInfo>> GetPrinterStatesAsync(CancellationToken cancellationToken);
}

/// <param name="PrinterName">Fila à qual o trabalho pertence.</param>
/// <param name="JobId">Número do trabalho.</param>
/// <param name="Status">Estado do trabalho; null = o Windows não informou.</param>
public sealed record PrintJobInfo(string PrinterName, int JobId, PrintJobStatus? Status);

/// <summary>Bits de estado de um trabalho (JOB_STATUS_* do Windows, campo StatusMask do WMI).</summary>
[Flags]
public enum PrintJobStatus : uint
{
    None = 0,
    Paused = 0x1,
    Error = 0x2,
    Deleting = 0x4,
    Spooling = 0x8,
    Printing = 0x10,
    Offline = 0x20,
    PaperOut = 0x40,
    Printed = 0x80,
    Deleted = 0x100,
    BlockedDeviceQueue = 0x200,
    UserIntervention = 0x400,
    Restarted = 0x800,
    Complete = 0x1000,
    Retained = 0x2000,
    RenderingLocally = 0x4000
}

/// <summary>Lê o nome do WMI de um trabalho ("Nome da fila, 12") e separa a fila do número.</summary>
public static class PrintJobNameParser
{
    public static (string PrinterName, int JobId)? Parse(string? wmiName)
    {
        if (wmiName is null) return null;
        var cut = wmiName.LastIndexOf(", ", StringComparison.Ordinal);
        if (cut <= 0 || !int.TryParse(wmiName.AsSpan(cut + 2), out var id)) return null;
        return (wmiName[..cut], id);
    }
}

/// <param name="Name">Nome da fila.</param>
/// <param name="PortName">Porta; null = não informada.</param>
/// <param name="WorkOffline">true = marcada como "usar impressora offline"; null = não informado.</param>
/// <param name="ExtendedStatus">ExtendedPrinterStatus do WMI (3 = ociosa, 7 = offline, 8 = pausada, 9 = erro); null = não informado.</param>
/// <param name="DetectedErrorState">DetectedErrorState do WMI (2 = sem erro, 3 = pouco papel ... 11 = bandeja cheia); null = não informado.</param>
/// <param name="IsDefault">true = impressora padrão; null = não informado.</param>
public sealed record PrinterStateInfo(
    string Name, string? PortName, bool? WorkOffline, int? ExtendedStatus, int? DetectedErrorState, bool? IsDefault);

/// <summary>Estado de um serviço do Windows.</summary>
public enum ServiceState
{
    Unknown,
    Stopped,
    StartPending,
    StopPending,
    Running,
    ContinuePending,
    PausePending,
    Paused
}

/// <param name="Name">Nome interno do serviço (ex.: Spooler).</param>
/// <param name="State">Estado atual.</param>
/// <param name="StartMode">Modo de início (Auto, Manual, Disabled...); null = não informado.</param>
public sealed record ServiceInfo(string Name, ServiceState State, string? StartMode = null);

/// <summary>Converte o texto que o WMI devolve ("Running", "Stop Pending"...) em <see cref="ServiceState"/>.</summary>
public static class ServiceStateParser
{
    public static ServiceState Parse(string? wmiState) => wmiState?.Trim().ToLowerInvariant() switch
    {
        "stopped" => ServiceState.Stopped,
        "start pending" => ServiceState.StartPending,
        "stop pending" => ServiceState.StopPending,
        "running" => ServiceState.Running,
        "continue pending" => ServiceState.ContinuePending,
        "pause pending" => ServiceState.PausePending,
        "paused" => ServiceState.Paused,
        _ => ServiceState.Unknown
    };
}
