using AutomationPlatform.Contracts;

namespace AutomationPlatform.Diagnostics.Printing;

/// <summary>
/// Identificadores das ações que RESOLVERIAM cada causa. Nesta fase nada é executado: os ids
/// só dizem qual ação será ligada à causa nas fases seguintes (contrato de ação, executor elevado).
/// </summary>
public static class PrinterActionIds
{
    public const string RestartSpooler = "printing.spooler.restart";
    public const string ResumePrinter = "printing.printer.resume";
    public const string PrintTestPage = "printing.testpage.print";
    public const string SetPrinterOnline = "printing.printer.set-online";
    public const string CancelStuckJobs = "printing.queue.cancel-stuck";
    public const string SetDefaultPrinter = "printing.default.set";
}

/// <summary>Códigos estáveis das causas, para testes e para as fases seguintes.</summary>
public static class CauseCodes
{
    public const string SpoolerNotRunning = "SPOOLER_NOT_RUNNING";
    public const string PrinterPaused = "PRINTER_PAUSED";
    public const string PrinterOffline = "PRINTER_OFFLINE";
    public const string PrinterNoPort = "PRINTER_NO_PORT";
    public const string DeviceError = "DEVICE_ERROR";
    public const string EquipmentError = "EQUIPMENT_ERROR";
    public const string StuckJobs = "STUCK_JOBS";
    public const string DefaultIsVirtual = "DEFAULT_IS_VIRTUAL";
    public const string NoDefaultPrinter = "NO_DEFAULT_PRINTER";
    public const string EquipmentWarning = "EQUIPMENT_WARNING";
}

/// <summary>Uma causa possível, já ordenada por prioridade (Rank menor = bloqueia mais).</summary>
/// <param name="IsBlocking">true = explica por que nada sai; false = apenas um aviso (ex.: pouco toner).</param>
/// <param name="PrinterName">Fila afetada; null = vale para o sistema todo.</param>
/// <param name="CandidateActionId">Ação que resolveria a causa (<see cref="PrinterActionIds"/>); null = só orientação ao usuário.</param>
/// <param name="Guidance">O que o usuário pode fazer à mão agora.</param>
/// <param name="EvidenceIds">Ids das evidências (já com prefixo da verificação, ex.: "state:ev-2").</param>
public sealed record ProbableCause(
    string Code,
    int Rank,
    string Title,
    string Description,
    Confidence Confidence,
    Severity Severity,
    bool IsBlocking,
    string? PrinterName,
    string? CandidateActionId,
    string Guidance,
    IReadOnlyList<string> EvidenceIds);

/// <summary>
/// O que o cruzamento das verificações concluiu. <see cref="Primary"/> é a causa a tratar primeiro;
/// as demais causas ficam em <see cref="Causes"/>; os efeitos dela em <see cref="Consequences"/>.
/// </summary>
public sealed record ScenarioAnalysis(
    IReadOnlyList<ProbableCause> Causes,
    IReadOnlyList<string> Consequences,
    IReadOnlyList<string> TargetPrinters,
    IReadOnlyList<string> Notes)
{
    /// <summary>A causa bloqueante de menor Rank; null = nenhuma causa foi encontrada.</summary>
    public ProbableCause? Primary => Causes.FirstOrDefault(c => c.IsBlocking);
}
