using AutomationPlatform.Contracts;

namespace AutomationPlatform.Host.Desktop.Chat;

public enum ChatKind
{
    /// <summary>Uma fala do computador enquanto trabalha ("Vou conferir o Spooler…").</summary>
    Narration,
    /// <summary>O cartão final com a conclusão.</summary>
    Conclusion
}

public enum ChatTone { Good, Problem, Neutral, Error }

/// <param name="Text">Fala da narração, ou o título da conclusão.</param>
/// <param name="Detail">Só na conclusão: o resumo do que ele concluiu.</param>
/// <param name="Lines">Só na conclusão: achados, consequências e próximos passos, um por linha.</param>
public sealed record ChatMessage(
    ChatKind Kind, string Text, ChatTone Tone = ChatTone.Neutral,
    string? Detail = null, IReadOnlyList<string>? Lines = null);

/// <summary>Traduz o relatório do Engine para o cartão de conclusão do chat. Sem tela: dá para testar.</summary>
public static class ReportToChat
{
    public static ChatMessage Conclusion(DiagnosticReport report)
    {
        if (report.ExecutionStatus != ExecutionStatus.Completed)
            return new ChatMessage(ChatKind.Conclusion, "A verificação não terminou", ChatTone.Error,
                report.Summary, report.ErrorMessage is null ? null : [report.ErrorMessage]);

        var (title, tone) = report.OverallStatus switch
        {
            OverallStatus.ProblemConfirmed => ("Encontrei o problema", ChatTone.Problem),
            OverallStatus.ProblemProbable => ("Encontrei um indício de problema", ChatTone.Problem),
            OverallStatus.NoProblemDetected => ("Nada de errado nas verificações feitas", ChatTone.Good),
            OverallStatus.Inconclusive => ("Não consegui concluir", ChatTone.Neutral),
            _ => ("A verificação não terminou", ChatTone.Error)
        };

        var lines = new List<string>();
        foreach (var f in report.Findings)
            lines.Add(f.Title == "Consequência" ? $"• {f.Description}" : $"• {f.Title}");
        foreach (var r in report.Recommendations.Where(r => !string.IsNullOrWhiteSpace(r.Text)))
            lines.Add($"→ {r.Text}");
        // O resumo de alguns diagnósticos já traz essa ressalva; não repetir.
        var alreadySaid = report.Summary.Contains("não prova", StringComparison.OrdinalIgnoreCase);
        if (report.OverallStatus is OverallStatus.NoProblemDetected or OverallStatus.Inconclusive && !alreadySaid)
            lines.Add("Isso não prova que a impressão funciona: só diz o que as verificações encontraram.");

        return new ChatMessage(ChatKind.Conclusion, title, tone, report.Summary, lines);
    }

    /// <summary>
    /// Conclusão do fluxo guiado: o resultado do ponto de vista do usuário (resolvido ou não) e em que ele
    /// se baseia. Sem fechamento (execução que falhou), cai na conclusão comum.
    /// </summary>
    public static ChatMessage GuidedConclusion(DiagnosticReport report)
    {
        var common = Conclusion(report);
        if (report.ExecutionStatus != ExecutionStatus.Completed || report.Closing is not { } closing)
            return common;

        var (title, tone) = closing.Result switch
        {
            FlowResult.Resolved => ("Resolvido", ChatTone.Good),
            FlowResult.NotResolved => ("Ainda não está resolvido", ChatTone.Problem),
            FlowResult.Cancelled => ("Cancelado: nada foi alterado", ChatTone.Neutral),
            _ => ("Não consegui concluir", ChatTone.Neutral)
        };

        // O cartão é para quem não é técnico: o resultado, em que ele se baseia e, se não resolveu, para onde ir.
        // Os detalhes técnicos (causas, evidências, ações) ficam no relatório, não na tela.
        var lines = new List<string>();
        var basis = closing.Basis switch
        {
            ClosingBasis.UserAnswer => "Considerei a sua resposta.",
            ClosingBasis.ObjectiveVerification => "Considerei só o que o computador mostrou, sem um teste seu.",
            _ => null
        };
        if (basis is not null) lines.Add(basis);
        if (closing.Result is FlowResult.NotResolved or FlowResult.Inconclusive)
            lines.Add("Se precisar, procure o suporte: os detalhes ficam registrados no relatório.");

        return new ChatMessage(ChatKind.Conclusion, title, tone, closing.Message, lines);
    }
}
