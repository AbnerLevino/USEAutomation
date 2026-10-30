using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Platform.Windows;

/// <summary>
/// Dados FALSOS de impressão, só para ver a janela reagir a cada problema sem mexer no Windows.
/// Nada aqui lê ou altera o computador: até as "correções" só mudam estes dados falsos. Quem usa tem de deixar claro na tela que é demonstração.
/// </summary>
public sealed class DemoPrintingProvider : IPrintSystemProvider, IPrintDeviceProvider, IPrinterControl, IElevatedExecutor
{
    private const string Usb = "HP LaserJet (demo)";
    private const string Pdf = "Microsoft Print to PDF";
    private const string Net = "Epson de Rede (demo)";

    /// <summary>Nomes aceitos por <see cref="Create"/>, com uma descrição curta de cada um.</summary>
    public static IReadOnlyDictionary<string, string> Scenarios { get; } = new Dictionary<string, string>
    {
        ["tudo-ok"] = "Tudo funcionando",
        ["spooler-parado"] = "Serviço de impressão (Spooler) parado",
        ["pausada"] = "Fila da impressora pausada",
        ["offline"] = "Impressora offline",
        ["sem-porta"] = "Fila física sem porta",
        ["erro-dispositivo"] = "Windows reporta erro no dispositivo (código 28)",
        ["papel-atolado"] = "Impressora reporta papel atolado",
        ["pouco-toner"] = "Só um aviso de pouco toner",
        ["fila-presa"] = "Trabalho preso na fila",
        ["padrao-virtual"] = "Padrão é o PDF, com impressora física instalada",
        ["sem-impressora"] = "Nenhuma impressora física",
        ["sem-acesso"] = "Windows nega a leitura (resultado inconclusivo)",
        ["varias"] = "Várias causas ao mesmo tempo"
    };

    private IReadOnlyList<PrintDeviceInfo> _devices = [];
    private IReadOnlyList<InstalledPrinterInfo> _installed = [];
    private IReadOnlyList<PrinterStateInfo> _states = [];
    private IReadOnlyList<PrintJobInfo> _jobs = [];
    private IReadOnlyList<NetworkPortInfo> _networkPorts = [];
    private ServiceState _spooler = ServiceState.Running;
    private SourceStatus _failure = SourceStatus.Consulted;

    private DemoPrintingProvider() { }

    /// <summary>Monta o cenário pelo nome; null se o nome não existir.</summary>
    public static DemoPrintingProvider? Create(string name)
    {
        var d = new DemoPrintingProvider();
        d.Healthy();
        switch (name.Trim().ToLowerInvariant())
        {
            case "tudo-ok": break;
            case "spooler-parado": d._spooler = ServiceState.Stopped; break;
            case "pausada": d.ChangeUsb(extended: 8); break;
            case "offline": d.ChangeUsb(extended: 7, workOffline: true); break;
            case "sem-porta": d.ChangeUsb(port: null); break;
            case "erro-dispositivo": d._devices = [new PrintDeviceInfo(Usb, @"USB\VID_03F0&PID_0001\DEMO", 28)]; break;
            case "papel-atolado": d.ChangeUsb(error: 8); break;
            case "pouco-toner": d.ChangeUsb(error: 5); break;
            case "fila-presa": d._jobs = [new PrintJobInfo(Usb, 12, PrintJobStatus.Error)]; break;
            case "padrao-virtual": d.SetDefault(Pdf); break;
            case "sem-impressora":
                d._devices = [];
                d._installed = [new InstalledPrinterInfo(Pdf, "PORTPROMPT:")];
                d._states = [new PrinterStateInfo(Pdf, "PORTPROMPT:", false, 3, 2, true)];
                d._networkPorts = [];
                break;
            case "sem-acesso": d._failure = SourceStatus.PermissionDenied; break;
            case "varias":
                d._spooler = ServiceState.Stopped;
                d.ChangeUsb(extended: 8, error: 8);
                d._jobs = [new PrintJobInfo(Usb, 7, PrintJobStatus.Error), new PrintJobInfo(Usb, 8, PrintJobStatus.Error)];
                break;
            default: return null;
        }
        return d;
    }

