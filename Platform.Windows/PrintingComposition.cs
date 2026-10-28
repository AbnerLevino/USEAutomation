using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;

namespace AutomationPlatform.Platform.Windows;

/// <summary>
/// Monta os diagnósticos de impressão com as fontes de dados reais do Windows (WMI).
/// Um só lugar para o terminal e a janela, assim os dois oferecem exatamente os mesmos diagnósticos.
/// </summary>
public static class PrintingComposition
{
    public static DiagnosticRegistry CreateRegistry()
    {
        var systemProvider = new WmiPrintSystemProvider();
        var deviceProvider = new WmiPrintDeviceProvider();

        var registry = new DiagnosticRegistry();
        registry.Register(NotPrintingScenario.Create(systemProvider, deviceProvider));
        registry.Register(FullPrintingDiagnostic.Create(systemProvider, deviceProvider));
        registry.Register(new PrinterRecognitionDiagnostic(deviceProvider));
        registry.Register(new CheckDiagnostic<IReadOnlyList<PrintDeviceInfo>>(new DeviceErrorCheck(deviceProvider)));
        registry.Register(new CheckDiagnostic<DefaultPrinterFacts>(new DefaultPrinterCheck(systemProvider)));
        registry.Register(new CheckDiagnostic<IReadOnlyList<PrinterPortInfo>>(new PortCheck(systemProvider)));
        registry.Register(new CheckDiagnostic<ServiceInfo>(new SpoolerCheck(systemProvider)));
        registry.Register(new CheckDiagnostic<IReadOnlyList<PrinterQueueSummary>>(new QueueCheck(systemProvider)));
        registry.Register(new CheckDiagnostic<IReadOnlyList<PrinterAssessment>>(new PrinterStateCheck(systemProvider)));
        return registry;
    }
}
