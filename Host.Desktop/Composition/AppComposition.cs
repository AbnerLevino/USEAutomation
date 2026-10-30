using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;
using AutomationPlatform.Host.Desktop.Chat;
using AutomationPlatform.Platform.Windows;

namespace AutomationPlatform.Host.Desktop.Composition;

/// <summary>
/// Onde a janela decide com o que vai trabalhar: o Windows de verdade ou dados falsos (modo demonstração),
/// e em que ritmo a conversa corre. Só leitura de argumentos e variáveis de ambiente; sem tela.
/// </summary>
public static class AppComposition
{
    /// <summary>Modo demonstração: "--demo &lt;cenario&gt;" (ou ASSISTENTE_DEMO) troca o Windows por dados falsos. Null = Windows real.</summary>
    public static string? DemoName { get; } = ReadDemoName();

    /// <summary>ASSISTENTE_RITMO=instantaneo existe só para os testes automáticos da janela.</summary>
    public static ChatPacing Pacing { get; } =
        Environment.GetEnvironmentVariable("ASSISTENTE_RITMO") == "instantaneo" ? ChatPacing.Instant : ChatPacing.Natural;

    public static DiagnosticRegistry CreateRegistry() =>
        DemoName is null
            ? PrintingComposition.CreateRegistry()
            : PrintingComposition.CreateDemoRegistry(DemoName)
              ?? throw new InvalidOperationException(
                  $"Cenário de demonstração desconhecido: '{DemoName}'. Use um destes: {string.Join(", ", DemoPrintingProvider.Scenarios.Keys)}.");

    /// <summary>
    /// O fluxo guiado "Impressora não imprime", com as correções que a automação já sabe fazer (hoje: retomar a
    /// impressora pausada). As causas sem correção disponível vão para o suporte. A auditoria fica em memória.
    /// </summary>
    public static GuidedNotPrintingFlow CreateGuidedFlow(IUserInteraction user)
    {
        var parts = DemoName is null
            ? PrintingComposition.CreateGuidedParts()
            : PrintingComposition.CreateDemoGuidedParts(DemoName)
              ?? throw new InvalidOperationException(
                  $"Cenário de demonstração desconhecido: '{DemoName}'. Use um destes: {string.Join(", ", DemoPrintingProvider.Scenarios.Keys)}.");
        return new GuidedNotPrintingFlow(parts.Scenario, new ActionCatalog(parts.Actions),
            new RemediationPipeline(new InMemoryAuditLog()), user);
    }

    private static string? ReadDemoName()
    {
        var args = Environment.GetCommandLineArgs();
        var at = Array.FindIndex(args, a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : Environment.GetEnvironmentVariable("ASSISTENTE_DEMO");
    }
}
