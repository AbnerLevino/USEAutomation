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
    /// O fluxo guiado "Impressora não imprime". Ainda não há correção real cadastrada (fase F): o catálogo vazio
    /// faz o fluxo mostrar a causa e a orientação, esperar o usuário agir e reverificar. A auditoria fica em memória.
    /// </summary>
    public static GuidedNotPrintingFlow CreateGuidedFlow(IUserInteraction user) => new(
        DemoName is null
            ? PrintingComposition.CreateNotPrintingScenario()
            : PrintingComposition.CreateDemoNotPrintingScenario(DemoName)
              ?? throw new InvalidOperationException(
                  $"Cenário de demonstração desconhecido: '{DemoName}'. Use um destes: {string.Join(", ", DemoPrintingProvider.Scenarios.Keys)}."),
        new ActionCatalog([]), new RemediationPipeline(new InMemoryAuditLog()), user);

    private static string? ReadDemoName()
    {
        var args = Environment.GetCommandLineArgs();
        var at = Array.FindIndex(args, a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : Environment.GetEnvironmentVariable("ASSISTENTE_DEMO");
    }
}
