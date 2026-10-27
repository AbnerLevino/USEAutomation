using System.Text.Json;
using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;
using AutomationPlatform.Engine;
using AutomationPlatform.Host.Cli;

namespace AutomationPlatform.Tests;

public class CliAppTests
{
    private static DiagnosticRegistry RegistryWith(FakePrintDeviceProvider provider)
    {
        var registry = new DiagnosticRegistry();
        registry.Register(new PrinterRecognitionDiagnostic(provider));
        return registry;
    }

    private static async Task<(int Code, string Out, string Err)> RunAsync(FakePrintDeviceProvider provider, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await CliApp.RunAsync(args, RegistryWith(provider), stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public async Task Json_mode_writes_only_valid_json_to_stdout_and_nothing_to_stderr()
    {
        var provider = new FakePrintDeviceProvider
        {
            Devices = SourceResult<PrintDeviceInfo>.Ok([new PrintDeviceInfo("X", "A", 0)]),
            Printers = SourceResult<InstalledPrinterInfo>.Ok([new InstalledPrinterInfo("X")])
        };

        var (code, output, err) = await RunAsync(provider, "run", "printing.device-recognition", "--json");

        Assert.Equal(CliApp.ExitNoProblem, code);
        Assert.Equal("", err);
        using var doc = JsonDocument.Parse(output); // lança se houver lixo fora do JSON
        Assert.Equal("NoProblemDetected", doc.RootElement.GetProperty("overallStatus").GetString());
    }

    [Fact]
    public async Task Human_mode_narrates_progress_on_stderr_and_report_on_stdout()
    {
        var (_, output, err) = await RunAsync(new FakePrintDeviceProvider(), "run", "printing.device-recognition");

        Assert.Contains("Vou começar perguntando ao Windows", err);
        Assert.Contains("Resumo:", output);
        Assert.DoesNotContain("Vou começar", output);
    }

    [Fact]
    public async Task Problem_found_returns_exit_code_1()
    {
        var provider = new FakePrintDeviceProvider
        {
            Devices = SourceResult<PrintDeviceInfo>.Ok([new PrintDeviceInfo("X", "A", 28)])
        };

        var (code, _, _) = await RunAsync(provider, "run", "printing.device-recognition", "--json");

        Assert.Equal(CliApp.ExitProblemFound, code);
    }

    [Fact]
    public async Task Inconclusive_returns_exit_code_2()
    {
        var (code, _, _) = await RunAsync(new FakePrintDeviceProvider(), "run", "printing.device-recognition", "--json");

        Assert.Equal(CliApp.ExitInconclusive, code);
    }

    [Fact]
    public async Task Unknown_diagnostic_returns_exit_code_3()
    {
        var (code, output, _) = await RunAsync(new FakePrintDeviceProvider(), "run", "nao.existe", "--json");

        Assert.Equal(CliApp.ExitExecutionFailed, code);
        using var doc = JsonDocument.Parse(output);
        Assert.Equal("Failed", doc.RootElement.GetProperty("executionStatus").GetString());
    }

    [Fact]
    public async Task Invalid_command_returns_usage_error_and_prints_help_on_stderr()
    {
        var (code, output, err) = await RunAsync(new FakePrintDeviceProvider(), "foo");

        Assert.Equal(CliApp.ExitUsageError, code);
        Assert.Equal("", output);
        Assert.Contains("Uso:", err);
    }

    [Fact]
    public async Task List_prints_registered_diagnostics()
    {
        var (code, output, _) = await RunAsync(new FakePrintDeviceProvider(), "list");

        Assert.Equal(0, code);
        Assert.Contains("printing.device-recognition", output);
    }
}
