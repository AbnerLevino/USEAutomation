using System.Management;
using System.Runtime.InteropServices;
using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Platform.Windows;

/// <summary>
/// As ordens que o programa dá ao Windows sobre filas de impressão, pelo WMI (Win32_Printer). É a única
/// classe que ALTERA algo do sistema de impressão. Cada método chama UM método fixo do WMI com o nome da
/// fila já escapado; nenhum texto vindo de fora vira comando.
/// </summary>
public sealed class WmiPrinterControl : IPrinterControl
{
    public Task<ControlResult> ResumeAsync(string printerName, CancellationToken cancellationToken) =>
        Task.Run(() => Invoke(printerName, "Resume", cancellationToken), cancellationToken);

    public Task<ControlResult> PrintTestPageAsync(string printerName, CancellationToken cancellationToken) =>
        Task.Run(() => Invoke(printerName, "PrintTestPage", cancellationToken), cancellationToken);

    private static ControlResult Invoke(string printerName, string method, CancellationToken ct)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Printer WHERE Name = '{Escape(printerName)}'");
            using var results = searcher.Get();
            foreach (ManagementObject printer in results)
            {
                ct.ThrowIfCancellationRequested();
                using (printer)
                {
                    var code = Convert.ToUInt32(printer.InvokeMethod(method, null), System.Globalization.CultureInfo.InvariantCulture);
                    return code == 0
                        ? new ControlResult(true)
                        : new ControlResult(false, $"o Windows respondeu com o código {code}.", AccessDenied: code == 5);
                }
            }
            return new ControlResult(false, $"não encontrei a impressora '{printerName}'.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new ControlResult(false, ex.Message, AccessDenied: true);
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
        {
            return new ControlResult(false, ex.Message, AccessDenied: true);
        }
        catch (ManagementException ex)
        {
            return new ControlResult(false, $"{ex.ErrorCode}: {ex.Message}");
        }
        catch (COMException ex)
        {
            return new ControlResult(false, ex.Message);
        }
    }

    /// <summary>Escapa o texto para um literal WQL entre aspas simples (barra invertida e aspas).</summary>
    public static string Escape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}
