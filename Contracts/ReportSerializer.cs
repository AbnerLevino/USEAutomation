using System.Text.Json;

namespace AutomationPlatform.Contracts;

/// <summary>Ponto único de (de)serialização, para o JSON sair sempre igual.</summary>
public static class ReportSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Mantém acentos legíveis (ç, ã) em vez de ç; o JSON não é embutido em HTML.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(DiagnosticReport report) =>
        JsonSerializer.Serialize(report, Options);

    public static DiagnosticReport Deserialize(string json) =>
        JsonSerializer.Deserialize<DiagnosticReport>(json, Options)
        ?? throw new JsonException("O JSON do relatório está vazio.");
}
