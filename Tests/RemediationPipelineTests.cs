using AutomationPlatform.Contracts;
using AutomationPlatform.Engine;

namespace AutomationPlatform.Tests;

public class RemediationPipelineTests
{
    private sealed class FakeAction(ActionLevel level = ActionLevel.N1) : IRemediationAction
    {
        public List<string> Calls { get; } = [];
        public PreconditionResult Precondition { get; set; } = PreconditionResult.Ok;
        public ExecutionResult Execution { get; set; } = new(ExecutionOutcome.Done);
        public VerificationResult Verification { get; set; } = new(VerificationOutcome.Resolved, "a fila voltou a funcionar");
        public Exception? ThrowOn { get; set; }
        public string? ThrowStep { get; set; }

        public string Id => "teste.acao";
        public string DisplayName => "Ação de teste";
        public ActionLevel Level => level;
        public string DescribeForConfirmation(ActionTarget target) => $"Vou mexer em {target.PrinterName}.";

        public Task<PreconditionResult> CheckPreconditionAsync(ActionTarget target, CancellationToken cancellationToken)
        {
            Calls.Add("pre");
            MaybeThrow("pre");
            return Task.FromResult(Precondition);
        }

        public Task<ExecutionResult> ExecuteAsync(ActionTarget target, CancellationToken cancellationToken)
        {
            Calls.Add("exec");
            MaybeThrow("exec");
            return Task.FromResult(Execution);
        }

        public Task<VerificationResult> VerifyAsync(ActionTarget target, CancellationToken cancellationToken)
        {
            Calls.Add("verify");
            MaybeThrow("verify");
            return Task.FromResult(Verification);
        }

        private void MaybeThrow(string step)
        {
            if (ThrowStep == step && ThrowOn is not null) throw ThrowOn;
        }
    }

    private sealed class FakeConfirmation(bool answer) : IConfirmation
    {
        public List<ConfirmationPrompt> Asked { get; } = [];

        public Task<bool> ConfirmAsync(ConfirmationPrompt prompt, CancellationToken cancellationToken)
        {
            Asked.Add(prompt);
            return Task.FromResult(answer);
        }
    }

    private static readonly ActionTarget Printer = new("Impressora X");

    private static async Task<(ActionRecord Record, InMemoryAuditLog Audit)> RunAsync(FakeAction action, FakeConfirmation confirmation)
    {
        var audit = new InMemoryAuditLog();
        var record = await new RemediationPipeline(audit).RunAsync(action, Printer, confirmation);
        return (record, audit);
    }

    [Fact]
    public async Task Caminho_feliz_passa_por_todas_as_etapas_na_ordem_e_so_entao_diz_resolvido()
    {
        var action = new FakeAction();
        var (record, audit) = await RunAsync(action, new FakeConfirmation(true));

        Assert.Equal(["pre", "exec", "verify"], action.Calls);
        Assert.Equal(ActionStatus.Resolved, record.Status);
        Assert.True(record.Confirmed);
        Assert.StartsWith("Resolvido", record.Summary);
        Assert.Equal("Impressora X", record.PrinterName);
        Assert.Same(record, Assert.Single(audit.Records));
    }

    [Fact]
    public async Task Sem_confirmacao_nada_e_executado_mas_a_tentativa_fica_na_auditoria()
    {
        var action = new FakeAction();
        var (record, audit) = await RunAsync(action, new FakeConfirmation(false));

        Assert.Equal(["pre"], action.Calls);
        Assert.Equal(ActionStatus.NotConfirmed, record.Status);
        Assert.False(record.Confirmed);
        Assert.Contains("Nada foi alterado", record.Summary);
        Assert.Single(audit.Records);
    }

    [Fact]
    public async Task Pre_condicao_nao_atendida_nem_pergunta_ao_usuario()
    {
        var action = new FakeAction { Precondition = PreconditionResult.Not("a fila já está ativa") };
        var confirmation = new FakeConfirmation(true);
        var (record, _) = await RunAsync(action, confirmation);

        Assert.Equal(["pre"], action.Calls);
        Assert.Empty(confirmation.Asked);
        Assert.Equal(ActionStatus.PreconditionFailed, record.Status);
        Assert.Contains("a fila já está ativa", record.Summary);
    }

