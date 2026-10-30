using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;
using AutomationPlatform.Platform.Windows.Elevation;

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

    /// <summary>
    /// Sobre o Windows de verdade. As ações que exigem administrador passam pelo executor elevado, que só roda se estiver
    /// numa pasta protegida; para desenvolvimento, AUTOMAXION_DEV_ELEVATED=1 libera uma pasta comum (com aviso no resultado).
    /// </summary>
    public static GuidedParts CreateGuidedParts() =>
        CreateGuidedParts(new WmiPrintSystemProvider(), new WmiPrintDeviceProvider(), new WmiPrinterControl(), CreateElevatedExecutor());

    /// <summary>Sobre dados falsos (modo demonstração: até as correções, e a "aprovação do administrador", só mudam os dados falsos); null se o cenário não existe.</summary>
    public static GuidedParts? CreateDemoGuidedParts(string scenario) =>
        DemoPrintingProvider.Create(scenario) is { } demo ? CreateGuidedParts(demo, demo, demo, demo) : null;

    private static ElevatedProcessLauncher CreateElevatedExecutor() => new(
        ElevatedProcessLauncher.DefaultExecutablePath(), new WindowsElevatedProcessRunner(), new WindowsAclReader(),
        new ElevatedLaunchOptions(AllowUnprotectedFolder: Environment.GetEnvironmentVariable("AUTOMAXION_DEV_ELEVATED") == "1"));

    private static GuidedParts CreateGuidedParts(
        IPrintSystemProvider system, IPrintDeviceProvider devices, IPrinterControl control, IElevatedExecutor elevated) =>
        new(NotPrintingScenario.Create(system, devices),
            [new ResumePrinterAction(system, control), new PrintTestPageAction(system, control), new RestartSpoolerAction(system, elevated)]);

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
