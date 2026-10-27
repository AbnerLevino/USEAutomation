using System.Management;
using System.Runtime.InteropServices;
using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Platform.Windows;

/// <summary>
/// Lê dispositivos e filas de impressão via WMI. Somente leitura: só executa consultas SELECT
/// e não coleta documentos da fila nem dados pessoais.
/// </summary>
public sealed class WmiPrintDeviceProvider : IPrintDeviceProvider
{
    // PNPClass Printer/PrintQueue cobre impressoras; Service usbprint cobre o suporte USB de impressão.
    private const string DevicesQuery =
        "SELECT Name, DeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity " +
        "WHERE PNPClass = 'Printer' OR PNPClass = 'PrintQueue' OR Service = 'usbprint'";

    private const string PrintersQuery = "SELECT Name, PortName FROM Win32_Printer";

    public Task<SourceResult<PrintDeviceInfo>> GetPrintDevicesAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Query(DevicesQuery, cancellationToken, o => new PrintDeviceInfo(
            o["Name"]?.ToString() ?? "(sem nome)",
            o["DeviceID"]?.ToString() ?? "(sem id)",
            o["ConfigManagerErrorCode"] is null ? null : Convert.ToInt32(o["ConfigManagerErrorCode"]))),
            cancellationToken);

    public Task<SourceResult<InstalledPrinterInfo>> GetInstalledPrintersAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Query(PrintersQuery, cancellationToken,
            o => new InstalledPrinterInfo(o["Name"]?.ToString() ?? "(sem nome)", o["PortName"]?.ToString())),
            cancellationToken);

    private static SourceResult<T> Query<T>(string wql, CancellationToken ct, Func<ManagementBaseObject, T> map)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            using var results = searcher.Get();
            var items = new List<T>();
            foreach (ManagementBaseObject obj in results)
            {
                ct.ThrowIfCancellationRequested();
                using (obj) items.Add(map(obj));
            }
            return SourceResult<T>.Ok(items);
        }
        catch (UnauthorizedAccessException ex)
        {
            return SourceResult<T>.Fail(SourceStatus.PermissionDenied, ex.Message);
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
        {
            return SourceResult<T>.Fail(SourceStatus.PermissionDenied, ex.Message);
        }
        catch (ManagementException ex)
        {
            return SourceResult<T>.Fail(SourceStatus.Unavailable, $"{ex.ErrorCode}: {ex.Message}");
        }
        catch (COMException ex)
        {
            return SourceResult<T>.Fail(SourceStatus.Unavailable, ex.Message);
        }
    }
}
