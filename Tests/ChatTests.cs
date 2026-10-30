using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;
using AutomationPlatform.Host.Desktop.Chat;

namespace AutomationPlatform.Tests;

public class ChatTests
{
    private static DiagnosticReport Report(
        OverallStatus overall, ExecutionStatus execution = ExecutionStatus.Completed,
        IReadOnlyList<Finding>? findings = null, IReadOnlyList<Recommendation>? recs = null, string? error = null) => new()
    {
        DiagnosticId = "x",
        ExecutionStatus = execution,
        OverallStatus = overall,
        StartedAtUtc = DateTimeOffset.UtcNow,
        CompletedAtUtc = DateTimeOffset.UtcNow,
        Summary = "resumo",
        Findings = findings ?? [],
        Recommendations = recs ?? [],
        ErrorMessage = error
    };

    private static Finding F(string title, string description = "d") => new()
    {
        Id = "f", Title = title, Description = description, Severity = Severity.High, Confidence = Confidence.Confirmed
    };

    [Theory]
    [InlineData(OverallStatus.ProblemConfirmed, "Encontrei o problema", ChatTone.Problem)]
    [InlineData(OverallStatus.ProblemProbable, "Encontrei um indício de problema", ChatTone.Problem)]
    [InlineData(OverallStatus.NoProblemDetected, "Nada de errado nas verificações feitas", ChatTone.Good)]
    [InlineData(OverallStatus.Inconclusive, "Não consegui concluir", ChatTone.Neutral)]
    public void Conclusao_tem_titulo_e_tom_conforme_o_resultado(OverallStatus overall, string title, ChatTone tone)
    {
        var message = ReportToChat.Conclusion(Report(overall));

        Assert.Equal(ChatKind.Conclusion, message.Kind);
        Assert.Equal(title, message.Text);
        Assert.Equal(tone, message.Tone);
        Assert.Equal("resumo", message.Detail);
    }

    [Theory]
    [InlineData(OverallStatus.NoProblemDetected)]
    [InlineData(OverallStatus.Inconclusive)]
    public void Sem_problema_ou_inconclusivo_sempre_avisa_que_nao_prova_o_funcionamento(OverallStatus overall)
    {
        var message = ReportToChat.Conclusion(Report(overall));

        Assert.Contains(message.Lines!, l => l.Contains("não prova que a impressão funciona"));
    }

    [Fact]
    public void Nao_repete_a_ressalva_quando_o_resumo_ja_a_traz()
    {
        var report = Report(OverallStatus.NoProblemDetected) with { Summary = "Tudo ok; isso não prova que a impressão funciona." };

        var message = ReportToChat.Conclusion(report);

        Assert.DoesNotContain(message.Lines!, l => l.Contains("não prova"));
        Assert.Contains("não prova", message.Detail);
    }

    [Fact]
    public void Com_problema_nao_acrescenta_o_aviso_generico()
    {
        var message = ReportToChat.Conclusion(Report(OverallStatus.ProblemConfirmed));

        Assert.DoesNotContain(message.Lines!, l => l.Contains("não prova"));
    }

    [Fact]
    public void Achados_consequencias_e_proximos_passos_viram_linhas_na_ordem()
    {
        var message = ReportToChat.Conclusion(Report(OverallStatus.ProblemConfirmed,
            findings: [F("Causa provável: fila pausada"), F("Consequência", "1 trabalho parado")],
            recs: [new Recommendation { Text = "Retome a fila." }]));

        Assert.Equal(["• Causa provável: fila pausada", "• 1 trabalho parado", "→ Retome a fila."], message.Lines!.ToArray());
    }

    [Theory]
    [InlineData(ExecutionStatus.Failed)]
    [InlineData(ExecutionStatus.PermissionDenied)]
    [InlineData(ExecutionStatus.TimedOut)]
    [InlineData(ExecutionStatus.Cancelled)]
    public void Execucao_que_nao_terminou_e_erro_e_mostra_o_motivo_tecnico(ExecutionStatus execution)
    {
        var message = ReportToChat.Conclusion(Report(OverallStatus.NotEvaluated, execution, error: "detalhe técnico"));

        Assert.Equal(ChatTone.Error, message.Tone);
        Assert.Equal("A verificação não terminou", message.Text);
        Assert.Equal(["detalhe técnico"], message.Lines!.ToArray());
    }

    [Fact]
    public async Task Runner_entrega_a_narracao_na_ordem_e_a_conclusao_por_ultimo()
    {
        var sys = new FakePrintSystemProvider
        {
            Spooler = SourceResult<ServiceInfo>.Ok([new ServiceInfo("Spooler", ServiceState.Stopped, "Auto")]),
            PrinterStates = SourceResult<PrinterStateInfo>.Ok([new PrinterStateInfo("Impressora X", "USB001", false, 3, 2, true)])
        };
        var registry = new DiagnosticRegistry();
        registry.Register(NotPrintingScenario.Create(sys, new FakePrintDeviceProvider()));
        var messages = new List<ChatMessage>();

        await new ChatRunner(new DiagnosticEngine(registry))
            .RunAsync(NotPrintingScenario.DiagnosticId, messages.Add, CancellationToken.None);

        Assert.True(messages.Count > 3);
        Assert.All(messages.Take(messages.Count - 1), m => Assert.Equal(ChatKind.Narration, m.Kind));
        Assert.Equal(ChatKind.Conclusion, messages[^1].Kind);
        Assert.StartsWith("Iniciando:", messages[0].Text);
        Assert.Equal("Encontrei o problema", messages[^1].Text);
        // A narração mostra o trabalho acontecendo antes de concluir.
        var spoolerIndex = messages.FindIndex(m => m.Text.Contains("Spooler"));
        var crossIndex = messages.FindIndex(m => m.Text.Contains("cruzar"));
        Assert.InRange(spoolerIndex, 1, crossIndex - 1);
    }

    [Fact]
    public async Task Runner_com_id_inexistente_termina_com_cartao_de_erro_sem_lancar()
    {
        var messages = new List<ChatMessage>();

        await new ChatRunner(new DiagnosticEngine(new DiagnosticRegistry()))
            .RunAsync("nao.existe", messages.Add, CancellationToken.None);

        Assert.Equal(ChatTone.Error, messages[^1].Tone);
    }
}
