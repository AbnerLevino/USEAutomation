namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>
/// Separa o que é impressora "de verdade" do que é virtual. A decisão usa a PORTA da fila
/// (não o nome, que muda conforme o idioma do Windows).
/// </summary>
internal static class PrinterClassifier
{
    // Portas que não levam a um equipamento: gravam em arquivo, pedem o destino ao usuário, etc.
    private static readonly string[] VirtualPorts = ["PORTPROMPT:", "FILE:", "NUL:", "SHRFAX:", "XPSPort:"];

    /// <summary>true = fila virtual; false = porta física/de rede ou porta desconhecida (não presumimos virtual).</summary>
    public static bool IsVirtualQueue(InstalledPrinterInfo printer) =>
        printer.PortName is not null &&
        VirtualPorts.Contains(printer.PortName.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Dispositivo ligado por USB (sustenta que existe um equipamento conectado).</summary>
    public static bool IsUsbDevice(PrintDeviceInfo device) =>
        device.DeviceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) ||
        device.DeviceId.StartsWith("USBPRINT\\", StringComparison.OrdinalIgnoreCase);

    public static string DescribeDevice(PrintDeviceInfo device) =>
        IsUsbDevice(device) ? "USB" : "fila/dispositivo de sistema, sem conexão USB identificada";
}
