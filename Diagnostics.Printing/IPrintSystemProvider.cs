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
}

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
