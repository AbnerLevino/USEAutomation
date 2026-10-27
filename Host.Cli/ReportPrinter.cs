using AutomationPlatform.Contracts;

namespace AutomationPlatform.Host.Cli;

/// <summary>Mostra o relatório em texto corrido para quem está no terminal.</summary>
internal static class ReportPrinter
{
    public static void Print(DiagnosticReport report, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine($"Resumo: {report.Summary}");
        output.WriteLine($"Execução: {report.ExecutionStatus} | Conclusão: {report.OverallStatus}");

        if (report.ErrorMessage is not null)
            output.WriteLine($"Detalhe técnico: {report.ErrorMessage}");

        foreach (var f in report.Findings)
            output.WriteLine($"- [{f.Severity}/{f.Confidence}] {f.Title}: {f.Description}");

        if (report.Evidence.Count > 0)
        {
            output.WriteLine("O que o Windows informou:");
            foreach (var e in report.Evidence)
                output.WriteLine($"  - {e.Description}");
        }

        if (report.Sources.Count > 0)
        {
            output.WriteLine("Fontes consultadas:");
            foreach (var s in report.Sources)
                output.WriteLine($"  - {s.Name}: {s.Status}" + (s.Detail is null ? "" : $" ({s.Detail})"));
        }

        if (report.Limitations.Count > 0)
        {
            output.WriteLine("Limitações:");
            foreach (var l in report.Limitations)
                output.WriteLine($"  - {l}");
        }

        if (report.Recommendations.Count > 0)
        {
            output.WriteLine("Próximos passos sugeridos (nada foi alterado no seu computador):");
            foreach (var r in report.Recommendations)
                output.WriteLine($"  - {r.Text}");
        }
    }
}
