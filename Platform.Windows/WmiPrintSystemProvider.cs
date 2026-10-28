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

    // Sem Document, Owner ou qualquer coluna que identifique conteúdo ou pessoa.
    private const string JobsQuery = "SELECT Name, StatusMask FROM Win32_PrintJob";

    private const string PrinterStatesQuery =
        "SELECT Name, PortName, WorkOffline, ExtendedPrinterStatus, DetectedErrorState, Default FROM Win32_Printer";

    public Task<SourceResult<ServiceInfo>> GetSpoolerServiceAsync(CancellationToken cancellationToken) =>
        Task.Run(() => WmiQuery.Run(SpoolerQuery, cancellationToken, o => new ServiceInfo(
            o["Name"]?.ToString() ?? "Spooler",
            ServiceStateParser.Parse(o["State"]?.ToString()),
            o["StartMode"]?.ToString())),
            cancellationToken);

    public Task<SourceResult<PrintJobInfo>> GetPrintJobsAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var raw = WmiQuery.Run(JobsQuery, cancellationToken, o => (
                Name: PrintJobNameParser.Parse(o["Name"]?.ToString()),
                Mask: o["StatusMask"] is null ? (uint?)null : Convert.ToUInt32(o["StatusMask"])));

            if (!raw.IsConsulted) return SourceResult<PrintJobInfo>.Fail(raw.Status, raw.Detail ?? "");

            // Um nome que não segue "fila, número" é descartado: melhor ignorar do que inventar a fila.
            var jobs = raw.Items
                .Where(j => j.Name is not null)
                .Select(j => new PrintJobInfo(j.Name!.Value.PrinterName, j.Name.Value.JobId, (PrintJobStatus?)j.Mask))
                .ToList();
            return SourceResult<PrintJobInfo>.Ok(jobs);
        }, cancellationToken);

    public Task<SourceResult<PrinterStateInfo>> GetPrinterStatesAsync(CancellationToken cancellationToken) =>
        Task.Run(() => WmiQuery.Run(PrinterStatesQuery, cancellationToken, o => new PrinterStateInfo(
            o["Name"]?.ToString() ?? "(sem nome)",
            o["PortName"]?.ToString(),
            o["WorkOffline"] is null ? null : Convert.ToBoolean(o["WorkOffline"]),
            o["ExtendedPrinterStatus"] is null ? null : Convert.ToInt32(o["ExtendedPrinterStatus"]),
            o["DetectedErrorState"] is null ? null : Convert.ToInt32(o["DetectedErrorState"]),
            o["Default"] is null ? null : Convert.ToBoolean(o["Default"]))),
            cancellationToken);
}
