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

    private static string? ReadDemoName()
    {
        var args = Environment.GetCommandLineArgs();
        var at = Array.FindIndex(args, a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : Environment.GetEnvironmentVariable("ASSISTENTE_DEMO");
    }
}
