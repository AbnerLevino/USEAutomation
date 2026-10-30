using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Platform.Windows.Elevation;

/// <summary>Controle de serviços do Windows. Uma porta pequena e nomeada, para o executor poder ser testado com uma versão falsa.</summary>
public interface IServiceControl
{
    /// <summary>Estado atual do serviço; null = o Windows não o encontrou.</summary>
    Task<ServiceState?> GetStateAsync(string serviceName, CancellationToken cancellationToken);

    /// <summary>Para (se estiver rodando) e inicia o serviço, esperando cada passo terminar.</summary>
    Task<ControlResult> RestartAsync(string serviceName, CancellationToken cancellationToken);
}

/// <summary>Diz se o processo atual está elevado (rodando com poder de administrador).</summary>
public interface IElevationProbe
{
    bool IsElevated { get; }
}

/// <summary>
/// O que roda DENTRO do processo elevado. Aceita só ações da lista fixa de <see cref="ElevatedProtocol"/>, com
/// parâmetros validados; não interpreta comando livre. Não confia no chamador: confere sozinho que está elevado e
/// revalida a condição de agir antes de mexer em qualquer coisa. Devolve um código de saída (ver o protocolo).
/// </summary>
public sealed class ElevatedActionRunner(IServiceControl services, IElevationProbe probe)
{
    /// <summary>O único serviço que esta ação mexe. Fixo no código, nunca vem do pedido.</summary>
    public const string SpoolerServiceName = "Spooler";

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        // 1. Pedido bem formado e da lista fixa; senão, recusa sem tocar em nada.
        if (ElevatedProtocol.Parse(args) is not { } request) return ElevatedProtocol.Refused;
        if (!ElevatedProtocol.AllowedActions.Contains(request.ActionId, StringComparer.Ordinal)) return ElevatedProtocol.Refused;

        // 1b. Parâmetro que a ação não aceita: recusa já aqui, para todo pedido inválido ter o mesmo código, elevado ou não.
        if (request.Printer is not null && !ElevatedProtocol.ActionsWithPrinter.Contains(request.ActionId, StringComparer.Ordinal))
            return ElevatedProtocol.Refused;

        // 2. Sem elevação de verdade não há o que fazer (ex.: alguém abriu o executor sem passar pela aprovação).
        if (!probe.IsElevated) return ElevatedProtocol.NotElevated;

        return request.ActionId switch
        {
            PrinterActionIds.RestartSpooler => await RestartSpoolerAsync(request, cancellationToken),
            _ => ElevatedProtocol.Refused   // na lista mas sem manipulador: recusa, nunca "tenta algo parecido"
        };
    }

    private async Task<int> RestartSpoolerAsync(ElevatedRequest request, CancellationToken ct)
    {
        if (request.Printer is not null) return ElevatedProtocol.Refused;   // esta ação não recebe parâmetro

        // Revalida por conta própria: o serviço existe?
        if (await services.GetStateAsync(SpoolerServiceName, ct) is null) return ElevatedProtocol.PreconditionNotMet;

        var result = await services.RestartAsync(SpoolerServiceName, ct);
        return result.Succeeded ? ElevatedProtocol.Done : ElevatedProtocol.Failed;
    }
}
