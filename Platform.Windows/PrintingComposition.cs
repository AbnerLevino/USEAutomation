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

    /// <summary>
    /// O que o fluxo guiado usa: o cenário "Impressora não imprime" e as correções que a automação sabe fazer.
    /// Os dois leem da MESMA fonte, senão a verificação posterior de uma correção não enxergaria o efeito dela.
    /// </summary>
    public sealed record GuidedParts(NotPrintingScenario Scenario, IReadOnlyList<IRemediationAction> Actions);

    /// <summary>Sobre o Windows de verdade.</summary>
    public static GuidedParts CreateGuidedParts() =>
        CreateGuidedParts(new WmiPrintSystemProvider(), new WmiPrintDeviceProvider(), new WmiPrinterControl());

    /// <summary>Sobre dados falsos (modo demonstração: até as correções só mudam os dados falsos); null se o cenário não existe.</summary>
    public static GuidedParts? CreateDemoGuidedParts(string scenario) =>
        DemoPrintingProvider.Create(scenario) is { } demo ? CreateGuidedParts(demo, demo, demo) : null;

    private static GuidedParts CreateGuidedParts(IPrintSystemProvider system, IPrintDeviceProvider devices, IPrinterControl control) =>
        new(NotPrintingScenario.Create(system, devices),
            [new ResumePrinterAction(system, control), new PrintTestPageAction(system, control)]);

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
