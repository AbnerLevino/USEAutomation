using AutomationPlatform.Contracts;
using AutomationPlatform.Engine;

namespace AutomationPlatform.Tests;

public class UserInteractionTests
{
    private sealed class SimpleAction : IRemediationAction
    {
        public bool Executed { get; private set; }
        public string Id => "teste.simples";
        public string DisplayName => "Ação simples";
        public ActionLevel Level => ActionLevel.N1;
        public string DescribeForConfirmation(ActionTarget target) => "Vou fazer algo simples.";
        public Task<PreconditionResult> CheckPreconditionAsync(ActionTarget target, CancellationToken ct) => Task.FromResult(PreconditionResult.Ok);
        public Task<ExecutionResult> ExecuteAsync(ActionTarget target, CancellationToken ct)
        {
            Executed = true;
            return Task.FromResult(new ExecutionResult(ExecutionOutcome.Done));
        }
        public Task<VerificationResult> VerifyAsync(ActionTarget target, CancellationToken ct) =>
            Task.FromResult(new VerificationResult(VerificationOutcome.Resolved, "ok"));
    }

    [Fact]
    public async Task Pipeline_aceita_uma_interacao_como_confirmacao_e_respeita_o_sim()
    {
        var interaction = new FakeUserInteraction().WillConfirm(true);
        var action = new SimpleAction();

        var record = await new RemediationPipeline(new InMemoryAuditLog())
            .RunAsync(action, new ActionTarget("Impressora X"), interaction);

        Assert.True(action.Executed);
        Assert.Equal(ActionStatus.Resolved, record.Status);
        Assert.Single(interaction.Confirmed);
    }

    [Fact]
    public async Task Pipeline_com_interacao_respeita_o_nao_e_nao_altera_nada()
    {
        var interaction = new FakeUserInteraction().WillConfirm(false);
        var action = new SimpleAction();

        var record = await new RemediationPipeline(new InMemoryAuditLog())
            .RunAsync(action, new ActionTarget("Impressora X"), interaction);

        Assert.False(action.Executed);
        Assert.Equal(ActionStatus.NotConfirmed, record.Status);
    }

    [Fact]
    public async Task Escolha_devolve_a_opcao_e_cancelar_devolve_null()
    {
        var options = new[] { new Choice("a", "Impressora A"), new Choice("b", "Impressora B") };
        var interaction = new FakeUserInteraction().WillChoose("b", null);

        var chosen = await interaction.ChooseAsync("Qual impressora?", options, CancellationToken.None);
        var cancelled = await interaction.ChooseAsync("Qual impressora?", options, CancellationToken.None);

        Assert.Equal("b", chosen?.Id);
        Assert.Null(cancelled);
    }

    [Fact]
    public async Task Pergunta_nao_prevista_no_roteiro_falha_o_teste()
    {
        var interaction = new FakeUserInteraction();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => interaction.AskIfResolvedAsync("Voltou a imprimir?", CancellationToken.None));
    }
}
