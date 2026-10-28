using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Platform.Windows;

/// <summary>
/// Lê o estado do sistema de impressão (serviço, fila, impressoras) via WMI.
/// Somente leitura: só consultas SELECT, pedindo apenas as colunas necessárias
/// (nunca nomes de documentos).
/// </summary>
public sealed class WmiPrintSystemProvider : IPrintSystemProvider
{
    private const string SpoolerQuery = "SELECT Name, State, StartMode FROM Win32_Service WHERE Name = 'Spooler'";

    public Task<SourceResult<ServiceInfo>> GetSpoolerServiceAsync(CancellationToken cancellationToken) =>
        Task.Run(() => WmiQuery.Run(SpoolerQuery, cancellationToken, o => new ServiceInfo(
            o["Name"]?.ToString() ?? "Spooler",
            ServiceStateParser.Parse(o["State"]?.ToString()),
            o["StartMode"]?.ToString())),
            cancellationToken);
}