    private void Healthy()
    {
        _devices = [new PrintDeviceInfo(Usb, @"USB\VID_03F0&PID_0001\DEMO", 0)];
        _installed = [new InstalledPrinterInfo(Usb, "USB001"), new InstalledPrinterInfo(Pdf, "PORTPROMPT:"), new InstalledPrinterInfo(Net, "IP_192.168.0.50")];
        _states =
        [
            new PrinterStateInfo(Usb, "USB001", false, 3, 2, true),
            new PrinterStateInfo(Pdf, "PORTPROMPT:", false, 3, 2, false),
            new PrinterStateInfo(Net, "IP_192.168.0.50", false, 3, 2, false)
        ];
        _networkPorts = [new NetworkPortInfo("IP_192.168.0.50", "192.168.0.50", 9100)];
    }

    private void ChangeUsb(int extended = 3, bool workOffline = false, int error = 2, string? port = "USB001")
    {
        var isDefault = _states.First(s => s.Name == Usb).IsDefault;
        _states = _states.Select(s => s.Name == Usb ? new PrinterStateInfo(Usb, port, workOffline, extended, error, isDefault) : s).ToList();
        _installed = _installed.Select(p => p.Name == Usb ? new InstalledPrinterInfo(Usb, port) : p).ToList();
    }

    private void SetDefault(string name) =>
        _states = _states.Select(s => s with { IsDefault = s.Name == name }).ToList();

    /// <summary>"Retoma" a fila nos dados falsos (muda o estado de pausada para ociosa). Não toca no Windows.</summary>
    public Task<ControlResult> ResumeAsync(string printerName, CancellationToken cancellationToken)
    {
        var found = false;
        _states = _states.Select(s =>
        {
            if (!string.Equals(s.Name, printerName, StringComparison.OrdinalIgnoreCase)) return s;
            found = true;
            return s.ExtendedStatus == 8 ? s with { ExtendedStatus = 3 } : s;
        }).ToList();
        return Task.FromResult(found ? new ControlResult(true) : new ControlResult(false, $"não encontrei a impressora '{printerName}'."));
    }

    /// <summary>
    /// Executor elevado "simulado": nenhum aviso do Windows aparece e nada é alterado de verdade; o serviço de impressão
    /// dos dados falsos volta a funcionar.
    /// </summary>
    public Task<ExecutionResult> RunAsync(string actionId, ActionTarget target, CancellationToken cancellationToken)
    {
        if (actionId != PrinterActionIds.RestartSpooler)
            return Task.FromResult(new ExecutionResult(ExecutionOutcome.Refused, "essa ação não está na lista de ações permitidas."));
        _spooler = ServiceState.Running;
        return Task.FromResult(new ExecutionResult(ExecutionOutcome.Done));
    }

    /// <summary>Página de teste "simulada": nada é impresso nem enviado a lugar nenhum.</summary>
    public Task<ControlResult> PrintTestPageAsync(string printerName, CancellationToken cancellationToken) =>
        Task.FromResult(_states.Any(s => string.Equals(s.Name, printerName, StringComparison.OrdinalIgnoreCase))
            ? new ControlResult(true)
            : new ControlResult(false, $"não encontrei a impressora '{printerName}'."));

    // Cada consulta devolve o cenário; em "sem-acesso" todas falham do mesmo jeito, como numa negação real.
    private SourceResult<T> Answer<T>(IReadOnlyList<T> items) =>
        _failure == SourceStatus.Consulted
            ? SourceResult<T>.Ok(items)
            : SourceResult<T>.Fail(_failure, "Dados de demonstração: acesso negado simulado.");

    public Task<SourceResult<ServiceInfo>> GetSpoolerServiceAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Answer<ServiceInfo>([new ServiceInfo("Spooler", _spooler, "Auto")]));

    public Task<SourceResult<PrintJobInfo>> GetPrintJobsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Answer(_jobs));

    public Task<SourceResult<PrinterStateInfo>> GetPrinterStatesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Answer(_states));

    public Task<SourceResult<DefaultPrinterModeInfo>> GetDefaultPrinterModeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Answer<DefaultPrinterModeInfo>([new DefaultPrinterModeInfo(false)]));

    public Task<SourceResult<NetworkPortInfo>> GetNetworkPortsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Answer(_networkPorts));

    public Task<SourceResult<PrintDeviceInfo>> GetPrintDevicesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Answer(_devices));

    public Task<SourceResult<InstalledPrinterInfo>> GetInstalledPrintersAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Answer(_installed));
}
