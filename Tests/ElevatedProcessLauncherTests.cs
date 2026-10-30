using System.ComponentModel;
using System.Security.AccessControl;
using AutomationPlatform.Contracts;
using AutomationPlatform.Platform.Windows.Elevation;

namespace AutomationPlatform.Tests;

public sealed class ElevatedProcessLauncherTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "automaxion-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _exe;

    public ElevatedProcessLauncherTests()
    {
        Directory.CreateDirectory(_folder);
        _exe = Path.Combine(_folder, "AutomationPlatform.Host.Elevated.exe");
        File.WriteAllText(_exe, "não é um executável de verdade; os testes não o iniciam");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private sealed class FakeRunner : IElevatedProcessRunner
    {
        public Func<int> Behavior { get; set; } = () => ElevatedProtocol.Done;
        public List<(string Exe, string Arguments)> Launched { get; } = [];

        public Task<int> RunAsync(string executablePath, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Launched.Add((executablePath, arguments));
            return Task.FromResult(Behavior());
        }
    }

    private sealed class FakeAcl(bool safe) : IAclReader
    {
        public IReadOnlyList<AclRule> Read(string path, bool isDirectory) =>
            safe ? [new AclRule("S-1-5-18", FileSystemRights.FullControl, true)]
                 : [new AclRule("S-1-5-32-545", FileSystemRights.Modify, true)];
    }

    private ElevatedProcessLauncher Launcher(FakeRunner runner, bool safeFolder = true, bool allowUnprotected = false) =>
        new(_exe, runner, new FakeAcl(safeFolder), new ElevatedLaunchOptions(allowUnprotected));

    private static readonly ActionTarget NoTarget = new();

    [Fact]
    public async Task Pedido_valido_lanca_o_executor_com_a_linha_de_comando_exata_e_devolve_o_resultado()
    {
        var runner = new FakeRunner();

        var result = await Launcher(runner).RunAsync("printing.spooler.restart", NoTarget, default);

        Assert.Equal(ExecutionOutcome.Done, result.Outcome);
        var launched = Assert.Single(runner.Launched);
        Assert.Equal(_exe, launched.Exe);
        Assert.Equal("--action printing.spooler.restart", launched.Arguments);
    }

    [Fact]
    public async Task Acao_fora_da_lista_e_recusada_antes_de_qualquer_aviso_do_windows()
    {
        var runner = new FakeRunner();

        var result = await Launcher(runner).RunAsync("system.format", NoTarget, default);

        Assert.Equal(ExecutionOutcome.Refused, result.Outcome);
        Assert.Empty(runner.Launched);
    }

    [Fact]
    public async Task Valor_que_nao_cabe_na_linha_de_comando_e_recusado_sem_lancar()
    {
        var runner = new FakeRunner();

        var result = await Launcher(runner).RunAsync("printing.spooler.restart", new ActionTarget("Fila \"x\""), default);

        Assert.Equal(ExecutionOutcome.Refused, result.Outcome);
        Assert.Empty(runner.Launched);
    }

    [Fact]
    public async Task Executor_que_nao_existe_e_falha_sem_lancar()
    {
        var runner = new FakeRunner();
        File.Delete(_exe);

        var result = await Launcher(runner).RunAsync("printing.spooler.restart", NoTarget, default);

        Assert.Equal(ExecutionOutcome.Failed, result.Outcome);
        Assert.Empty(runner.Launched);
    }

    [Fact]
    public async Task Executor_em_pasta_que_usuario_comum_altera_nao_recebe_pedido_de_administrador()
    {
        var runner = new FakeRunner();

        var result = await Launcher(runner, safeFolder: false).RunAsync("printing.spooler.restart", NoTarget, default);

        Assert.Equal(ExecutionOutcome.Refused, result.Outcome);
        Assert.Contains("usuário comum", result.Detail);
        Assert.Empty(runner.Launched);   // nem o aviso do Windows chega a aparecer
    }

    [Fact]
    public async Task Modo_de_desenvolvimento_libera_a_pasta_comum_mas_o_resultado_leva_um_aviso()
    {
        var runner = new FakeRunner();

        var result = await Launcher(runner, safeFolder: false, allowUnprotected: true)
            .RunAsync("printing.spooler.restart", NoTarget, default);

        Assert.Equal(ExecutionOutcome.Done, result.Outcome);
        Assert.Contains("AVISO", result.Detail);
        Assert.Single(runner.Launched);
    }

    [Fact]
    public async Task Pasta_protegida_nao_leva_aviso_nenhum()
    {
        var result = await Launcher(new FakeRunner(), allowUnprotected: true).RunAsync("printing.spooler.restart", NoTarget, default);

        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task Aviso_do_windows_recusado_vira_elevation_denied_e_nada_e_alterado()
    {
        var runner = new FakeRunner { Behavior = () => throw new ElevationCancelledException() };

        var result = await Launcher(runner).RunAsync("printing.spooler.restart", NoTarget, default);

        Assert.Equal(ExecutionOutcome.ElevationDenied, result.Outcome);
    }

    [Theory]
    [InlineData(ElevatedProtocol.Refused, ExecutionOutcome.Refused)]
    [InlineData(ElevatedProtocol.Failed, ExecutionOutcome.Failed)]
    [InlineData(ElevatedProtocol.PreconditionNotMet, ExecutionOutcome.Failed)]
    [InlineData(ElevatedProtocol.NotElevated, ExecutionOutcome.ElevationDenied)]
    [InlineData(99, ExecutionOutcome.Failed)]
    public async Task Codigo_de_saida_do_executor_e_traduzido(int exitCode, ExecutionOutcome expected)
    {
        var runner = new FakeRunner { Behavior = () => exitCode };

        var result = await Launcher(runner).RunAsync("printing.spooler.restart", NoTarget, default);

        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task Executor_que_demora_demais_e_abandonado_como_falha_e_manda_conferir()
    {
        var runner = new FakeRunner { Behavior = () => throw new TimeoutException() };

        var result = await Launcher(runner).RunAsync("printing.spooler.restart", NoTarget, default);

        Assert.Equal(ExecutionOutcome.Failed, result.Outcome);
        Assert.Contains("confira", result.Detail);
    }

    [Fact]
    public async Task Windows_que_nao_consegue_iniciar_o_executor_vira_falha_com_o_motivo()
    {
        var runner = new FakeRunner { Behavior = () => throw new Win32Exception(5, "Acesso negado") };

        var result = await Launcher(runner).RunAsync("printing.spooler.restart", NoTarget, default);

        Assert.Equal(ExecutionOutcome.Failed, result.Outcome);
        Assert.Contains("Acesso negado", result.Detail);
    }

    [Fact]
    public void O_executor_e_procurado_ao_lado_do_programa()
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "AutomationPlatform.Host.Elevated.exe"), ElevatedProcessLauncher.DefaultExecutablePath());
    }
}
