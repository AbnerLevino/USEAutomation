using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Platform.Windows.Elevation;

namespace AutomationPlatform.Tests;

public class ElevatedActionRunnerTests
{
    private sealed class FakeServices : IServiceControl
    {
        public ServiceState? State { get; set; } = ServiceState.Stopped;
        public ControlResult RestartResult { get; set; } = new(true);
        public List<string> Queried { get; } = [];
        public List<string> Restarted { get; } = [];

        public Task<ServiceState?> GetStateAsync(string serviceName, CancellationToken cancellationToken)
        {
            Queried.Add(serviceName);
            return Task.FromResult(State);
        }

        public Task<ControlResult> RestartAsync(string serviceName, CancellationToken cancellationToken)
        {
            Restarted.Add(serviceName);
            return Task.FromResult(RestartResult);
        }
    }

    private sealed class FakeProbe(bool elevated) : IElevationProbe
    {
        public bool IsElevated { get; } = elevated;
    }

    private static (ElevatedActionRunner Runner, FakeServices Services) Setup(bool elevated = true)
    {
        var services = new FakeServices();
        return (new ElevatedActionRunner(services, new FakeProbe(elevated)), services);
    }

    private static readonly string[] Restart = ["--action", "printing.spooler.restart"];

    [Fact]
    public async Task Pedido_valido_reinicia_so_o_servico_de_impressao_e_devolve_sucesso()
    {
        var (runner, services) = Setup();

        var code = await runner.RunAsync(Restart, default);

        Assert.Equal(ElevatedProtocol.Done, code);
        Assert.Equal(["Spooler"], services.Restarted);
    }

    [Fact]
    public async Task O_nome_do_servico_e_fixo_no_codigo_e_nunca_vem_do_pedido()
    {
        var (runner, services) = Setup();

        await runner.RunAsync(Restart, default);

        Assert.Equal(ElevatedActionRunner.SpoolerServiceName, Assert.Single(services.Queried));
        Assert.Equal("Spooler", ElevatedActionRunner.SpoolerServiceName);
    }

    [Theory]
    [InlineData("--action", "printing.queue.clear")]          // nome bem formado, mas fora da lista
    [InlineData("--action", "system.format")]
    [InlineData("--action", "printing.spooler.restart & calc")]
    public async Task Acao_fora_da_lista_e_recusada_sem_tocar_em_nada(params string[] args)
    {
        var (runner, services) = Setup();

        var code = await runner.RunAsync(args, default);

        Assert.Equal(ElevatedProtocol.Refused, code);
        Assert.Empty(services.Queried);
        Assert.Empty(services.Restarted);
    }

    [Fact]
    public async Task Pedido_mal_formado_e_recusado_sem_tocar_em_nada()
    {
        var (runner, services) = Setup();

        Assert.Equal(ElevatedProtocol.Refused, await runner.RunAsync([], default));
        Assert.Equal(ElevatedProtocol.Refused, await runner.RunAsync(["--action"], default));
        Assert.Equal(ElevatedProtocol.Refused, await runner.RunAsync(["--shell", "cmd"], default));
        Assert.Empty(services.Restarted);
    }

    [Fact]
    public async Task Parametro_que_a_acao_nao_aceita_e_recusado()
    {
        var (runner, services) = Setup();

        var code = await runner.RunAsync(["--action", "printing.spooler.restart", "--printer", "Qualquer"], default);

        Assert.Equal(ElevatedProtocol.Refused, code);
        Assert.Empty(services.Restarted);
    }

    [Fact]
    public async Task Sem_elevacao_de_verdade_nada_e_feito()
    {
        var (runner, services) = Setup(elevated: false);

        var code = await runner.RunAsync(Restart, default);

        Assert.Equal(ElevatedProtocol.NotElevated, code);
        Assert.Empty(services.Restarted);
        Assert.Empty(services.Queried);
    }

    [Fact]
    public async Task A_validacao_do_pedido_vem_antes_da_checagem_de_elevacao()
    {
        var (runner, _) = Setup(elevated: false);

        Assert.Equal(ElevatedProtocol.Refused, await runner.RunAsync(["--action", "system.format"], default));
    }

    [Fact]
    public async Task Servico_que_nao_existe_revalida_a_condicao_e_nao_age()
    {
        var (runner, services) = Setup();
        services.State = null;

        var code = await runner.RunAsync(Restart, default);

        Assert.Equal(ElevatedProtocol.PreconditionNotMet, code);
        Assert.Empty(services.Restarted);
    }

    [Fact]
    public async Task Falha_ao_reiniciar_vira_codigo_de_falha_e_nunca_sucesso()
    {
        var (runner, services) = Setup();
        services.RestartResult = new ControlResult(false, "o serviço não parou a tempo.");

        Assert.Equal(ElevatedProtocol.Failed, await runner.RunAsync(Restart, default));
    }

    [Fact]
    public async Task Parametro_que_a_acao_nao_aceita_e_recusado_mesmo_sem_elevacao()
    {
        // Todo pedido inválido tem o mesmo código, elevado ou não (achado na execução real do executor).
        var (runner, services) = Setup(elevated: false);

        var code = await runner.RunAsync(["--action", "printing.spooler.restart", "--printer", "X"], default);

        Assert.Equal(ElevatedProtocol.Refused, code);
        Assert.Empty(services.Queried);
    }

    [Fact]
    public void Hoje_nenhuma_acao_recebe_o_nome_de_impressora()
    {
        Assert.Empty(ElevatedProtocol.ActionsWithPrinter);
    }
}
