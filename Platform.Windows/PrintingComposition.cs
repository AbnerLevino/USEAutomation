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
    public static DiagnosticRegistry CreateRegistry() =>
        CreateRegistry(new WmiPrintSystemProvider(), new WmiPrintDeviceProvider());

    /// <summary>Mesmos diagnósticos sobre dados falsos (modo demonstração); null se o cenário não existe.</summary>
    public static DiagnosticRegistry? CreateDemoRegistry(string scenario) =>
        DemoPrintingProvider.Create(scenario) is { } demo ? CreateRegistry(demo, demo) : null;

    /// <summary>O cenário "Impressora não imprime" sobre o Windows de verdade, para o fluxo guiado.</summary>
    public static NotPrintingScenario CreateNotPrintingScenario() =>
        NotPrintingScenario.Create(new WmiPrintSystemProvider(), new WmiPrintDeviceProvider());

    /// <summary>O mesmo cenário sobre dados falsos (modo demonstração); null se o cenário de demonstração não existe.</summary>
    public static NotPrintingScenario? CreateDemoNotPrintingScenario(string scenario) =>
        DemoPrintingProvider.Create(scenario) is { } demo ? NotPrintingScenario.Create(demo, demo) : null;

    private static DiagnosticRegistry CreateRegistry(IPrintSystemProvider systemProvider, IPrintDeviceProvider deviceProvider)
    {
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
