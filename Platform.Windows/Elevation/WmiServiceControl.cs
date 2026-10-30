using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Platform.Windows.Elevation;

/// <summary>
/// Para e inicia serviços pelo WMI (Win32_Service). Só funciona de verdade dentro do executor elevado.
/// O nome do serviço é escapado antes de entrar na consulta e nunca vem de fora do código do executor.
/// Não força nada: se outros serviços dependem deste e impedem a parada, devolve falha (não derruba os outros).
/// </summary>
public sealed class WmiServiceControl(TimeSpan? stepTimeout = null) : IServiceControl
{
    private static readonly TimeSpan DefaultStepTimeout = TimeSpan.FromSeconds(25);
    private readonly TimeSpan _stepTimeout = stepTimeout ?? DefaultStepTimeout;

    public Task<ServiceState?> GetStateAsync(string serviceName, CancellationToken cancellationToken) =>
        Task.Run(() => ReadState(serviceName), cancellationToken);

    public Task<ControlResult> RestartAsync(string serviceName, CancellationToken cancellationToken) =>
        Task.Run(() => Restart(serviceName, cancellationToken), cancellationToken);

    private ControlResult Restart(string name, CancellationToken ct)
    {
        try
        {
            var state = ReadState(name);
            if (state is null) return new ControlResult(false, "o Windows não encontrou o serviço.");

            if (state != ServiceState.Stopped)
            {
                var stop = Invoke(name, "StopService");
                if (stop != 0 && stop != 5) return new ControlResult(false, Describe("parar", stop), AccessDenied: stop == 2);
                if (!WaitFor(name, ServiceState.Stopped, ct)) return new ControlResult(false, "o serviço não parou a tempo.");
            }

            var start = Invoke(name, "StartService");
            if (start != 0 && start != 10) return new ControlResult(false, Describe("iniciar", start), AccessDenied: start == 2);
            return WaitFor(name, ServiceState.Running, ct)
                ? new ControlResult(true)
                : new ControlResult(false, "o serviço não voltou a funcionar a tempo.");
        }
        catch (UnauthorizedAccessException ex) { return new ControlResult(false, ex.Message, AccessDenied: true); }
        catch (ManagementException ex) { return new ControlResult(false, $"{ex.ErrorCode}: {ex.Message}", AccessDenied: ex.ErrorCode == ManagementStatus.AccessDenied); }
        catch (COMException ex) { return new ControlResult(false, ex.Message); }
    }

    private static ServiceState? ReadState(string name)
    {
        using var searcher = new ManagementObjectSearcher($"SELECT State FROM Win32_Service WHERE Name = '{Escape(name)}'");
        using var results = searcher.Get();
        foreach (ManagementBaseObject service in results)
            using (service)
                return ServiceStateParser.Parse(service["State"] as string);
        return null;
    }

    private static uint Invoke(string name, string method)
    {
        using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Service WHERE Name = '{Escape(name)}'");
        using var results = searcher.Get();
        foreach (ManagementObject service in results)
            using (service)
                return Convert.ToUInt32(service.InvokeMethod(method, null), System.Globalization.CultureInfo.InvariantCulture);
        return 1;   // não encontrado
    }

    private bool WaitFor(string name, ServiceState wanted, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + _stepTimeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (ReadState(name) == wanted) return true;
            Thread.Sleep(250);
        }
        return ReadState(name) == wanted;
    }

    private static string Describe(string what, uint code) => code switch
    {
        2 => $"o Windows negou permissão para {what} o serviço.",
        3 => $"não foi possível {what} o serviço porque outros serviços dependem dele.",
        _ => $"o Windows não conseguiu {what} o serviço (código {code})."
    };

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}

/// <summary>Pergunta ao Windows se este processo roda com poder de administrador.</summary>
public sealed class WindowsElevationProbe : IElevationProbe
{
    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