    [Fact]
    public async Task Verificacao_que_mostra_problema_vira_nao_resolvido()
    {
        var action = new FakeAction { Verification = new(VerificationOutcome.NotResolved, "a fila continua pausada") };
        var (record, _) = await RunAsync(action, new FakeConfirmation(true));

        Assert.Equal(ActionStatus.NotResolved, record.Status);
        Assert.StartsWith("Não resolvido", record.Summary);
        Assert.Equal("a fila continua pausada", record.VerificationDetail);
    }

    [Fact]
    public async Task Verificacao_inconclusiva_nunca_vira_resolvido()
    {
        var action = new FakeAction { Verification = new(VerificationOutcome.Inconclusive, "o Windows não respondeu") };
        var (record, _) = await RunAsync(action, new FakeConfirmation(true));

        Assert.Equal(ActionStatus.Inconclusive, record.Status);
        Assert.DoesNotContain("Resolvido", record.Summary);
    }

    [Fact]
    public async Task Elevacao_recusada_diz_que_nada_foi_alterado_e_nao_verifica()
    {
        var action = new FakeAction(ActionLevel.N2) { Execution = new(ExecutionOutcome.ElevationDenied) };
        var (record, _) = await RunAsync(action, new FakeConfirmation(true));

        Assert.Equal(["pre", "exec"], action.Calls);
        Assert.Equal(ActionStatus.ElevationDenied, record.Status);
        Assert.Contains("aprovação de um administrador", record.Summary);
        Assert.Contains("Nada foi alterado", record.Summary);
    }

    [Theory]
    [InlineData(ExecutionOutcome.Failed)]
    [InlineData(ExecutionOutcome.Refused)]
    public async Task Falha_ou_recusa_do_executor_nao_chega_a_verificacao(ExecutionOutcome outcome)
    {
        var action = new FakeAction { Execution = new(outcome, "serviço não iniciou") };
        var (record, _) = await RunAsync(action, new FakeConfirmation(true));

        Assert.Equal(["pre", "exec"], action.Calls);
        Assert.Equal(ActionStatus.ExecutionFailed, record.Status);
        Assert.Contains("serviço não iniciou", record.Summary);
    }

    [Theory]
    [InlineData("pre", ActionStatus.PreconditionFailed)]
    [InlineData("exec", ActionStatus.ExecutionFailed)]
    [InlineData("verify", ActionStatus.Inconclusive)]
    public async Task Excecao_em_qualquer_etapa_vira_resultado_e_e_registrada(string step, ActionStatus expected)
    {
        var action = new FakeAction { ThrowStep = step, ThrowOn = new InvalidOperationException("boom") };
        var (record, audit) = await RunAsync(action, new FakeConfirmation(true));

        Assert.Equal(expected, record.Status);
        Assert.Single(audit.Records);
    }

    [Fact]
    public async Task Cancelamento_nao_e_engolido()
    {
        var action = new FakeAction { ThrowStep = "exec", ThrowOn = new OperationCanceledException() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(action, new FakeConfirmation(true)));
    }

    [Theory]
    [InlineData(ActionLevel.N1, false, false)]
    [InlineData(ActionLevel.N2, false, true)]
    [InlineData(ActionLevel.N3, true, true)]
    public async Task A_confirmacao_avisa_perda_de_dados_e_administrador_conforme_o_nivel(ActionLevel level, bool dataLoss, bool admin)
    {
        var confirmation = new FakeConfirmation(true);
        await RunAsync(new FakeAction(level), confirmation);

        var prompt = Assert.Single(confirmation.Asked);
        Assert.Equal(dataLoss, prompt.WarnsDataLoss);
        Assert.Equal(admin, prompt.MayNeedAdministrator);
        Assert.Equal("Vou mexer em Impressora X.", prompt.Text);
    }
}
