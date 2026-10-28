using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>Auxiliares comuns às verificações: narração e descrição do estado de uma fonte.</summary>
internal static class CheckProgress
{
    public static void Say(IProgress<DiagnosticProgress> progress, string message) =>
        progress.Report(new DiagnosticProgress(message, DateTimeOffset.UtcNow));

    public static string Describe(SourceStatus status) => status switch
    {
        SourceStatus.PermissionDenied => "permissão negada",
        SourceStatus.Unavailable => "fonte indisponível",
        SourceStatus.Failed => "falha na consulta",
        _ => "consultada"
    };
}
