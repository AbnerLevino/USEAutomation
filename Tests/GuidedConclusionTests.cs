using AutomationPlatform.Contracts;
using AutomationPlatform.Host.Desktop.Chat;

namespace AutomationPlatform.Tests;

public class GuidedConclusionTests
{
    private static DiagnosticReport Report(FlowClosing? closing, ExecutionStatus status = ExecutionStatus.Completed) => new()
    {
        DiagnosticId = "printing.not-printing",
        ExecutionStatus = status,
        OverallStatus = OverallStatus.ProblemConfirmed,
        StartedAtUtc = DateTimeOffset.UtcNow,
        CompletedAtUtc = DateTimeOffset.UtcNow,
        Summary = "Resumo.",
        Findings = [new Finding { Id = "f-1", Title = "Causa provável: fila pausada", Description = "d", Severity = Severity.High, Confidence = Confidence.Confirmed }],
        Recommendations = [new Recommendation { Text = "Retome a fila." }],
        Closing = closing
    };

    [Theory]
    [InlineData(FlowResult.Resolved, "Resolvido", ChatTone.Good)]
    [InlineData(FlowResult.NotResolved, "Ainda não está resolvido", ChatTone.Problem)]
    [InlineData(FlowResult.Cancelled, "Cancelado: nada foi alterado", ChatTone.Neutral)]
    [InlineData(FlowResult.Inconclusive, "Não consegui concluir", ChatTone.Neutral)]
    public void Titulo_e_tom_seguem_o_resultado_do_fluxo(FlowResult result, string title, ChatTone tone)
    {
        var message = ReportToChat.GuidedConclusion(Report(new FlowClosing(result, ClosingBasis.None, "texto")));

        Assert.Equal(title, message.Text);
        Assert.Equal(tone, message.Tone);
        Assert.Equal("texto", message.Detail);
    }

    [Fact]
    public void Conclusao_diz_em_que_se_baseia()
    {
        var user = ReportToChat.GuidedConclusion(Report(new FlowClosing(FlowResult.Resolved, ClosingBasis.UserAnswer, "x")));
        var objective = ReportToChat.GuidedConclusion(Report(new FlowClosing(FlowResult.Resolved, ClosingBasis.ObjectiveVerification, "x")));

        Assert.Contains(user.Lines!, l => l.Contains("sua resposta"));
        Assert.Contains(objective.Lines!, l => l.Contains("só na verificação do sistema"));
    }

    [Fact]
    public void Quando_resolvido_as_orientacoes_de_correcao_somem_mas_o_que_foi_achado_fica()
    {
        var message = ReportToChat.GuidedConclusion(Report(new FlowClosing(FlowResult.Resolved, ClosingBasis.UserAnswer, "x")));

        Assert.DoesNotContain(message.Lines!, l => l.StartsWith("→"));
        Assert.Contains(message.Lines!, l => l.Contains("fila pausada"));
    }

    [Fact]
    public void Quando_nao_resolvido_as_orientacoes_continuam()
    {
        var message = ReportToChat.GuidedConclusion(Report(new FlowClosing(FlowResult.NotResolved, ClosingBasis.None, "x")));

        Assert.Contains(message.Lines!, l => l.StartsWith("→ Retome a fila"));
    }

    [Fact]
    public void Execucao_que_falhou_cai_na_conclusao_comum_de_erro()
    {
        var message = ReportToChat.GuidedConclusion(Report(null, ExecutionStatus.TimedOut));

        Assert.Equal("A verificação não terminou", message.Text);
        Assert.Equal(ChatTone.Error, message.Tone);
    }
}
