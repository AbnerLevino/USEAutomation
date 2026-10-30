using AutomationPlatform.Contracts;
using AutomationPlatform.Diagnostics.Printing;

namespace AutomationPlatform.Platform.Windows.Elevation;

/// <summary>Um pedido já validado ao executor elevado: uma ação da lista fixa e, se couber, uma impressora.</summary>
public sealed record ElevatedRequest(string ActionId, string? Printer);

/// <summary>
/// O "contrato" entre o app e o executor elevado: quais ações existem, como o pedido é escrito na linha de comando e
/// o que cada código de saída significa. Usado pelos dois lados, então não há duas versões da mesma regra.
/// <para>Decisão de desenho: o resultado volta SÓ pelo código de saída. Nada de arquivo temporário ou canal em que um
/// usuário comum pudesse interferir (um processo elevado que escreve em caminho escolhido por usuário comum é um
/// risco clássico). O app confirma o efeito lendo o estado de novo, de forma independente.</para>
/// </summary>
public static class ElevatedProtocol
{
    /// <summary>A lista fixa. Qualquer outra ação é recusada. Cresce na fase F, uma ação por vez, com teste.</summary>
    public static IReadOnlyList<string> AllowedActions { get; } = [PrinterActionIds.RestartSpooler];

    /// <summary>As ações que recebem o nome de uma impressora. As demais recusam o parâmetro. (Nenhuma ainda.)</summary>
    public static IReadOnlyList<string> ActionsWithPrinter { get; } = [];

    public const int Done = 0;
    public const int Failed = 10;
    /// <summary>Pedido inválido: ação fora da lista, parâmetro sobrando ou mal formado.</summary>
    public const int Refused = 20;
    /// <summary>A condição para agir não existe (mais).</summary>
    public const int PreconditionNotMet = 21;
    /// <summary>O processo não está elevado: nada foi feito.</summary>
    public const int NotElevated = 22;

    private const string ActionFlag = "--action";
    private const string PrinterFlag = "--printer";
    private const int MaxPrinterLength = 256;

    /// <summary>Lê a linha de comando do executor. Estrito: qualquer coisa fora do formato esperado devolve null.</summary>
    public static ElevatedRequest? Parse(string[] args)
    {
        string? action = null, printer = null;
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length) return null;
            switch (args[i])
            {
                case ActionFlag when action is null: action = args[i + 1]; break;
                case PrinterFlag when printer is null: printer = args[i + 1]; break;
                default: return null;   // flag desconhecida ou repetida
            }
        }
        if (action is null || !IsValidActionId(action)) return null;
        if (printer is not null && !IsValidPrinterName(printer)) return null;
        return new ElevatedRequest(action, printer);
    }

    /// <summary>Escreve o pedido para a linha de comando. Null se algum valor não for aceitável (nada é lançado).</summary>
    public static string? BuildArguments(string actionId, string? printer)
    {
        if (!IsValidActionId(actionId)) return null;
        if (printer is not null && !IsValidPrinterName(printer)) return null;
        return printer is null ? $"{ActionFlag} {actionId}" : $"{ActionFlag} {actionId} {PrinterFlag} \"{printer}\"";
    }

    /// <summary>Só letras minúsculas, dígitos, ponto e hífen, como "printing.spooler.restart".</summary>
    public static bool IsValidActionId(string id) =>
        id.Length is > 0 and <= 64 && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-')
        && !id.StartsWith('.') && !id.EndsWith('.') && !id.Contains("..", StringComparison.Ordinal);

    /// <summary>Sem aspas, sem caracteres de controle e sem barra invertida no fim (que "escaparia" a aspa de fechamento).</summary>
    public static bool IsValidPrinterName(string name) =>
        name.Length is > 0 and <= MaxPrinterLength && !name.Any(c => char.IsControl(c) || c == '"') && !name.EndsWith('\\');

    /// <summary>Traduz o código de saída do executor no resultado que o pipeline entende.</summary>
    public static ExecutionResult ToResult(int exitCode) => exitCode switch
    {
        Done => new ExecutionResult(ExecutionOutcome.Done),
        Refused => new ExecutionResult(ExecutionOutcome.Refused, "o executor recusou o pedido."),
        PreconditionNotMet => new ExecutionResult(ExecutionOutcome.Failed, "a condição para agir já não existe."),
        NotElevated => new ExecutionResult(ExecutionOutcome.ElevationDenied, "o executor não recebeu a aprovação do administrador."),
        Failed => new ExecutionResult(ExecutionOutcome.Failed, "o executor não conseguiu concluir a ação."),
        _ => new ExecutionResult(ExecutionOutcome.Failed, $"o executor terminou com um código inesperado ({exitCode}).")
    };
}
