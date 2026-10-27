using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>
/// Fonte de dados do diagnóstico. A implementação real (WMI) fica em Platform.Windows;
/// os testes usam uma versão falsa. Os métodos devolvem o resultado da consulta em vez
/// de lançar exceção para falhas esperadas (sem permissão, fonte fora do ar).
/// </summary>
public interface IPrintDeviceProvider
{
    /// <summary>Dispositivos Plug and Play relacionados a impressão que o Windows reconhece.</summary>
    Task<SourceResult<PrintDeviceInfo>> GetPrintDevicesAsync(CancellationToken cancellationToken);

    /// <summary>Filas de impressão instaladas (somente os nomes; nunca os documentos).</summary>
    Task<SourceResult<InstalledPrinterInfo>> GetInstalledPrintersAsync(CancellationToken cancellationToken);
}

/// <summary>Dispositivo de impressão reportado pelo Windows.</summary>
/// <param name="Name">Nome do dispositivo.</param>
/// <param name="DeviceId">Identificador do dispositivo no Windows.</param>
/// <param name="ErrorCode">Código do Gerenciador de Dispositivos; 0 = sem problema; null = o Windows não informou.</param>
public sealed record PrintDeviceInfo(string Name, string DeviceId, int? ErrorCode);

public sealed record InstalledPrinterInfo(string Name);

/// <summary>O que aconteceu ao consultar uma fonte e, se deu certo, os itens obtidos.</summary>
public sealed record SourceResult<T>(SourceStatus Status, IReadOnlyList<T> Items, string? Detail = null)
{
    public bool IsConsulted => Status == SourceStatus.Consulted;

    public static SourceResult<T> Ok(IReadOnlyList<T> items) => new(SourceStatus.Consulted, items);

    public static SourceResult<T> Fail(SourceStatus status, string detail) => new(status, [], detail);
}
